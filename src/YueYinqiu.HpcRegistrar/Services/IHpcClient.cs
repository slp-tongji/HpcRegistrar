namespace YueYinqiu.HpcRegistrar.Services;

public interface IHpcClient : IDisposable
{
    Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default);

    Task SetExecutableAsync(string path, CancellationToken cancellationToken = default);

    Task CreateSymbolicLinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default);

    Task AppendAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default);

    Task RemoveAuthorizedKeyAsync(string authorizedKeysPath, string keyLine, CancellationToken cancellationToken = default);
}
