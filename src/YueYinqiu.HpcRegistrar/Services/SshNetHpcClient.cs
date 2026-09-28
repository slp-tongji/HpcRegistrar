using System.Text;
using Renci.SshNet;

namespace YueYinqiu.HpcRegistrar.Services;

public sealed class SshNetHpcClient : IDisposable
{
    private readonly SshClient client;

    public SshNetHpcClient(string host, int port, string username, FileInfo privateKeyPath, string hostKeyFingerprint)
    {
        var privateKey = new PrivateKeyFile(privateKeyPath.FullName);
        var connectionInfo = new Renci.SshNet.ConnectionInfo(
            host,
            port,
            username,
            new PrivateKeyAuthenticationMethod(username, privateKey));
        client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == hostKeyFingerprint;
        client.Connect();
    }

    public void Dispose() => client.Dispose();

    public async Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var (status, _) = await RunAsync("test -e \"$1\"", [path], cancellationToken);
        return status == 0;
    }

    public async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var (status, output) = await RunAsync("cat \"$1\"", [path], cancellationToken);
        if (status != 0)
        {
            throw new InvalidOperationException($"读取文件失败（退出码 {status?.ToString() ?? "未知"}）：{output}");
        }

        return output;
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync("mkdir -p \"$1\"", [path], cancellationToken);

    public async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync($"printf '%s' '{Encode(content)}' | base64 -d > \"$1\"", [path], cancellationToken);

    public async Task SetExecutableAsync(string path, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync("chmod 500 \"$1\"", [path], cancellationToken);

    public async Task CreateSymbolicLinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync("ln -s \"$2\" \"$1\"", [linkPath, targetPath], cancellationToken);

    public async Task AddAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync(
            $$"""
            flock -x 200 \
                bash -c \
                    'printf "%s\n" "$2" > "$1".tmp; [ -f "$1" ] && cat "$1" >> "$1".tmp; mv "$1".tmp "$1"' \
                _ "$1" "$2" \
            200>>"$1".lock
            """,
            [authorizedKeysPath, keyLine],
            cancellationToken);

    public async Task RemoveAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync(
            $$"""
            flock -x 200 \
                bash -c \
                    'grep -vF -- "$2" "$1" > "$1".tmp && mv "$1".tmp "$1"' \
                    _ "$1" "$2" \
            200>>"$1".lock
            """,
            [authorizedKeysPath, keyLine],
            cancellationToken);

    private async Task<(int? Status, string Output)> RunAsync(string script, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var setArgs = string.Join(' ', arguments.Select(a => $"\"$(printf '%s' '{Encode(a)}' | base64 -d)\""));
        using var cmd = client.CreateCommand($"set -- {setArgs}\n{script}");
        await cmd.ExecuteAsync(cancellationToken);
        var output = string.Concat(cmd.Result, cmd.Error);
        return (cmd.ExitStatus, output);
    }

    private async Task RunCheckedAsync(string script, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var (status, output) = await RunAsync(script, arguments, cancellationToken);
        if (status != 0)
        {
            throw new InvalidOperationException($"HPC 命令执行失败（退出码 {status?.ToString() ?? "未知"}）：{output}");
        }
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
