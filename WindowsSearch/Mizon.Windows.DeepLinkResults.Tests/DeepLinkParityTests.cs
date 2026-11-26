using System.Text.Json;
using Mizon.Windows.Services;

internal static class DeepLinkParityTests
{
    internal static void Run(Action<bool, string> check)
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "deep-links-v1.json"));
        using var contract = JsonDocument.Parse(file);
        var root = contract.RootElement;
        var version = root.GetProperty("protocolVersion").GetInt32();
        var requestId = root.GetProperty("requestId").GetGuid();
        check(version == AppDeepLinkResultProtocol.Version && requestId != Guid.Empty, "shared response version and correlation");

        var routeKeys = new HashSet<string>(StringComparer.Ordinal);
        var sections = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("routes").EnumerateArray())
        {
            var key = row.GetProperty("routeKey").GetString()!;
            var uri = row.GetProperty("canonicalUri").GetString()!;
            var requiresAuthentication = row.GetProperty("requiresAuthentication").GetBoolean();
            var stage = row.GetProperty("completionStage").GetString()!;
            var duration = row.GetProperty("timeoutSeconds").GetInt32();
            var publicId = ReadGuid(row, "publicId");
            var comment = ReadGuid(row, "commentPublicId");
            var section = ReadText(row, "settingsSection");
            var search = ReadText(row, "typedSearchQuery") ?? ReadText(row, "searchQuery");
            routeKeys.Add(key);
            if (section is not null) sections.Add(section);

            check(AppDeepLinkCatalog.TryCreate(key, publicId, comment, search, section, out var expected),
                "shared typed route " + key);
            foreach (var input in row.GetProperty("inputs").EnumerateArray())
                check(AppActivationRequest.TryParse(input.GetString(), out var actual) && actual == expected,
                    "shared raw route " + key);
            check(expected.ToUri() == uri && expected.RequiresAuthentication == requiresAuthentication,
                "shared canonical URI and sign-in gate " + key);
            check(AppDeepLinkResponseSession.TimeoutFor(expected) == TimeSpan.FromSeconds(duration),
                "shared response deadline " + key);

            using var session = new AppDeepLinkResponseSession(requestId);
            session.Complete(AppNavigationOutcome.ForDestination(expected, "Synthetic navigation success."));
            var receipt = session.Completion.GetAwaiter().GetResult();
            var json = AppDeepLinkResultProtocol.Serialize(receipt);
            check(AppDeepLinkResultProtocol.TryParse(json, requestId, out var parsed)
                && parsed is { Success: true, Data: { } data } && parsed.Code == stage
                && data.Destination == key && data.Uri == uri && data.CompletionStage == stage
                && data.PublicId == publicId && data.CommentPublicId == comment && data.SettingsSection == section,
                "shared response payload " + key);
            check(!AppDeepLinkResultProtocol.TryParse(json, Guid.NewGuid(), out _),
                "unrelated response is rejected " + key);
        }

        check(routeKeys.SetEquals(AppDeepLinkCatalog.Entries.Select(static entry => entry.Key)),
            "shared fixture covers the Windows route catalog");
        check(sections.SetEquals(AppDeepLinkCatalog.SettingsSections), "shared fixture covers every Windows settings section");
        foreach (var input in root.GetProperty("invalidUris").EnumerateArray())
            check(!AppActivationRequest.TryParse(input.GetString(), out _), "shared malformed route is rejected: " + input.GetString());
    }

    private static Guid? ReadGuid(JsonElement source, string key) =>
        source.TryGetProperty(key, out var value) ? value.GetGuid() : null;

    private static string? ReadText(JsonElement source, string key) =>
        source.TryGetProperty(key, out var value) ? value.GetString() : null;
}
