namespace TongjiHpcRegistrar.Services;

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
        await SpaceFilesCreator.EnsureAsync(hpc, layout, cancellationToken);

        await hpc.AddAuthorizedKeyAsync(layout.OriginalAuthorizedKeysPath, $"{layout.AuthorizedKeyPrefix}{normalized}", cancellationToken);
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
        using var hpc = hpcFactory();
        await hpc.RemoveAuthorizedKeyAsync(layout.OriginalAuthorizedKeysPath, $"{layout.AuthorizedKeyPrefix}{normalized}", cancellationToken);
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

            var key = line[prefix.Length..];
            var normalized = SshPublicKeyParser.Normalize(key);
            if (normalized != key)
            {
                continue;
            }

            result.Add(new AuthorizedKey(normalized));
        }

        return result;
    }
}
