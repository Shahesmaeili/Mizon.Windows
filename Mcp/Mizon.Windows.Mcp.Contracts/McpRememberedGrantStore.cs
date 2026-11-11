using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mizon.Windows.Mcp.Contracts;

public sealed record McpRememberedGrant
{
    public string Id { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string ClientVersion { get; init; } = string.Empty;
    public bool AllowRead { get; init; }
    public bool AllowControl { get; init; }
    public bool AllowSubmit { get; init; }
    // Missing fields in an existing version-1 grant never authorize this new domain.
    public bool AllowAppearanceRead { get; init; }
    public bool AllowAppearanceControl { get; init; }
    public bool AllowNavigationRead { get; init; }
    public bool AllowNavigationControl { get; init; }
    public bool AllowTaskbarRead { get; init; }
    public bool AllowTaskbarControl { get; init; }
    public bool AllowMusicHomeRead { get; init; }
    public bool AllowMusicHomeControl { get; init; }
    public bool AllowMusicHomeSubmit { get; init; }
    public bool AllowPresenceRead { get; init; }
    public bool AllowWindowRead { get; init; }
    public bool AllowWindowControl { get; init; }
    public bool AllowVpnRead { get; init; }
    public bool AllowVpnControl { get; init; }
    public DateTimeOffset ApprovedAt { get; init; }
}

// Only the app's native consent handlers may call Save/Remove/Clear. There is no
// MCP operation that can write this store or promote a pending connection.
[SupportedOSPlatform("windows")]
public sealed class McpRememberedGrantStore : IDisposable
{
    private const string Purpose = "remembered-authentication-grants";
    private readonly string _path;
    private bool _disposed;

    public McpRememberedGrantStore(string filePath) => _path = Path.GetFullPath(filePath);

    public async Task<IReadOnlyList<McpRememberedGrant>> ReadAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var gate = await McpProtectedFile.AcquireAsync(_path, token).ConfigureAwait(false);
        return (await ReadCoreAsync(token).ConfigureAwait(false)).Grants.ToArray();
    }

    public Task SaveAsync(McpRememberedGrant grant, CancellationToken token = default)
    {
        Validate(grant);
        return ChangeAsync(list =>
        {
            list.RemoveAll(item => item.Id == grant.Id);
            list.Add(grant);
        }, token);
    }

    public Task RemoveAsync(string id, CancellationToken token = default)
    {
        if (!McpCredentialIdentity.IsValidId(id)) throw new ArgumentException("Invalid MCP identity.", nameof(id));
        return ChangeAsync(list => list.RemoveAll(item => item.Id == id), token);
    }

    public async Task ClearAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var gate = await McpProtectedFile.AcquireAsync(_path, token).ConfigureAwait(false);
        // Explicit native-UI revoke-all can also recover a corrupt store. Never
        // deserialize or trust old grants merely to remove all of them.
        await WriteCoreAsync(new(), token).ConfigureAwait(false);
    }

    private async Task ChangeAsync(Action<List<McpRememberedGrant>> change, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var gate = await McpProtectedFile.AcquireAsync(_path, token).ConfigureAwait(false);
        var document = await ReadCoreAsync(token).ConfigureAwait(false);
        change(document.Grants);
        Validate(document);
        await WriteCoreAsync(document, token).ConfigureAwait(false);
    }

    private async Task WriteCoreAsync(McpGrantDocument document, CancellationToken token)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document, McpPersistenceJsonContext.Default.McpGrantDocument);
        try { await McpProtectedFile.WriteAsync(_path, plaintext, Purpose, token).ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private async Task<McpGrantDocument> ReadCoreAsync(CancellationToken token)
    {
        var plaintext = await McpProtectedFile.ReadAsync(_path, Purpose, token).ConfigureAwait(false);
        if (plaintext is null) return new();
        try
        {
            var document = JsonSerializer.Deserialize(plaintext, McpPersistenceJsonContext.Default.McpGrantDocument)
                ?? throw new IOException("Mizon MCP remembered permissions are invalid.");
            Validate(document);
            return document;
        }
        catch (JsonException) { throw new IOException("Mizon MCP remembered permissions could not be read."); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static void Validate(McpGrantDocument document)
    {
        if (document.Version != 1 || document.Grants is null || document.Grants.Count > 256)
            throw new IOException("Mizon MCP remembered permissions have an unsupported format.");
        foreach (var grant in document.Grants) Validate(grant);
        if (document.Grants.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != document.Grants.Count)
            throw new IOException("Mizon MCP remembered permissions contain duplicate identities.");
    }

    private static void Validate(McpRememberedGrant grant)
    {
        if (grant is null || !McpCredentialIdentity.IsValidId(grant.Id)
            || string.IsNullOrWhiteSpace(grant.ClientName) || grant.ClientName.Length > 100
            || grant.ClientVersion is null || grant.ClientVersion.Length > 100
            || grant.AllowSubmit && !grant.AllowControl || grant.AllowMusicHomeSubmit && !grant.AllowMusicHomeControl
            || grant.ApprovedAt == default)
            throw new IOException("Mizon MCP remembered permission is invalid.");
    }

    public void Dispose() => _disposed = true;
}

internal sealed record McpGrantDocument
{
    public int Version { get; init; } = 1;
    public List<McpRememberedGrant> Grants { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(McpGrantDocument))]
internal sealed partial class McpPersistenceJsonContext : JsonSerializerContext;
