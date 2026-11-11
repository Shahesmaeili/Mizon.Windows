namespace Mizon.Windows.Mcp.Contracts;

/// <summary>Inputs for the live music discovery page, never an arbitrary service invocation.</summary>
public sealed record McpMusicHomeRequest
{
    public string? Section { get; init; }
    public string? Action { get; init; }
    public string? Id { get; init; }
    public int? Index { get; init; }
    public double? Value { get; init; }
    public bool? Enabled { get; init; }
    public string? Language { get; init; }
}

public sealed record McpMusicHomeState
{
    public bool IsAvailable { get; init; }
    public bool HasSession { get; init; }
    public string Language { get; init; } = "fa-IR";
    public string Layout { get; init; } = "unavailable";
    public bool IsBusy { get; init; }
    public McpMusicHomeSectionState[] Sections { get; init; } = [];
    public McpMusicHomeHeroState? Hero { get; init; }
    public McpMusicHomeStoryState? Story { get; init; }
    public string? ActiveDialog { get; init; }
    public string? ActiveStoryId { get; init; }
    public string? ActiveArtistId { get; init; }
    public int? ActiveNewsIndex { get; init; }
    public int? SelectedDayIndex { get; init; }
    public string[] Actions { get; init; } = [];
}

public sealed record McpMusicHomeSectionState
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Source { get; init; } = "api";
    public bool IsLoading { get; init; }
    public bool HasError { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public bool HasMore { get; init; }
    public McpMusicHomeItemState[] Items { get; init; } = [];
    public string[] Actions { get; init; } = [];
}

/// <summary>Bounded visible-card metadata. Images are presence flags, never local paths or bytes.</summary>
public sealed record McpMusicHomeItemState
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public bool ImageAvailable { get; init; }
    public bool IsDemo { get; init; }
    public bool IsAvailable { get; init; }
    public bool CanOpen { get; init; }
    public long? Count { get; init; }
    public double? DurationSeconds { get; init; }
    public string? ActorId { get; init; }
    public string? TargetId { get; init; }
    public string? TargetKind { get; init; }
    public string? ActivityKind { get; init; }
    public bool? IsSeen { get; init; }
    public bool? IsSelected { get; init; }
    public string[] Actions { get; init; } = [];
}

public sealed record McpMusicHomeHeroState
{
    public string? Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public int Index { get; init; }
    public int Count { get; init; }
    public bool IsPlaying { get; init; }
    public bool IsMuted { get; init; }
    public double Volume { get; init; }
    public bool IsFullscreen { get; init; }
    public bool IsLoading { get; init; }
    public bool CanSeek { get; init; }
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public bool IsLiked { get; init; }
    public bool CanLike { get; init; }
    public string[] Actions { get; init; } = [];
}

public sealed record McpMusicHomeStoryState
{
    public bool HasError { get; init; }
    public string? ErrorCode { get; init; }
    public string? Id { get; init; }
    public int Index { get; init; }
    public int Count { get; init; }
    public bool IsLoading { get; init; }
    public bool IsPlaying { get; init; }
    public bool IsLiked { get; init; }
    public bool CanLike { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanNext { get; init; }
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public string[] Actions { get; init; } = [];
    public McpMusicHomeItemState[] Widgets { get; init; } = [];
}

/// <summary>Shared input validation at both the SDK tool and app adapter boundaries.</summary>
public static class McpMusicHomeContract
{
    public const int MaxItemsPerSection = 20;

    public static bool IsSection(string? section) => section is "featured" or "spotlight" or "stories"
        or "genres" or "artists" or "recommended" or "recent" or "library" or "weekly"
        or "news" or "trending" or "following";

    public static bool IsValid(McpRequest request)
    {
        if (request.Operation == "get_music_home") return request.MusicHome is null;
        if (request.MusicHome is not { } input) return false;
        if (request.Operation == "refresh_music_home")
            return (input.Section == "all" || IsSection(input.Section)) && input.Action is null
                && input.Id is null && input.Index is null && input.Value is null
                && input.Enabled is null && input.Language is null;
        if (request.Operation is not ("music_home_action" or "music_home_submit")) return false;
        if ((request.Operation == "music_home_submit") != (input.Action is "like" or "follow")) return false;
        if (!IsActionForSection(input.Section, input.Action)) return false;

        var usesId = input.Action is "open" or "play" or "profile" or "copy_link" or "follow" or "open_widget";
        var needsId = usesId && input.Section is not ("featured" or "spotlight" or "news");
        if (!usesId && input.Id is not null || needsId && input.Id is null) return false;
        if (input.Id is { } id && (string.IsNullOrWhiteSpace(id) || id.Length > 100
            || id.Any(char.IsControl))) return false;

        var needsIndex = input.Action == "select";
        var contributorIndex = input.Action == "profile" && input.Section is "recommended" or "recent" or "trending";
        if (needsIndex && !input.Index.HasValue || !needsIndex && !contributorIndex && input.Index.HasValue) return false;
        if (input.Index is < 0 or > 255 || contributorIndex && input.Index > 1) return false;
        var needsValue = input.Action is "seek" or "volume";
        if (input.Value.HasValue != needsValue) return false;
        if (input.Value is { } value && (!double.IsFinite(value) || value < 0
            || value > (input.Action == "volume" ? 1 : 86400))) return false;
        if (input.Enabled.HasValue != (input.Action is "mute" or "fullscreen" or "like" or "follow")) return false;
        if (input.Action == "set_language") return input.Language is "fa-IR" or "en-US";
        return input.Language is null;
    }

    private static bool IsActionForSection(string? section, string? action) => action switch
    {
        "open" => section is "featured" or "spotlight" or "stories" or "artists" or "recommended"
            or "recent" or "library" or "news" or "trending" or "following",
        "play" => section is "featured" or "spotlight" or "recommended" or "recent" or "library" or "trending",
        "pause" or "resume" => section is "featured" or "stories" or "news",
        "retry" or "like" => section is "featured" or "stories",
        "follow" => section is "spotlight" or "artists",
        "previous" or "next" => section is "featured" or "spotlight" or "news" or "artists" or "stories",
        "select" => section is "featured" or "spotlight" or "news" or "artists" or "stories" or "weekly",
        "mute" or "fullscreen" or "seek" or "volume" or "request_download" => section == "featured",
        "copy_link" => section is "featured" or "library",
        "open_all" => section is "library" or "following",
        "show_recent" => section == "library",
        "add_story" => section == "stories",
        "open_widget" => section == "stories",
        "dismiss" => section is "stories" or "all",
        "set_language" => section == "all",
        "reset_model" => section == "spotlight",
        "profile" => section is "spotlight" or "artists" or "stories" or "following" or "recommended" or "recent" or "trending",
        _ => false
    };
}
