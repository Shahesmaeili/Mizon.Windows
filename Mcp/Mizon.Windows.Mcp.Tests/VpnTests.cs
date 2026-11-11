using System.Security.Cryptography;
using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp.Tests;

internal static class VpnTests
{
    public static async Task ScopeAndSnapshot()
    {
        var policy = new McpAccessPolicy();
        policy.Approve(true, true, true, true, true, true, true, true, true, true, true, true, true, true, true);
        Check.That(!policy.CanExecute("get_vpn_state") && !policy.CanExecute("set_vpn_particles")
            && !policy.CanExecute("open_vpn_settings"), "Existing domains granted VPN access.");
        policy.Approve(false, false, false, vpnRead: true);
        Check.That(policy.CanExecute("get_vpn_state") && !policy.CanExecute("set_vpn_particles") && !policy.CanExecute("open_vpn_settings"), "VPN read implied control.");
        policy.Approve(false, false, false, vpnControl: true);
        Check.That(!policy.CanExecute("get_vpn_state") && policy.CanExecute("set_vpn_particles") && policy.CanExecute("open_vpn_settings")
            && !policy.CanExecute("set_appearance") && !policy.CanExecute("music_home_action"), "VPN control leaked scopes.");
        foreach (var reset in new Action<McpAccessPolicy>[] { p => p.Deny(), p => p.Revoke(), p => p.Disconnect() })
        {
            var scoped = new McpAccessPolicy(); scoped.Approve(false, false, false, vpnRead: true, vpnControl: true);
            reset(scoped);
            Check.That(!scoped.AllowVpnRead && !scoped.AllowVpnControl && !scoped.CanExecute("open_vpn_settings"), "Permission reset retained VPN access.");
        }
        using var stream = new MemoryStream();
        var expected = new McpVpnState { IsAvailable = true, NetworkStatus = "local", ParticlesEnabled = true, MotionAllowed = false,
            IsLoading = true, IsPreferenceSaved = false, LastErrorCode = "preference_save_failed" };
        await McpPipeProtocol.WriteAsync(stream, new McpResponse { Success = true, Vpn = expected }, McpJsonContext.Default.McpResponse, default);
        stream.Position = 0;
        Check.That((await McpPipeProtocol.ReadAsync(stream, McpJsonContext.Default.McpResponse, default))?.Vpn == expected,
            "Source-generated framing changed VPN state.");
        Check.That(McpJsonContext.Default.McpVpnState.Properties.Select(p => p.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { "isAvailable", "isBusy", "isConfigured", "isConnected", "isLoading", "isPreferenceSaved", "lastErrorCode",
                "motionAllowed", "networkStatus", "particlesEnabled" }),
            "VPN snapshot exposed undeclared data.");
        var old = JsonSerializer.Deserialize("""{"allowAppearanceRead":true,"allowAppearanceControl":true}""", McpJsonContext.Default.McpConnectionStatus);
        Check.That(old is { AllowVpnRead: false, AllowVpnControl: false }, "Old permissions implicitly authorized VPN.");
    }

    public static async Task PersistentScopes()
    {
        var path = Path.Combine(WindowsIdentity.StorageDirectory, "vpn-" + Guid.NewGuid().ToString("N"), "grants.dat");
        McpCredentialIdentity.TryGetId(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), out var id);
        using (var store = new McpRememberedGrantStore(path))
            await store.SaveAsync(new() { Id = id, ClientName = "Synthetic VPN", ApprovedAt = DateTimeOffset.UtcNow, AllowAppearanceControl = true });
        using (var store = new McpRememberedGrantStore(path))
        {
            var grant = (await store.ReadAsync()).Single();
            Check.That(!grant.AllowVpnRead && !grant.AllowVpnControl, "Default stored consent gained VPN access.");
            await store.SaveAsync(grant with { AllowAppearanceControl = false, AllowVpnControl = true });
        }
        using (var store = new McpRememberedGrantStore(path))
        {
            var grant = (await store.ReadAsync()).Single();
            Check.That(grant.AllowVpnControl && !grant.AllowVpnRead && !grant.AllowAppearanceControl, "Persisted VPN control changed other scopes.");
            await store.RemoveAsync(id);
        }
        using var final = new McpRememberedGrantStore(path);
        Check.That((await final.ReadAsync()).Count == 0, "Revoked VPN grant was restored.");
    }
}
