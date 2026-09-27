using System.Text;
using Renci.SshNet;

namespace YueYinqiu.HpcRegistrar.Services;

public sealed class SshNetHpcClient : IHpcClient
{
    private readonly HpcOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private SshClient? client;

    public SshNetHpcClient(HpcOptions options)
    {
        this.options = options;
    }

    public void Dispose()
    {
        gate.Dispose();
        client?.Dispose();
    }

    public async Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var (status, _) = await RunAsync($"test -e {ShellQuote(path)}", cancellationToken);
        return status == 0;
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        await RunCheckedAsync($"mkdir -p {ShellQuote(path)}", cancellationToken);
    }

    public async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        await RunCheckedAsync($"printf '%s' '{encoded}' | base64 -d > {ShellQuote(path)}", cancellationToken);
    }

    public async Task SetExecutableAsync(string path, CancellationToken cancellationToken = default)
    {
        await RunCheckedAsync($"chmod 500 {ShellQuote(path)}", cancellationToken);
    }

    public async Task CreateSymbolicLinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default)
    {
        await RunCheckedAsync($"ln -s {ShellQuote(targetPath)} {ShellQuote(linkPath)}", cancellationToken);
    }

    public async Task AppendAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(keyLine));
        var path = ShellQuote(authorizedKeysPath);
        await RunCheckedAsync(
            $"""
            key=$(printf '%s' '{encoded}' | base64 -d)
            ( flock -x 200
              grep -qF -- "$key" {path} || printf '%s\n' "$key" >> {path}
            ) 200>>{path}
            """,
            cancellationToken);
    }

    public async Task RemoveAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(keyLine));
        var path = ShellQuote(authorizedKeysPath);
        await RunCheckedAsync(
            $"""
            key=$(printf '%s' '{encoded}' | base64 -d)
            ( flock -x 200
              grep -vF -- "$key" {path} > {path}.tmp && mv {path}.tmp {path}
            ) 200>>{path}
            """,
            cancellationToken);
    }

    private async Task<(int? Status, string Output)> RunAsync(string command, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            using var cmd = client!.CreateCommand(command);
            await cmd.ExecuteAsync(cancellationToken);
            var output = string.Concat(cmd.Result, cmd.Error);
            return (cmd.ExitStatus, output);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RunCheckedAsync(string command, CancellationToken cancellationToken)
    {
        var (status, output) = await RunAsync(command, cancellationToken);
        if (status != 0)
        {
            throw new InvalidOperationException($"HPC 命令执行失败（退出码 {status?.ToString() ?? "未知"}）：{output}");
        }
    }

    private void EnsureConnected()
    {
        if (client is null)
        {
            var privateKey = new PrivateKeyFile(options.PrivateKeyPath);
            var connectionInfo = new Renci.SshNet.ConnectionInfo(
                options.Host,
                options.Port,
                options.Username,
                new PrivateKeyAuthenticationMethod(options.Username, privateKey));
            client = new SshClient(connectionInfo);
        }

        if (!client.IsConnected)
        {
            client.Connect();
        }
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
