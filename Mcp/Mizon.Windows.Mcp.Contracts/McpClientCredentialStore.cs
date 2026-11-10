using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Mizon.Windows.Mcp.Contracts;

[SupportedOSPlatform("windows")]
public sealed class McpClientCredentialStore
{
    private readonly string _directory;

    public McpClientCredentialStore(string directory) => _directory = Path.GetFullPath(directory);

    public async Task<string> GetOrCreateAsync(string clientProfile, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(clientProfile, "N", out var profile) && !Guid.TryParseExact(clientProfile, "D", out profile)
            || profile == Guid.Empty)
            throw new ArgumentException("A configured MCP client profile is required.", nameof(clientProfile));
        // The profile is chosen in the host's local startup configuration, NEVER
        // from MCP-supplied metadata. Another client claiming the same name must
        // not automatically obtain an existing client's private proof.
        var key = profile.ToString("N");
        var path = Path.Combine(_directory, key + ".credential");
        await using var gate = await McpProtectedFile.AcquireAsync(path, cancellationToken).ConfigureAwait(false);
        var secret = await McpProtectedFile.ReadAsync(path, "client-credential/" + key, cancellationToken).ConfigureAwait(false);
        var create = secret is null;
        secret ??= RandomNumberGenerator.GetBytes(32);
        try
        {
            if (secret.Length != 32) throw new CryptographicException("Mizon MCP client credential has an invalid size.");
            if (create)
                await McpProtectedFile.WriteAsync(path, secret, "client-credential/" + key, cancellationToken).ConfigureAwait(false);
            return Convert.ToBase64String(secret);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
}
