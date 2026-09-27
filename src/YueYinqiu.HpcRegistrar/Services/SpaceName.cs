using System.Security.Cryptography;
using System.Text;

namespace YueYinqiu.HpcRegistrar.Services;

public static class SpaceName
{
    public static string FromSub(string sub)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sub));
        return "s" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
