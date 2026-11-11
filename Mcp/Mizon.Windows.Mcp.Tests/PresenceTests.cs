using System.Security.Cryptography;
using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp.Tests;

internal static class PresenceTests
{
    public static async Task ScopeAndSnapshot()
    {
        var policy = new McpAccessPolicy();
        Check.That(!policy.CanExecute("get_presence_state"), "Pending consent allowed presence.");
        policy.Approve(true, true, true, true, true, true, true, true, true, true, true, true);
        Check.That(!policy.CanExecute("get_presence_state"), "Existing scopes granted new presence access.");
        policy.Approve(false, false, false, presenceRead: true);
        Check.That(policy.CanExecute("get_presence_state") && !policy.CanExecute("get_state")
            && !policy.CanExecute("get_music_home") && !policy.CanExecute("set_presence"),
            "Presence is not an independent read-only scope.");
        foreach (var status in new[] { "denied", "revoked", "disconnected" })
        {
            var reset = new McpAccessPolicy(); reset.Approve(false, false, false, presenceRead: true);
            if (status == "denied") reset.Deny(); else if (status == "revoked") reset.Revoke(); else reset.Disconnect();
            Check.That(!reset.AllowPresenceRead && !reset.CanExecute("get_presence_state"), "Consent reset retained presence.");
        }
        var expected = new McpPresenceState { HasSession = true, IsWindowActive = true, IsConnected = false,
            IsOnline = false, HasConfirmedState = false };
        using var stream = new MemoryStream();
        await McpPipeProtocol.WriteAsync(stream, new McpResponse { Success = true, Presence = expected }, McpJsonContext.Default.McpResponse, default);
        stream.Position = 0;
        var actual = await McpPipeProtocol.ReadAsync(stream, McpJsonContext.Default.McpResponse, default);
        Check.That(actual?.Presence == expected, "Presence flags changed across source-generated pipe framing.");
        var names = McpJsonContext.Default.McpPresenceState.Properties.Select(property => property.Name).Order(StringComparer.Ordinal);
        Check.That(names.SequenceEqual(new[] { "hasConfirmedState", "hasSession", "isConnected", "isOnline", "isWindowActive" }.Order(StringComparer.Ordinal)),
            "Presence output exposes unapproved extra data or lacks synchronization state.");
        var oldStatus = JsonSerializer.Deserialize("""{"id":"old","allowRead":true,"allowMusicHomeRead":true}""",
            McpJsonContext.Default.McpConnectionStatus);
        Check.That(oldStatus is { AllowPresenceRead: false }, "A status without the new field implicitly approved presence.");
    }

    public static async Task PersistentScope()
    {
        var path = Path.Combine(WindowsIdentity.StorageDirectory, "presence-" + Guid.NewGuid().ToString("N"), "grants.dat");
        Check.That(McpCredentialIdentity.TryGetId(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), out var id), "Fixture proof is invalid.");
        var grant = new McpRememberedGrant { Id = id, ClientName = "Synthetic presence", ApprovedAt = DateTimeOffset.UtcNow };
        using (var store = new McpRememberedGrantStore(path)) await store.SaveAsync(grant);
        using (var store = new McpRememberedGrantStore(path))
        {
            var restored = (await store.ReadAsync()).Single();
            Check.That(!restored.AllowPresenceRead, "Stored older/default grant gained presence.");
            await store.SaveAsync(restored with { AllowPresenceRead = true });
        }
        using (var store = new McpRememberedGrantStore(path))
        {
            var restored = (await store.ReadAsync()).Single();
            Check.That(restored.AllowPresenceRead && !restored.AllowRead && !restored.AllowControl && !restored.AllowMusicHomeRead,
                "Presence restoration changed unrelated scopes.");
            await store.RemoveAsync(id);
        }
        using var final = new McpRememberedGrantStore(path);
        Check.That((await final.ReadAsync()).Count == 0, "Revoked presence was restored.");
    }
}
