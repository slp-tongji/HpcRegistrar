namespace YueYinqiu.HpcRegistrar.Services;

public sealed record AuthorizedKey(string Fingerprint, string Key);

public sealed class IsolationSpaceService(SshNetHpcClient hpc, string username)
{
    public async Task<string?> AddKeyAsync(string sub, string key, CancellationToken cancellationToken = default)
    {
        var normalized = SshPublicKeyParser.Normalize(key);
        if (normalized is null)
        {
            return "无法解析该公钥，请重新输入。";
        }

        var layout = new SpaceLayout(username, sub);

        if (!await hpc.PathExistsAsync(layout.Space, cancellationToken))
        {
            await CreateSpaceFilesAsync(layout, cancellationToken);
        }

        await hpc.AppendAuthorizedKeyAsync(layout.AuthorizedKeys, layout.AuthorizedKeyLine(normalized), cancellationToken);
        return null;
    }

    public async Task<bool> RemoveKeyAsync(string sub, string key, CancellationToken cancellationToken = default)
    {
        var normalized = SshPublicKeyParser.Normalize(key);
        if (normalized is null)
        {
            return false;
        }

        var layout = new SpaceLayout(username, sub);
        await hpc.RemoveAuthorizedKeyAsync(layout.AuthorizedKeys, layout.AuthorizedKeyLine(normalized), cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AuthorizedKey>> ListKeysAsync(string sub, CancellationToken cancellationToken = default)
    {
        var layout = new SpaceLayout(username, sub);
        var content = await hpc.ReadFileAsync(layout.AuthorizedKeys, cancellationToken);
        var prefix = $"command=\"{layout.SshCommand}\" ";

        var result = new List<AuthorizedKey>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var normalized = SshPublicKeyParser.Normalize(trimmed[prefix.Length..]);
            if (normalized is null)
            {
                continue;
            }

            var fingerprint = SshPublicKeyParser.GetFingerprint(normalized)!;
            result.Add(new AuthorizedKey(fingerprint, normalized));
        }

        return result;
    }

    private async Task CreateSpaceFilesAsync(SpaceLayout layout, CancellationToken cancellationToken)
    {
        await hpc.CreateDirectoryAsync(layout.Space, cancellationToken);

        await hpc.WriteFileAsync(layout.SshCommand, layout.SshCommandContent(), cancellationToken);
        await hpc.SetExecutableAsync(layout.SshCommand, cancellationToken);

        await hpc.WriteFileAsync(Path.Combine(layout.Space, ".bash_logout"), layout.BashLogoutContent(), cancellationToken);
        await hpc.WriteFileAsync(Path.Combine(layout.Space, ".bash_profile"), layout.BashProfileContent(), cancellationToken);
        await hpc.WriteFileAsync(Path.Combine(layout.Space, ".bashrc"), layout.BashrcContent(), cancellationToken);

        var spaceSsh = Path.Combine(layout.Space, ".ssh");
        await hpc.CreateDirectoryAsync(spaceSsh, cancellationToken);
        await hpc.WriteFileAsync(Path.Combine(spaceSsh, "authorized_keys"), layout.SpaceAuthorizedKeysContent(), cancellationToken);
        await hpc.WriteFileAsync(Path.Combine(spaceSsh, "config"), layout.SpaceSshConfigContent(), cancellationToken);

        await hpc.CreateDirectoryAsync(layout.Ssdfs, cancellationToken);
        await hpc.CreateSymbolicLinkAsync(Path.Combine(layout.Space, "ssdfs"), layout.Ssdfs, cancellationToken);
    }
}
