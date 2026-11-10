using Mizon.Windows.Services;

internal static class DeepLinkRouteTests
{
    internal static void Run(Action<bool, string> check)
    {
        var publicId = Guid.Parse("0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72");
        var commentId = Guid.Parse("0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a73");
        var expectedKeys = new[] { "home", "login", "register", "music", "movies", "apps", "vpn", "playlists",
            "playlist", "music-details", "music-play", "music-download", "music-comment", "client", "story", "activities", "settings", "search" };
        check(AppDeepLinkCatalog.Entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal).SetEquals(expectedKeys),
            "catalog describes all supported destinations without unknown routes");
        check(AppDeepLinkCatalog.Entries.Count == expectedKeys.Length, "catalog does not duplicate destinations");
        foreach (var prefix in new[] { "mizon://", "mizon://localhost/", "mizon://S-1-5-21-123-1001/localhost/" })
            foreach (var suffix in new[] { "welcome", "WELCOME", "welcome/", "welcome?", "welcome?action=play" })
                check(!AppActivationRequest.TryParse(prefix + suffix, out var request) && !request.HasDestination,
                    "internal Welcome page cannot be opened through a protocol alias");
        check(!AppDeepLinkCatalog.TryCreate("welcome", null, null, null, null, out _)
            && !AppDeepLinkCatalog.TryCreate("WELCOME", null, null, null, null, out _),
            "internal Welcome page is absent from the typed navigation builder");

        foreach (var entry in AppDeepLinkCatalog.Entries)
        {
            var needsId = entry.Key is "playlist" or "music-details" or "music-play" or "music-download" or "music-comment" or "client" or "story";
            var query = entry.Key == "search" ? "معین & Mizon + 🎵" : null;
            var comment = entry.Key == "music-comment" ? (Guid?)commentId : null;
            var section = entry.Key == "settings" ? "appearance" : null;
            check(AppDeepLinkCatalog.TryCreate(entry.Key, needsId ? publicId : null, comment, query, section, out var request),
                "typed builder accepts complete " + entry.Key);
            var canonical = request.ToUri();
            check(request.HasDestination && request.RouteKey == entry.Key && canonical is not null, "request identifies " + entry.Key);
            check(request.RequiresAuthentication == entry.RequiresAuthentication, "authentication metadata agrees with route " + entry.Key);
            check(AppActivationRequest.TryParse(canonical, out var parsed) && parsed == request,
                "builder/parser roundtrip retains exactly " + entry.Key);
            check(!string.IsNullOrWhiteSpace(entry.Description), "catalog explains " + entry.Key);
            foreach (var prefix in new[] { "mizon://localhost/", "mizon://S-1-5-21-123-1001/localhost/" })
                check(AppActivationRequest.TryParse(prefix + canonical![8..], out var alias) && alias == request,
                    "legacy authority keeps canonical destination " + entry.Key);
            if (needsId)
                check(!AppDeepLinkCatalog.TryCreate(entry.Key, null, comment, query, section, out _), "missing ID rejected for " + entry.Key);
            else
                check(!AppDeepLinkCatalog.TryCreate(entry.Key, publicId, comment, query, section, out _), "extraneous ID rejected for " + entry.Key);
            if (entry.Key != "search")
                check(!AppDeepLinkCatalog.TryCreate(entry.Key, needsId ? publicId : null, comment, "unexpected", section, out _),
                    "extraneous search rejected for " + entry.Key);
            if (entry.Key != "settings")
                check(!AppDeepLinkCatalog.TryCreate(entry.Key, needsId ? publicId : null, comment, query, "account", out _),
                    "extraneous settings rejected for " + entry.Key);
        }

        foreach (var section in AppDeepLinkCatalog.SettingsSections)
        {
            check(AppActivationRequest.TryParse("mizon://settings/" + section, out var request)
                && request == new AppActivationRequest("settings", SettingsSection: section), "settings section parses " + section);
            check(AppDeepLinkCatalog.TryCreate("settings", null, null, null, section, out var built) && built == request,
                "settings section builds " + section);
            check(AppActivationRequest.TryParse("MIZON://SETTINGS/" + section.ToUpperInvariant() + "/", out var mixed) && mixed == request,
                "case and trailing slash normalize " + section);
        }
        check(AppActivationRequest.TryParse("mizon://settings", out var settings) && settings.SettingsSection is null,
            "settings without section uses page default");
        check(AppActivationRequest.TryParse($"mizon://music/{publicId}?action=details", out var details)
            && details.ToUri() == $"mizon://music/{publicId:D}", "explicit details canonicalizes without autoplay");
        check(AppActivationRequest.TryParse($"mizon://music/{publicId:N}", out var shortId)
            && shortId.ToUri() == $"mizon://music/{publicId:D}", "legacy compact GUID canonicalizes to hyphenated format");

