namespace YueYinqiu.HpcRegistrar.Services;

public sealed class IsolationSpaceService(SshNetHpcClient hpc, SpaceRepository repository, HpcOptions options) : IDisposable
{
    public void Dispose()
    {
        hpc.Dispose();
        repository.Dispose();
    }

    public async Task<string?> AddKeyAsync(string sub, string displayName, string key, CancellationToken cancellationToken = default)
    {
        var fingerprint = SshPublicKeyParser.GetFingerprint(key);
        if (fingerprint is null)
        {
            return "无法解析该公钥，请重新输入。";
        }

        var layout = new SpaceLayout(options, SpaceName.FromSub(sub));
        var space = repository.FindByOwner(sub);

        if (space is null)
        {
            if (!await hpc.PathExistsAsync(layout.Space, cancellationToken))
            {
                await CreateSpaceFilesAsync(layout, cancellationToken);
            }
            space = new SpaceOwnership(layout.Name, sub, []);
        }
        else if (space.Keys.Any(k => k.Fingerprint == fingerprint))
        {
            return "该公钥已存在。";
        }

        var keyLine = layout.AuthorizedKeyLine(key, displayName);
        await hpc.AppendAuthorizedKeyAsync(layout.AuthorizedKeys, keyLine, cancellationToken);

        space = space with { Keys = [.. space.Keys, new SpaceKey(fingerprint, key, keyLine)] };
        repository.Upsert(space);

        return null;
    }

    public async Task<bool> RemoveKeyAsync(string sub, string fingerprint, CancellationToken cancellationToken = default)
    {
        var space = repository.FindByOwner(sub);
        if (space is null)
        {
            return false;
        }

        var key = space.Keys.FirstOrDefault(k => k.Fingerprint == fingerprint);
        if (key is null)
        {
            return false;
        }

        var layout = new SpaceLayout(options, space.Name);
        await hpc.RemoveAuthorizedKeyAsync(layout.AuthorizedKeys, key.KeyLine, cancellationToken);

        space = space with { Keys = space.Keys.Where(k => k.Fingerprint != fingerprint).ToList() };
        repository.Upsert(space);

        return true;
    }

    public SpaceOwnership? FindOwn(string sub) => repository.FindByOwner(sub);

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
