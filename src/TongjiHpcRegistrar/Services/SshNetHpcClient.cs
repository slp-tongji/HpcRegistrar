using System.IO;
using System.Text;
using Renci.SshNet;

namespace TongjiHpcRegistrar.Services;

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
        var (status, _) = await RunAsync("/usr/bin/test -e \"$1\"", [path], cancellationToken);
        return status == 0;
    }

    public async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var (status, output) = await RunAsync("/usr/bin/cat \"$1\"", [path], cancellationToken);
        if (status != 0)
        {
            throw new InvalidOperationException($"读取文件失败（退出码 {status?.ToString() ?? "未知"}）：{output}");
        }

        return output;
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync("/usr/bin/mkdir -p \"$1\"", [path], cancellationToken);

    public async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync($"/usr/bin/printf '%s' '{Encode(content)}' | /usr/bin/base64 -d > \"$1\"", [path], cancellationToken);

    public async Task ChmodAsync(string path, UnixFileMode mode, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync("/usr/bin/chmod \"$2\" \"$1\"", [path, Convert.ToString((int)mode, 8)], cancellationToken);

    public async Task CreateSymbolicLinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync("/usr/bin/ln -s \"$2\" \"$1\"", [linkPath, targetPath], cancellationToken);

    public async Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync("/usr/bin/mv \"$1\" \"$2\"", [sourcePath, targetPath], cancellationToken);

    public async Task GenerateSshKeyAsync(string path, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync("/usr/bin/ssh-keygen -t ed25519 -f \"$1\" -N ''", [path], cancellationToken);

    public async Task AddAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync(
            $$"""
            /usr/bin/flock -x "$1".lock \
                /bin/bash -c \
                    '/usr/bin/printf "%s\n" "$2" > "$1".tmp && /usr/bin/cat "$1" >> "$1".tmp && /usr/bin/chmod 644 "$1".tmp && /usr/bin/mv "$1".tmp "$1"' \
                    _ "$1" "$2"
            """,
            [authorizedKeysPath, keyLine],
            cancellationToken);

    public async Task RemoveAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default) =>
        await RunCheckedAsync(
            $$"""
            /usr/bin/flock -x "$1".lock \
                /bin/bash -c \
                    '/usr/bin/grep -vxF -- "$2" "$1" > "$1".tmp && /usr/bin/chmod 644 "$1".tmp && /usr/bin/mv "$1".tmp "$1"' \
                    _ "$1" "$2"
            """,
            [authorizedKeysPath, keyLine],
            cancellationToken);

    private async Task<(int? Status, string Output)> RunAsync(string script, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var setArgs = string.Join(' ', arguments.Select(a => $"\"$(/usr/bin/printf '%s' '{Encode(a)}' | /usr/bin/base64 -d)\""));
        using var cmd = client.CreateCommand($"set -- {setArgs}\n{script}");
        cmd.CommandTimeout = TimeSpan.FromSeconds(30);
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
