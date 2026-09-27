namespace YueYinqiu.HpcRegistrar.Services;

public sealed record AuthorizedKey(string Fingerprint, string KeyLine);

public sealed class IsolationSpaceService(SshNetHpcClient hpc, string username)
{
    public async Task<string?> AddKeyAsync(string sub, string displayName, string key, CancellationToken cancellationToken = default)
    {
        var fingerprint = SshPublicKeyParser.GetFingerprint(key);
        if (fingerprint is null)
        {
            return "无法解析该公钥，请重新输入。";
        }

        var layout = new SpaceLayout(username, SpaceName.FromSub(sub));

        if (!await hpc.PathExistsAsync(layout.Space, cancellationToken))
        {
            await CreateSpaceFilesAsync(layout, cancellationToken);
        }

        if ((await ListKeysAsync(sub, cancellationToken)).Any(k => k.Fingerprint == fingerprint))
        {
            return "该公钥已存在。";
        }

        await hpc.AppendAuthorizedKeyAsync(layout.AuthorizedKeys, layout.AuthorizedKeyLine(key, displayName), cancellationToken);
        return null;
    }

    public async Task<bool> RemoveKeyAsync(string sub, string fingerprint, CancellationToken cancellationToken = default)
    {
        var layout = new SpaceLayout(username, SpaceName.FromSub(sub));
        var target = (await ListKeysAsync(sub, cancellationToken)).FirstOrDefault(k => k.Fingerprint == fingerprint);
        if (target is null)
        {
            return false;
        }

        await hpc.RemoveAuthorizedKeyAsync(layout.AuthorizedKeys, target.KeyLine, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AuthorizedKey>> ListKeysAsync(string sub, CancellationToken cancellationToken = default)
    {
        var layout = new SpaceLayout(username, SpaceName.FromSub(sub));
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

            var fingerprint = SshPublicKeyParser.GetFingerprint(trimmed[prefix.Length..]);
            if (fingerprint is not null)
            {
                result.Add(new AuthorizedKey(fingerprint, trimmed));
            }
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
