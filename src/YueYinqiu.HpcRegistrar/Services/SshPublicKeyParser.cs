using System.Security.Cryptography;

namespace YueYinqiu.HpcRegistrar.Services;

public static class SshPublicKeyParser
{
    public static string? Normalize(string keyLine)
    {
        var parts = keyLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !IsKnownType(parts[0]))
        {
            return null;
        }

        try
        {
            Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        return $"{parts[0]} {parts[1]}";
    }

    public static string? GetFingerprint(string keyLine)
    {
        var normalized = Normalize(keyLine);
        if (normalized is null)
        {
            return null;
        }

        var blob = Convert.FromBase64String(normalized.Split(' ')[1]);
        var hash = SHA256.HashData(blob);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    private static bool IsKnownType(string type) => type switch
    {
        "ssh-rsa" => true,
        "ssh-ed25519" => true,
        "ecdsa-sha2-nistp256" => true,
        "ecdsa-sha2-nistp384" => true,
        "ecdsa-sha2-nistp521" => true,
        "sk-ssh-ed25519@openssh.com" => true,
        "sk-ecdsa-sha2-nistp256@openssh.com" => true,
        _ => false,
    };
}
