using System.Text.RegularExpressions;

namespace YueYinqiu.HpcRegistrar.Services;

public sealed class IsolationSpaceService(IHpcClient hpc, SpaceRepository repository, HpcOptions options)
{
    private static readonly Regex NamePattern = new("^[a-zA-Z][a-zA-Z0-9_-]{3,}$", RegexOptions.Compiled);

    public async Task<string?> CreateAsync(string name, string contact, string key, string owner, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (!NamePattern.IsMatch(name))
        {
            return "隔离空间名称应当由字母、数字、下划线或减号组成。首位只能是字母，最少四个字符。";
        }

        var figure = SshPublicKeyParser.GetFingerprint(key);
        if (figure is null)
        {
            return "无法解析该公钥，请重新输入。";
        }

        if (repository.FindByName(name) is not null)
        {
            return "该名称的隔离空间已存在。";
        }

        var layout = new SpaceLayout(options, name);

        if (!await hpc.PathExistsAsync(layout.Space, cancellationToken))
        {
            await CreateSpaceFilesAsync(layout, cancellationToken);
        }

        var keyLine = layout.AuthorizedKeyLine(key, contact);
        await hpc.AppendAuthorizedKeyAsync(layout.AuthorizedKeys, keyLine, cancellationToken);

        repository.Insert(new SpaceOwnership(name, owner, contact, figure, key, keyLine));
        return null;
    }

    public async Task<bool> DeleteOwnAsync(string name, string owner, CancellationToken cancellationToken = default)
    {
        var ownership = repository.FindByName(name);
        if (ownership is null || ownership.Owner != owner)
        {
            return false;
        }

        return await DeleteAnyAsync(name, cancellationToken);
    }

    public async Task<bool> DeleteAnyAsync(string name, CancellationToken cancellationToken = default)
    {
        var ownership = repository.FindByName(name);
        if (ownership is null)
        {
            return false;
        }

        var layout = new SpaceLayout(options, name);
        await hpc.RemoveAuthorizedKeyAsync(layout.AuthorizedKeys, ownership.KeyLine, cancellationToken);

        repository.Delete(name);
        return true;
    }

    public IEnumerable<SpaceOwnership> ListOwn(string owner) => repository.FindByOwner(owner);

    public IEnumerable<SpaceOwnership> ListAll() => repository.FindAll();

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
