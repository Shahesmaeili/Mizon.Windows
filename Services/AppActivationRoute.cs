using System;
using System.Globalization;
using System.Text;

namespace Mizon.Windows.Services;

internal enum MusicActivationAction { Details, Play, Download }

internal sealed partial record AppActivationRequest(
    string? Destination = null,
    Guid? MusicPublicId = null,
    Guid? PlaylistPublicId = null,
    Guid? ClientPublicId = null,
    MusicActivationAction MusicAction = MusicActivationAction.Details,
    string? SearchQuery = null,
    Guid? StoryPublicId = null,
    Guid? MusicCommentPublicId = null,
    string? SettingsSection = null)
{
    private const int MaximumUriLength = 2048;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal bool HasDestination => RouteKey is not null;
    internal bool RequiresAuthentication => HasDestination && Destination is not ("login" or "register");

    internal string? RouteKey
    {
        get
        {
            var targets = (Destination is null ? 0 : 1) + (MusicPublicId is null ? 0 : 1)
                + (PlaylistPublicId is null ? 0 : 1) + (ClientPublicId is null ? 0 : 1)
                + (SearchQuery is null ? 0 : 1) + (StoryPublicId is null ? 0 : 1);
            if (targets != 1 || (MusicCommentPublicId is not null && MusicPublicId is null)
                || (SettingsSection is not null && Destination != "settings")) return null;
            if (MusicPublicId is { } musicId)
            {
                if (musicId == Guid.Empty) return null;
                if (MusicCommentPublicId is { } commentId)
                    return commentId != Guid.Empty && MusicAction == MusicActivationAction.Details ? "music-comment" : null;
                return MusicAction switch
                {
                    MusicActivationAction.Details => "music-details",
                    MusicActivationAction.Play => "music-play",
                    MusicActivationAction.Download => "music-download",
                    _ => null
                };
            }
            if (MusicAction != MusicActivationAction.Details) return null;
            if (PlaylistPublicId is { } playlistId) return playlistId == Guid.Empty ? null : "playlist";
            if (ClientPublicId is { } clientId) return clientId == Guid.Empty ? null : "client";
            if (StoryPublicId is { } storyId) return storyId == Guid.Empty ? null : "story";
            if (SearchQuery is { } query) return IsValidSearch(query) ? "search" : null;
            if (Destination == "settings")
                return SettingsSection is null || AppDeepLinkCatalog.IsSettingsSection(SettingsSection) ? "settings" : null;
            return Destination is "home" or "music" or "movies" or "apps" or "vpn"
                or "login" or "register" or "playlists" or "activities" ? Destination : null;
        }
    }

    internal string? ToUri() => RouteKey switch
    {
        "music-details" => $"mizon://music/{MusicPublicId:D}",
        "music-play" => $"mizon://music/{MusicPublicId:D}?action=play",
        "music-download" => $"mizon://music/{MusicPublicId:D}?action=download",
        "music-comment" => $"mizon://music/{MusicPublicId:D}/comments/{MusicCommentPublicId:D}",
        "playlist" => $"mizon://playlist/{PlaylistPublicId:D}",
        "client" => $"mizon://client/{ClientPublicId:D}",
        "story" => $"mizon://story/{StoryPublicId:D}",
        "search" => "mizon://search?query=" + Uri.EscapeDataString(SearchQuery!.Trim()),
        "settings" => SettingsSection is null ? "mizon://settings" : "mizon://settings/" + SettingsSection,
        { } destination => "mizon://" + destination,
        _ => null
    };

    internal static bool TryParse(string? value, out AppActivationRequest request)
    {
        request = new();
        if (value is null || value.Length is < 9 or > MaximumUriLength
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        request = FromUri(uri);
        return request.HasDestination;
    }

    // Protocol input never becomes a file path, API address, credential or command.
    // Validate the original path before Uri can normalize dot segments or slashes.
    internal static AppActivationRequest FromUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals("mizon", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Fragment.Length != 0) return new();
        var original = uri.OriginalString;
        if (original.Length > MaximumUriLength || !original.StartsWith("mizon://", StringComparison.OrdinalIgnoreCase)
            || !IsValidText(original) || original.Contains('\\')) return new();
        var queryIndex = original.IndexOf('?');
        var path = original.AsSpan(8, (queryIndex < 0 ? original.Length : queryIndex) - 8);
        if (path.Length == 0 || path.IndexOf('%') >= 0 || path.IndexOf('#') >= 0) return new();
        if (path[^1] == '/') path = path[..^1];
        var parts = path.ToString().Split('/');
        foreach (var part in parts)
            if (part.Length == 0 || part is "." or "..") return new();
        var offset = 0;
        if (parts[0].Equals("localhost", StringComparison.OrdinalIgnoreCase)) offset = 1;
        else if (IsLegacySid(parts[0]) && parts.Length > 1
            && parts[1].Equals("localhost", StringComparison.OrdinalIgnoreCase)) offset = 2;
        if (parts.Length <= offset) return new();
        var query = queryIndex < 0 ? string.Empty : original[queryIndex..];
        var destination = parts[offset].ToLowerInvariant();
        var length = parts.Length - offset;
        if (length == 1)
        {
            if (destination == "search")
                return TryGetSearchQuery(query, out var search) ? new(SearchQuery: search) : new();
            if (query.Length != 0) return new();
            return destination is "home" or "music" or "movies" or "apps" or "vpn"
                or "login" or "register" or "playlists" or "activities" or "settings"
                ? new(destination) : new();
        }
        if (length == 2 && destination == "settings" && query.Length == 0)
        {
            var section = parts[offset + 1].ToLowerInvariant();
            return AppDeepLinkCatalog.IsSettingsSection(section) ? new("settings", SettingsSection: section) : new();
        }
        if (!TryGetPublicId(parts[offset + 1], out var id)) return new();
        if (length == 4 && destination == "music" && query.Length == 0
            && parts[offset + 2].Equals("comments", StringComparison.OrdinalIgnoreCase)
            && TryGetPublicId(parts[offset + 3], out var commentId))
            return new(MusicPublicId: id, MusicCommentPublicId: commentId);
        if (length != 2) return new();
        return destination switch
        {
            "music" => TryGetMusicAction(query, out var action) ? new(MusicPublicId: id, MusicAction: action) : new(),
            "playlist" when query.Length == 0 => new(PlaylistPublicId: id),
            "client" when query.Length == 0 => new(ClientPublicId: id),
            "story" when query.Length == 0 => new(StoryPublicId: id),
            _ => new()
        };
    }

    private static bool TryGetPublicId(string value, out Guid id)
    {
        id = Guid.Empty;
        return ((value.Length == 36 && Guid.TryParseExact(value, "D", out id))
            || (value.Length == 32 && Guid.TryParseExact(value, "N", out id))) && id != Guid.Empty;
    }

    private static bool IsLegacySid(string value)
    {
        if (value.Length > 184 || !value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = value.Split('-');
        if (parts.Length < 4) return false;
        for (var index = 2; index < parts.Length; index++)
            if (!ulong.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;
        return true;
    }

    internal static bool IsValidSearch(string value) => value.Trim().Length is >= 1 and <= 120 && IsValidText(value);

    private static bool IsValidText(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsControl(character)) return false;
            if (!char.IsSurrogate(character)) continue;
            if (!char.IsHighSurrogate(character) || index + 1 >= value.Length || !char.IsLowSurrogate(value[++index])) return false;
        }
        return true;
    }

    private static bool TryGetSearchQuery(string query, out string value)
    {
        value = string.Empty;
        if (query.Length is < 2 or > 1500) return false;
        var pair = query.AsSpan(1);
        var separator = pair.IndexOf('=');
        if (separator < 0 || pair.IndexOf('&') >= 0 || !pair[..separator].Equals("query", StringComparison.OrdinalIgnoreCase)) return false;
        var encoded = pair[(separator + 1)..];
        var decoded = new StringBuilder(encoded.Length);
        Span<byte> bytes = stackalloc byte[1500];
        try
        {
            for (var index = 0; index < encoded.Length;)
            {
                if (encoded[index] != '%')
                {
                    decoded.Append(encoded[index] == '+' ? ' ' : encoded[index]);
                    index++;
                    continue;
                }
                var count = 0;
                while (index < encoded.Length && encoded[index] == '%')
                {
                    if (index + 2 >= encoded.Length || !byte.TryParse(encoded.Slice(index + 1, 2),
                        NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[count++])) return false;
                    index += 3;
                }
                decoded.Append(StrictUtf8.GetString(bytes[..count]));
            }
        }
        catch (DecoderFallbackException) { return false; }
        value = decoded.ToString().Trim();
        return IsValidSearch(value);
    }

    private static bool TryGetMusicAction(string query, out MusicActivationAction action)
    {
        action = MusicActivationAction.Details;
        if (query.Length == 0) return true;
        var pair = query.AsSpan(1);
        var separator = pair.IndexOf('=');
        if (separator < 0 || pair.IndexOf('&') >= 0 || !pair[..separator].Equals("action", StringComparison.OrdinalIgnoreCase)) return false;
        var value = pair[(separator + 1)..];
        if (value.Equals("details", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("play", StringComparison.OrdinalIgnoreCase)) { action = MusicActivationAction.Play; return true; }
        if (value.Equals("download", StringComparison.OrdinalIgnoreCase)) { action = MusicActivationAction.Download; return true; }
        return false;
    }
}
