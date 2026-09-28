namespace HpcRegistrar.Services;

using System.Buffers;

public static class SshPublicKeyParser
{
    public static string? Normalize(string keyLine)
    {
        var parts = keyLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !IsKnownType(parts[0]))
        {
            return null;
        }

        if (parts[1].AsSpan().ContainsAnyExcept(base64Characters))
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

    private static readonly SearchValues<char> base64Characters = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/="
    );

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
