using System.Security.Cryptography;

namespace Mizon.Windows.Mcp.Contracts;

public static class McpCredentialIdentity
{
    public static bool TryGetId(string? credential, out string id)
    {
        id = string.Empty;
        if (credential is not { Length: 44 }) return false;
        Span<byte> secret = stackalloc byte[32];
        try
        {
            if (!Convert.TryFromBase64String(credential, secret, out var length) || length != secret.Length) return false;
            id = Convert.ToHexStringLower(SHA256.HashData(secret));
            return true;
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    public static bool IsValidId(string? id) => id is { Length: 64 }
        && id.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
