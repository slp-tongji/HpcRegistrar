namespace YueYinqiu.HpcRegistrar.Services;

public sealed record AuthorizedKey(string Key);

public sealed class IsolationSpaceService(Func<SshNetHpcClient> hpcFactory, string username)
{
    public async Task<string?> AddKeyAsync(string sub, string key, CancellationToken cancellationToken = default)
    {
        var normalized = SshPublicKeyParser.Normalize(key);
        if (normalized is null)
        {
            return "无法解析该公钥，请重新输入。";
        }

        var layout = new SpaceLayout(username, sub);

        using var hpc = hpcFactory();
        if (!await hpc.PathExistsAsync(layout.SpaceHomePath, cancellationToken))
        {
            await CreateSpaceFilesAsync(hpc, layout, cancellationToken);
        }

        await hpc.AddAuthorizedKeyAsync(layout.OriginalAuthorizedKeysPath, $"{layout.AuthorizedKeyPrefix}{normalized}", cancellationToken);
        return null;
    }

    public async Task<bool> RemoveKeyAsync(string sub, string key, CancellationToken cancellationToken = default)
    {
        var layout = new SpaceLayout(username, sub);
        using var hpc = hpcFactory();
        await hpc.RemoveAuthorizedKeyAsync(layout.OriginalAuthorizedKeysPath, $"{layout.AuthorizedKeyPrefix}{key}", cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AuthorizedKey>> ListKeysAsync(string sub, CancellationToken cancellationToken = default)
    {
        var layout = new SpaceLayout(username, sub);
        using var hpc = hpcFactory();
        if (!await hpc.PathExistsAsync(layout.OriginalAuthorizedKeysPath, cancellationToken))
        {
            return [];
        }

        var content = await hpc.ReadFileAsync(layout.OriginalAuthorizedKeysPath, cancellationToken);
        var prefix = layout.AuthorizedKeyPrefix;

        var result = new List<AuthorizedKey>();
        foreach (var line in content.Split('\n'))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(new AuthorizedKey(line[prefix.Length..]));
        }

        return result;
    }

    private async Task CreateSpaceFilesAsync(SshNetHpcClient hpc, SpaceLayout layout, CancellationToken cancellationToken)
    {
        await hpc.CreateDirectoryAsync(layout.SpaceHomePath, cancellationToken);

        await hpc.WriteFileAsync(layout.SshCommandPath, layout.SshCommandContent, cancellationToken);
        await hpc.SetExecutableAsync(layout.SshCommandPath, cancellationToken);

        await hpc.WriteFileAsync(Path.Combine(layout.SpaceHomePath, ".bash_logout"), layout.BashLogoutContent, cancellationToken);
        await hpc.WriteFileAsync(Path.Combine(layout.SpaceHomePath, ".bash_profile"), layout.BashProfileContent, cancellationToken);
        await hpc.WriteFileAsync(Path.Combine(layout.SpaceHomePath, ".bashrc"), layout.BashrcContent, cancellationToken);

        var spaceSsh = Path.Combine(layout.SpaceHomePath, ".ssh");
        await hpc.CreateDirectoryAsync(spaceSsh, cancellationToken);
        await hpc.WriteFileAsync(Path.Combine(spaceSsh, "authorized_keys"), layout.SpaceAuthorizedKeysContent, cancellationToken);
        await hpc.WriteFileAsync(Path.Combine(spaceSsh, "config"), layout.SpaceSshConfigContent, cancellationToken);

        await hpc.CreateDirectoryAsync(layout.SpaceSsdfsPath, cancellationToken);
        await hpc.CreateSymbolicLinkAsync(Path.Combine(layout.SpaceHomePath, "ssdfs"), layout.SpaceSsdfsPath, cancellationToken);
    }
}
