using System.Security.Cryptography;
using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp.Tests;

internal static class WindowPresentationTests
{
    public static async Task ScopeAndSnapshot()
    {
        var policy = new McpAccessPolicy();
        policy.Approve(true, true, true, true, true, true, true, true, true, true, true, true, true);
        Check.That(!policy.CanExecute("get_window_state") && !policy.CanExecute("set_fullscreen"), "Older scopes granted new window access.");
        policy.Approve(false, false, false, windowRead: true);
        Check.That(policy.CanExecute("get_window_state") && !policy.CanExecute("set_fullscreen"), "Window read implied control.");
        policy.Approve(false, false, false, windowControl: true);
        Check.That(!policy.CanExecute("get_window_state") && policy.CanExecute("set_fullscreen")
            && !policy.CanExecute("set_appearance") && !policy.CanExecute("music_home_action"), "Window control leaked into other scopes.");
        foreach (var reset in new Action<McpAccessPolicy>[] { p => p.Deny(), p => p.Revoke(), p => p.Disconnect() })
        {
            var scoped = new McpAccessPolicy(); scoped.Approve(false, false, false, windowRead: true, windowControl: true);
            reset(scoped);
            Check.That(!scoped.AllowWindowRead && !scoped.AllowWindowControl
                && !scoped.CanExecute("set_fullscreen"), "Permission reset retained window access.");
        }
        using var stream = new MemoryStream();
        var expected = new McpWindowPresentationState { IsAvailable = true, FullScreen = true, ControlsVisible = false };
        await McpPipeProtocol.WriteAsync(stream, new McpResponse { Success = true, Window = expected }, McpJsonContext.Default.McpResponse, default);
        stream.Position = 0;
        Check.That((await McpPipeProtocol.ReadAsync(stream, McpJsonContext.Default.McpResponse, default))?.Window == expected,
            "Source-generated protocol changed window presentation state.");
        Check.That(McpJsonContext.Default.McpWindowPresentationState.Properties.Select(p => p.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { "controlsVisible", "fullScreen", "isAvailable" }), "Window snapshot exposed undeclared data.");
        var old = JsonSerializer.Deserialize("""{"allowAppearanceRead":true,"allowAppearanceControl":true}""", McpJsonContext.Default.McpConnectionStatus);
        Check.That(old is { AllowWindowRead: false, AllowWindowControl: false }, "Old grant fields implicitly authorized new window tools.");
    }

    public static async Task PersistentScopes()
    {
        var path = Path.Combine(WindowsIdentity.StorageDirectory, "window-" + Guid.NewGuid().ToString("N"), "grants.dat");
        McpCredentialIdentity.TryGetId(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), out var id);
        using (var store = new McpRememberedGrantStore(path))
            await store.SaveAsync(new() { Id = id, ClientName = "Synthetic window", ApprovedAt = DateTimeOffset.UtcNow, AllowAppearanceControl = true });
        using (var store = new McpRememberedGrantStore(path))
        {
            var grant = (await store.ReadAsync()).Single();
            Check.That(!grant.AllowWindowRead && !grant.AllowWindowControl, "Default stored consent gained window access.");
            await store.SaveAsync(grant with { AllowAppearanceControl = false, AllowWindowControl = true });
        }
        using (var store = new McpRememberedGrantStore(path))
        {
            var grant = (await store.ReadAsync()).Single();
            Check.That(grant.AllowWindowControl && !grant.AllowWindowRead && !grant.AllowAppearanceControl,
                "Persisted window control changed independent grants.");
            await store.RemoveAsync(id);
        }
        using var final = new McpRememberedGrantStore(path);
        Check.That((await final.ReadAsync()).Count == 0, "Revoked window consent was restored.");
    }
}
