using YueYinqiu.HpcRegistrar.Services;

namespace YueYinqiu.HpcRegistrar.E2E.MockHpc;

public sealed class MockHpcClient : IHpcClient
{
    public Dictionary<string, string> Files { get; } = new();

    public HashSet<string> Directories { get; } = new();

    public HashSet<string> Executables { get; } = new();

    public Dictionary<string, string> SymbolicLinks { get; } = new();

    public Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var exists = Directories.Contains(path)
            || Files.Keys.Any(key => key.StartsWith(path + "/", StringComparison.Ordinal));
        return Task.FromResult(exists);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        Directories.Add(path);
        return Task.CompletedTask;
    }

    public Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        Files[path] = content;
        return Task.CompletedTask;
    }

    public Task SetExecutableAsync(string path, CancellationToken cancellationToken = default)
    {
        Executables.Add(path);
        return Task.CompletedTask;
    }

    public Task CreateSymbolicLinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default)
    {
        SymbolicLinks[linkPath] = targetPath;
        return Task.CompletedTask;
    }

    public Task AppendAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default)
    {
        var lines = Files.TryGetValue(authorizedKeysPath, out var existing)
            ? existing.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList()
            : [];
        if (!lines.Contains(keyLine))
        {
            lines.Add(keyLine);
        }

        Files[authorizedKeysPath] = string.Join('\n', lines) + "\n";
        return Task.CompletedTask;
    }

    public Task RemoveAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default)
    {
        if (Files.TryGetValue(authorizedKeysPath, out var existing))
        {
            var lines = existing.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line != keyLine)
                .ToList();
            Files[authorizedKeysPath] = string.Join('\n', lines) + "\n";
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
