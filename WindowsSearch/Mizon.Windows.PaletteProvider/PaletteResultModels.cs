namespace Mizon.Windows.PaletteProvider;

internal enum PaletteResultKind
{
    Music,
    Client,
    Demo
}

// Music/client fields come from the search endpoint; Demo has an explicitly
// separate identity. Loading and actions belong to state, not Windows templates.
internal sealed record PaletteResultData
{
    internal required PaletteResultKind Kind { get; init; }
    internal required Guid PublicId { get; init; }
    internal string? Title { get; init; }
    internal string? PersianTitle { get; init; }
    internal string? Username { get; init; }
    internal string? FirstName { get; init; }
    internal string? LastName { get; init; }
    internal string? NickName { get; init; }
    internal int? LikesCount { get; init; }
    internal int? ViewsCount { get; init; }
    internal string? AlbumTitle { get; init; }
    internal string? GenreText { get; init; }
    internal string? DurationText { get; init; }
    internal DateTimeOffset? ReleaseDate { get; init; }
    internal string? SearchQuery { get; init; }
    internal string? MatchedTitle { get; init; }
    internal string? MatchedSubtitle { get; init; }
    internal double? RelevanceScore { get; init; }

    // Search rows expose the field that matched the user's language. Rich
    // previews continue to use the preferred display name and original fields.
    internal string RowTitle => MatchedTitle ?? DisplayTitle;
    internal string RowSubtitle => MatchedSubtitle ?? SecondaryTitle;

    internal string DisplayTitle => Kind == PaletteResultKind.Demo ? FirstNonEmpty(Title, "آزمایشگاه میزون") : Kind == PaletteResultKind.Music
        ? FirstNonEmpty(PersianTitle, Title, "آهنگ")
        : FirstNonEmpty(NickName, FullName, Username, "کاربر میزون");

    internal string SecondaryTitle => Kind == PaletteResultKind.Music
        ? (!string.IsNullOrWhiteSpace(PersianTitle) && !string.Equals(PersianTitle, Title, StringComparison.Ordinal) ? Title?.Trim() ?? string.Empty : string.Empty)
        : (!string.IsNullOrWhiteSpace(Username) ? $"@{Username.Trim()}" : string.Empty);

    internal string FullName => $"{FirstName?.Trim()} {LastName?.Trim()}".Trim();
    internal string TypeLabel => Kind == PaletteResultKind.Demo ? "نمونهٔ آزمایشی" : Kind == PaletteResultKind.Music ? "موسیقی" : "پروفایل";
    internal string Key => Kind == PaletteResultKind.Demo ? "demo/capabilities" : $"{(Kind == PaletteResultKind.Music ? "music" : "client")}/{PublicId:D}";
    internal string DetailsUri => Kind == PaletteResultKind.Demo ? "mizon://search?query=mizon%20demo" : $"mizon://{(Kind == PaletteResultKind.Music ? "music" : "client")}/{PublicId:D}";
    internal string DefaultActionUri => Kind == PaletteResultKind.Music ? $"{DetailsUri}?action=play" : DetailsUri;

    private static string FirstNonEmpty(params ReadOnlySpan<string?> values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return string.Empty;
    }
}

internal readonly record struct PaletteResultSnapshot(
    string? ImageUri,
    bool IsBusy,
    int? DownloadProgress,
    string? StatusMessage,
    bool CanDownload);

internal interface IPaletteResultState
{
    PaletteResultData Result { get; }
    PaletteResultSnapshot GetSnapshot();
    event Action? Changed;
    void Invoke(string action);
    void RequestDetails() { }
}

// Cards with local interactive state preserve the full host action/input payload.
// Normal music/profile results keep their existing allow-listed command handling.
internal interface IPaletteCustomPreview
{
    (string Template, string Data) GetPreview();
    void InvokeCard(string actionType, string actionJson, string inputsJson);
}
