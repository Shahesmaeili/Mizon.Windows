using System;
using System.Collections.Generic;

namespace Mizon.Windows.Services;

internal sealed record AppDeepLinkDefinition(string Key, string UriTemplate, bool RequiresAuthentication, string Description);

// One explicit catalog for protocol links, typed navigation tools and documentation.
// Windows Search, shortcuts and existing clients can keep their original links.
internal static class AppDeepLinkCatalog
{
    internal static IReadOnlyList<AppDeepLinkDefinition> Entries { get; } = Array.AsReadOnly<AppDeepLinkDefinition>(
    [
        new("home", "mizon://home", true, "Open the authenticated home shell and its music landing page."),
        new("login", "mizon://login", false, "Open the sign-in form without submitting it or signing out."),
        new("register", "mizon://register", false, "Open the registration form without submitting it or signing out."),
        new("music", "mizon://music", true, "Open music discovery."),
        new("movies", "mizon://movies", true, "Open the current movies and series landing page."),
        new("apps", "mizon://apps", true, "Open the current apps landing page."),
        new("vpn", "mizon://vpn", true, "Open the VPN page without changing its connection."),
        new("playlists", "mizon://playlists", true, "Open all music playlists for the signed-in account."),
        new("playlist", "mizon://playlist/{publicId}", true, "Open a music playlist by its public ID."),
        new("music-details", "mizon://music/{publicId}", true, "Open music details without starting playback."),
        new("music-play", "mizon://music/{publicId}?action=play", true, "Open a music item and explicitly start playback."),
        new("music-download", "mizon://music/{publicId}?action=download", true, "Open a music item and explicitly download through its existing cache service."),
        new("music-comment", "mizon://music/{publicId}/comments/{commentPublicId}", true, "Open the music item and reveal its requested comment."),
        new("client", "mizon://client/{publicId}", true, "Open a client profile by its public ID."),
        new("story", "mizon://story/{publicId}", true, "Open a story with the existing story viewer."),
        new("activities", "mizon://activities", true, "Open all following activities across available content types."),
        new("settings", "mizon://settings/{section?}", true, "Open settings or one of its explicitly listed sections."),
        new("search", "mizon://search?query={encodedText}", true, "Open the in-app search with a 1–120 character query.")
    ]);

    internal static IReadOnlyList<string> SettingsSections { get; } = Array.AsReadOnly(
        new[] { "account", "security", "language", "appearance", "notifications", "storage", "updates", "platforms", "ai-connections" });

    internal static bool IsSettingsSection(string? section) => section is "account" or "security" or "language"
        or "appearance" or "notifications" or "storage" or "updates" or "platforms" or "ai-connections";

    internal static bool TryCreate(string? destination, Guid? publicId, Guid? commentPublicId, string? searchQuery,
        string? settingsSection, out AppActivationRequest request)
    {
        request = new();
        if (destination is null) return false;
        var key = destination.ToLowerInvariant();
        var hasId = key is "music-details" or "music-play" or "music-download" or "music-comment" or "playlist" or "client" or "story";
        if (hasId != publicId.HasValue || publicId == Guid.Empty || (key == "music-comment") != commentPublicId.HasValue
            || commentPublicId == Guid.Empty || (key == "search") != (searchQuery is not null)
            || (settingsSection is not null && key != "settings")) return false;
        request = key switch
        {
            "music-details" => new(MusicPublicId: publicId),
            "music-play" => new(MusicPublicId: publicId, MusicAction: MusicActivationAction.Play),
            "music-download" => new(MusicPublicId: publicId, MusicAction: MusicActivationAction.Download),
            "music-comment" => new(MusicPublicId: publicId, MusicCommentPublicId: commentPublicId),
            "playlist" => new(PlaylistPublicId: publicId),
            "client" => new(ClientPublicId: publicId),
            "story" => new(StoryPublicId: publicId),
            "search" => new(SearchQuery: searchQuery!.Trim()),
            "settings" => new("settings", SettingsSection: settingsSection?.ToLowerInvariant()),
            "home" or "login" or "register" or "music" or "movies" or "apps" or "vpn" or "playlists" or "activities" => new(key),
            _ => new()
        };
        if (request.HasDestination) return true;
        request = new();
        return false;
    }
}
