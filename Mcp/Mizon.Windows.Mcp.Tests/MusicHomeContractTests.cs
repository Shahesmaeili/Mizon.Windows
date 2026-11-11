using System.Security.Cryptography;
using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp.Tests;

internal static class MusicHomeContractTests
{
    public static Task Permissions()
    {
        string[] operations = ["get_music_home", "refresh_music_home", "music_home_action", "music_home_submit"];
        var oldScopes = new McpAccessPolicy();
        oldScopes.Approve(true, true, true, true, true, true, true, true, true);
        foreach (var operation in operations)
            Check.That(!oldScopes.CanExecute(operation), "Earlier authentication/settings/navigation scopes authorized music home.");
        for (var flags = 0; flags < 8; flags++)
        {
            var read = (flags & 1) != 0;
            var control = (flags & 2) != 0;
            var submit = (flags & 4) != 0;
            foreach (var reset in new[] { "denied", "revoked", "disconnected" })
            {
                var policy = new McpAccessPolicy();
                foreach (var operation in operations) Check.That(!policy.CanExecute(operation), "Pending music scope was usable.");
                policy.Approve(false, false, false, musicHomeRead: read, musicHomeControl: control, musicHomeSubmit: submit);
                Check.That(policy.CanExecute("get_music_home") == read, "Music read depends on another scope.");
                Check.That(policy.CanExecute("refresh_music_home") == control && policy.CanExecute("music_home_action") == control,
                    "Music control depends on read/submit or failed to grant controls.");
                Check.That(policy.CanExecute("music_home_submit") == (control && submit), "Music submission bypassed independent consent.");
                Check.That(!policy.CanExecute("submit") && !policy.CanExecute("set_appearance") && !policy.CanExecute("open_destination"),
                    "Music scopes authorized an unrelated domain.");
                if (reset == "denied") policy.Deny();
                else if (reset == "revoked") policy.Revoke();
                else policy.Disconnect();
                Check.That(!policy.AllowMusicHomeRead && !policy.AllowMusicHomeControl && !policy.AllowMusicHomeSubmit,
                    "Reset retained a music scope.");
                foreach (var operation in operations) Check.That(!policy.CanExecute(operation), "Reset retained music operation access.");
            }
        }
        Check.That(!McpMusicHomeContract.IsValid(new() { Operation = "music_home_action", MusicHome = new() { Section = "featured", Action = "like", Enabled = true } }),
            "Raw pipe could disguise a like as a control operation.");
        Check.That(!McpMusicHomeContract.IsValid(new() { Operation = "music_home_action", MusicHome = new() { Section = "spotlight", Action = "follow", Enabled = true } }),
            "Raw pipe could disguise a follow as a control operation.");
        return Task.CompletedTask;
    }

    public static async Task SnapshotRoundTrip()
    {
        var snapshot = new McpResponse
        {
            Success = true, Code = "music_home_state",
            MusicHome = new()
            {
                IsAvailable = true, HasSession = true, Language = "fa-IR", Layout = "WideLayout",
                Sections = [new()
                {
                    Id = "recommended", Title = "کشف بعدی تو", Source = "demo", HasMore = true,
                    Items = Enumerable.Range(0, McpMusicHomeContract.MaxItemsPerSection).Select(index => new McpMusicHomeItemState
                    {
                        Id = "demo-" + index, Kind = "music", Title = "آهنگ نمونه", IsDemo = true,
                        ImageAvailable = true, Actions = ["open"], DurationSeconds = 240
                    }).ToArray()
                }],
                Hero = new() { Id = "fixture", IsPlaying = true, Volume = 0.7, PositionSeconds = 30.25, DurationSeconds = 240, Actions = ["pause", "like"] },
                Story = new() { Id = "fixture-story", IsLoading = true, CanNext = true }, SelectedDayIndex = 3
            }
        };
        using var stream = new MemoryStream();
        await McpPipeProtocol.WriteAsync(stream, snapshot, McpJsonContext.Default.McpResponse, default);
        stream.Position = 0;
        var actual = await McpPipeProtocol.ReadAsync(stream, McpJsonContext.Default.McpResponse, default);
        Check.That(actual?.MusicHome?.Sections.Single().Items.Length == 20 && actual.MusicHome.Hero?.PositionSeconds == 30.25,
            "Bounded home snapshot lost items or playback precision across framing.");
        var json = JsonSerializer.SerializeToElement(actual, McpJsonContext.Default.McpResponse);
        SchemaAssertions.Conforms(json, Mizon.Windows.Mcp.MizonAuthSchemas.Output);
        ProtocolTests.AssertNoSecrets(json);
        foreach (var metadata in new System.Text.Json.Serialization.Metadata.JsonTypeInfo[] { McpJsonContext.Default.McpMusicHomeState, McpJsonContext.Default.McpMusicHomeItemState,
            McpJsonContext.Default.McpMusicHomeHeroState, McpJsonContext.Default.McpMusicHomeStoryState })
            foreach (var property in metadata.Properties)
                Check.That(!property.Name.Contains("path", StringComparison.OrdinalIgnoreCase) && !property.Name.Contains("token", StringComparison.OrdinalIgnoreCase),
                    "Home output metadata exposes local paths or session credentials.");
    }

    public static async Task PersistentScopes()
    {
        var path = Path.Combine(WindowsIdentity.StorageDirectory, "music-home-" + Guid.NewGuid().ToString("N"), "grants.dat");
        Check.That(McpCredentialIdentity.TryGetId(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), out var id), "Invalid fixture identity.");
        var legacy = new McpRememberedGrant { Id = id, ClientName = "Synthetic old client", AllowRead = true, AllowControl = true, ApprovedAt = DateTimeOffset.UtcNow };
        using (var store = new McpRememberedGrantStore(path)) await store.SaveAsync(legacy);
        using (var restarted = new McpRememberedGrantStore(path))
        {
            var restored = (await restarted.ReadAsync()).Single();
            Check.That(!restored.AllowMusicHomeRead && !restored.AllowMusicHomeControl && !restored.AllowMusicHomeSubmit,
                "Existing approval gained new music permissions.");
            await restarted.SaveAsync(restored with { AllowMusicHomeRead = true, AllowMusicHomeControl = true, AllowMusicHomeSubmit = false });
        }
        using (var restarted = new McpRememberedGrantStore(path))
        {
            var restored = (await restarted.ReadAsync()).Single();
            Check.That(restored.AllowMusicHomeRead && restored.AllowMusicHomeControl && !restored.AllowMusicHomeSubmit,
                "Restart broadened or dropped exact music scopes.");
            await Check.Throws<IOException>(() => restarted.SaveAsync(restored with { AllowMusicHomeControl = false, AllowMusicHomeSubmit = true }));
            await restarted.RemoveAsync(id);
        }
        using var final = new McpRememberedGrantStore(path);
        Check.That((await final.ReadAsync()).Count == 0, "Revocation restored music grants after restart.");
    }
}
