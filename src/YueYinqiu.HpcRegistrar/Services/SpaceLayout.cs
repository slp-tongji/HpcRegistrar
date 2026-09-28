using System.Security.Cryptography;
using System.Text;

namespace YueYinqiu.HpcRegistrar.Services;

public sealed class SpaceLayout(string username, string sub)
{
    private const string shareHome = "/share/home";

    private const string ssdfsDatahome = "/ssdfs/datahome";

    public string SpaceName { get; } = "s" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(sub)).AsSpan(0, 8)).ToLowerInvariant();

    public string OriginalHomePath => Path.Combine(shareHome, username);

    public string SpaceHomePath => Path.Combine(OriginalHomePath, "data", SpaceName);

    public string SpaceSsdfsPath => Path.Combine(ssdfsDatahome, username, SpaceName);

    public string SshCommandPath => Path.Combine(SpaceHomePath, ".hpc-isolation", "ssh-command.sh");

    public string OriginalAuthorizedKeysPath => Path.Combine(OriginalHomePath, ".ssh", "authorized_keys");

    public string AuthorizedKeyPrefix => $"command=\"{SshCommandPath}\" ";
}
