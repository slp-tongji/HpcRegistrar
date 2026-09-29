using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace TongjiHpcRegistrar.Services;

public sealed class SpaceLayout
{
    private static readonly SearchValues<char> safeCharacters = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-"
    );

    private const string shareHome = "/share/home";

    private const string ssdfsDatahome = "/ssdfs/datahome";

    private readonly string username;

    public string Sub { get; }

    public string SpaceName { get; }

    public string OriginalHomePath => Path.Combine(shareHome, username);

    public string SpaceHomePath => Path.Combine(OriginalHomePath, "data", SpaceName);

    public string SpaceSsdfsPath => Path.Combine(ssdfsDatahome, username, SpaceName);

    public string IsolationDirectoryRelativePath => ".hpc-isolation";

    public string SshCommandRelativePath => Path.Combine(IsolationDirectoryRelativePath, "ssh-command.sh");

    public string OwnerFileRelativePath => Path.Combine(IsolationDirectoryRelativePath, "owner");

    public string SshCommandPath => Path.Combine(SpaceHomePath, SshCommandRelativePath);

    public string OriginalAuthorizedKeysPath => Path.Combine(OriginalHomePath, ".ssh", "authorized_keys");

    public string AuthorizedKeyPrefix => $"command=\"{SshCommandPath}\" ";

    public SpaceLayout(string username, string sub)
    {
        EnsureSafe(username, nameof(username));
        this.username = username;
        Sub = sub;

        SpaceName = "s" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sub)).AsSpan(0, 8)).ToLowerInvariant();
        EnsureSafe(SpaceName, nameof(SpaceName));
    }

    private static void EnsureSafe(string value, string paramName)
    {
        if (value.Length == 0 || value.AsSpan().ContainsAnyExcept(safeCharacters))
        {
            throw new ArgumentException(
                $"{paramName} 只能包含 [a-zA-Z0-9._-] 且不能为空。" +
                "该值会被用作 SSH 登录用户名、文件系统路径，并嵌入 authorized_keys 的 command= 选项" +
                "（该选项最终由 shell -c 执行），特殊字符可能导致命令注入或路径逃逸。",
                paramName);
        }
    }
}