        foreach (var query in new[] { "100%", "%FF", "%2F", "test@example.com", "a=b?c#d", "نیم‌فاصله", "a+b & c?x=1/#% 🎵", new string('ا', 120) })
            check(AppDeepLinkCatalog.TryCreate("search", null, null, query, null, out var request)
                && AppActivationRequest.TryParse(request.ToUri(), out var parsed) && parsed.SearchQuery == query,
                "search roundtrip preserves literal punctuation and Unicode");
        check(AppActivationRequest.TryParse("mizon://search?query=%20test%20", out var trimmed) && trimmed.SearchQuery == "test",
            "search normalizes surrounding spaces once");

        foreach (var invalid in new string?[]
        {
            null, "", "mizon:", "mizon:/music", "mizon:///music", "mizon://", "mizon://localhost/", "mizon://unknown",
            "mizon://settings/arbitrary", "mizon://settings/appearance?theme=dark", "mizon://settings/../login",
            "mizon://settings/%2E%2E/login", "mizon://settings/%61ccount", "mizon://settings//account", "mizon://home//",
            "mizon://music/../login", "mizon://localhost/../music", "mizon://music/./", "mizon://S-1-invalid/localhost/music",
            "mizon://localhost//music", "mizon://user:password@settings/appearance", "mizon://settings:42/appearance",
            "mizon://settings/appearance#", "mizon://home?", "mizon://activities?filter=music", "mizon://story/not-a-guid",
            $"mizon://music/{publicId}/comments/{Guid.Empty}", $"mizon://music/{publicId}/comments/{commentId}?action=play",
            $"mizon://music/ {publicId}", $"mizon://music/{publicId} ", $"mizon://music/{publicId:B}",
            $"mizon://music/{publicId}/comments/{commentId}/extra", $"mizon://story/{publicId}?action=delete",
            "mizon://music\\..\\login", " mizon://home", "mizon://home\r\n", "mizon://search?query=a%00b",
            "mizon://search?query=a%0Ab", "mizon://search?query=%", "mizon://search?query=%0", "mizon://search?query=%GG",
            "mizon://search?query=%FF", "mizon://search?query=%C0%AF", "mizon://search?query=%ED%A0%80", "mizon://search?query=%E2%82",
            "mizon://search?query=%F4%90%80%80", "mizon://search?query=hello&query=world", "mizon://search?query=\uD800",
            "mizon://search?query=" + new string('a', 121), "mizon://search?query=" + new string('a', 2050),
            "https://music/" + publicId, "file:///C:/Windows/System32/cmd.exe", "mizon://search?query=hello&redirect=mizon://login"
        })
            check(!AppActivationRequest.TryParse(invalid, out var rejected) && rejected == new AppActivationRequest()
                && !rejected.HasDestination && rejected.ToUri() is null, "malformed protocol input has no destination");

        foreach (var invalid in new AppActivationRequest[]
        {
            new(Destination: "home", MusicPublicId: publicId), new(MusicPublicId: Guid.Empty), new(ClientPublicId: Guid.Empty),
            new(StoryPublicId: Guid.Empty), new(PlaylistPublicId: Guid.Empty), new(MusicCommentPublicId: commentId),
            new(MusicPublicId: publicId, MusicCommentPublicId: commentId, MusicAction: MusicActivationAction.Play),
            new(MusicPublicId: publicId, MusicAction: (MusicActivationAction)123), new(Destination: "home", MusicAction: MusicActivationAction.Play),
            new(Destination: "settings", SettingsSection: "unknown"), new(Destination: "music", SettingsSection: "account"),
            new(SearchQuery: "  "), new(SearchQuery: "x\ny"), new(SearchQuery: "x\uD800"), new(SearchQuery: new string('x', 121)),
            new(Destination: "not-a-page"), new(Destination: "welcome"), new(StoryPublicId: publicId, ClientPublicId: publicId)
        }) check(!invalid.HasDestination && !invalid.RequiresAuthentication && invalid.ToUri() is null,
            "invalid typed request cannot bypass route allowlist");
        check(!AppDeepLinkCatalog.TryCreate("music-comment", publicId, null, null, null, out _), "comment route requires its comment ID");
        check(!AppDeepLinkCatalog.TryCreate("music-details", publicId, commentId, null, null, out _), "detail route does not silently ignore comment ID");
        check(!AppDeepLinkCatalog.TryCreate("story", Guid.Empty, null, null, null, out _), "builder rejects empty public ID");
        check(!AppDeepLinkCatalog.TryCreate("settings", null, null, null, "arbitrary", out _), "builder rejects unregistered setting");
        check(!AppDeepLinkCatalog.TryCreate("search", null, null, " ", null, out _), "builder rejects empty search");
        check(!AppDeepLinkCatalog.TryCreate("invoke", null, null, null, null, out _), "builder cannot invoke arbitrary operation");
    }
}
