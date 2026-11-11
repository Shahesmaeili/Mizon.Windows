using Mizon.Windows.Services;
using System.Globalization;
using Symbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Xaml.Media;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Common;
using System;
using Windows.UI;

namespace Mizon.Windows.ViewModels.Items;

public sealed partial class FriendMusicActivityItemViewModel : ViewModelBase
{
    public LocalizationService Localization => LocalizationService.Current;
    private FollowingActivityItemResponse? _activity;
    public string Id { get; private set; } = string.Empty;
    public Guid ActorPublicId { get; private set; }
    public Guid? MusicPublicId { get; private set; }
    public Guid? MusicPlaylistPublicId { get; private set; }
    public Guid? MusicCommentPublicId { get; private set; }
    public Guid? StoryPublicId { get; private set; }
    public FollowingActivityTargetKind TargetKind { get; private set; }
    public bool StoryViewedByMe { get; private set; }
    public bool CanOpen => TargetKind != FollowingActivityTargetKind.Unknown;
    public string NavigationHelpText => TargetKind switch
    {
        FollowingActivityTargetKind.MusicComment => Localization.GetString("MusicWidgets/ActivityCommentHelp"),
        FollowingActivityTargetKind.Story => Localization.GetString("MusicWidgets/ActivityStoryHelp"),
        FollowingActivityTargetKind.MusicPlaylist => Localization.GetString("MusicWidgets/ActivityPlaylistHelp"),
        FollowingActivityTargetKind.Music => Localization.GetString("MusicWidgets/ActivityMusicHelp"),
        _ => Localization.GetString("MusicWidgets/ActivityUnavailable")
    };
    public Guid? ProfileMediaPublicId { get; private set; }
    public Guid? CoverMediaPublicId { get; private set; }
    public string ContentDomain { get; private set; } = string.Empty;
    public string ActivityKind { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string ActionText { get; private set; } = string.Empty;
    public string ContentKindTitle { get; private set; } = string.Empty;
    public string ActivityText => $"{DisplayName} {ActionText}";
    public string ContentTitle { get; private set; } = string.Empty;
    public string Subtitle { get; private set; } = string.Empty;
    public string TimeText => FormatTime(OccurredAt);
    public string DateText => OccurredAt.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo(Localization.LanguageTag));
    public string AutomationName => $"{ActivityText}، {ContentTitle}، {Subtitle}، {TimeText}";
    public Symbol ActionSymbol { get; private set; }
    public Color AccentColor { get; private set; }
    public ImageSource? ProfileImage { get => field; private set => SetField(ref field, value); }
    public ImageSource? CoverImage { get => field; private set => SetField(ref field, value); }

    internal void Update(FollowingActivityItemResponse activity)
    {
        _activity = activity;
        Id = activity.ActivityId;
        ActorPublicId = activity.ClientPublicId;
        MusicPublicId = activity.MusicPublicId;
        MusicPlaylistPublicId = activity.MusicPlaylistPublicId;
        MusicCommentPublicId = activity.MusicCommentPublicId;
        StoryPublicId = activity.StoryPublicId;
        StoryViewedByMe = activity.StoryViewedByMe;
        TargetKind = ResolveTarget(activity);
        ContentDomain = activity.Domain;
        ActivityKind = activity.ActivityType;
        OccurredAt = activity.CreateDateTime;
        var fullName = $"{activity.FirstName} {activity.LastName}".Trim();
        DisplayName = !string.IsNullOrWhiteSpace(activity.NickName) ? activity.NickName
            : fullName.Length > 0 ? fullName : activity.Username ?? Localization.GetString("MusicWidgets/MizonUser");
        ContentTitle = Localization.IsPersian && !string.IsNullOrWhiteSpace(activity.PersianTitle)
            ? activity.PersianTitle : !string.IsNullOrWhiteSpace(activity.Title) ? activity.Title : activity.PersianTitle ?? string.Empty;
        ContentKindTitle = TargetKind switch
        {
            FollowingActivityTargetKind.Story => Localization.GetString("MusicWidgets/Story"),
            FollowingActivityTargetKind.MusicPlaylist => Localization.GetString("MusicWidgets/Playlist"),
            _ => activity.Domain == "music-video" ? Localization.GetString("MusicWidgets/MusicVideo") : Localization.GetString("MusicWidgets/Track")
        };
        if (TargetKind == FollowingActivityTargetKind.Story && string.IsNullOrWhiteSpace(ContentTitle))
            ContentTitle = Localization.GetString("MusicWidgets/StoryNew");
        (ActionText, ActionSymbol, AccentColor) = activity.ActivityType switch
        {
            "comment" => (Localization.GetString("MusicWidgets/ActivityComment"), Symbol.Comment, Color.FromArgb(255, 141, 190, 242)),
            "like" => (Localization.GetString("MusicWidgets/ActivityLike"), Symbol.Heart, Color.FromArgb(255, 238, 166, 190)),
            "reaction" => (Localization.GetString("MusicWidgets/ActivityReaction"), Symbol.Emoji, Color.FromArgb(255, 241, 205, 134)),
            "play" => (Localization.GetString("MusicWidgets/ActivityPlay"), Symbol.Headphones, Color.FromArgb(255, 157, 206, 196)),
            "playlist-created" => (Localization.GetString("MusicWidgets/ActivityPlaylist"), Symbol.MusicNote2, Color.FromArgb(255, 195, 169, 239)),
            "story-posted" => (Localization.GetString("MusicWidgets/ActivityStory"), Symbol.Image, Color.FromArgb(255, 174, 185, 246)),
            _ => (Localization.GetString("MusicWidgets/ActivityNew"), Symbol.People, Color.FromArgb(255, 157, 206, 196))
        };
        if (activity.ActivityType == "play" && activity.Domain == "music-video") ActionText = Localization.GetString("MusicWidgets/ActivityWatch");
        Subtitle = activity.ActivityType switch
        {
            "comment" => activity.CommentText ?? string.Empty,
            "reaction" => activity.ReactionEmoji ?? Localization.GetString("MusicWidgets/ActivityReactionFallback"),
            "playlist-created" => Localization.Format("MusicWidgets/TracksCount", activity.PlaylistMusicsCount ?? 0),
            "story-posted" => activity.TaggedMusicsCount > 0
                ? Localization.Format("MusicWidgets/ActivityStorySummary", activity.TaggedMusicsCount, Localization.GetString(activity.StoryViewedByMe ? "MusicWidgets/Viewed" : "MusicWidgets/New")) : Localization.GetString("MusicWidgets/StoryNew"),
            _ => Localization.IsPersian && !string.IsNullOrWhiteSpace(activity.PersianTitle) ? activity.Title : ContentKindTitle
        };
        if (ProfileMediaPublicId != activity.ProfileMediaPublicId)
        {
            ProfileMediaPublicId = activity.ProfileMediaPublicId;
            ProfileImage = null;
        }
        if (CoverMediaPublicId != activity.CoverMediaPublicId)
        {
            CoverMediaPublicId = activity.CoverMediaPublicId;
            CoverImage = null;
        }
        foreach (var property in new[] { nameof(DisplayName), nameof(ActionText), nameof(ContentKindTitle),
            nameof(ActivityText), nameof(ContentTitle), nameof(Subtitle), nameof(TimeText), nameof(DateText),
            nameof(AutomationName), nameof(ActionSymbol), nameof(AccentColor), nameof(TargetKind),
            nameof(CanOpen), nameof(NavigationHelpText), nameof(StoryViewedByMe) }) OnPropertyChanged(property);
    }

    private static FollowingActivityTargetKind ResolveTarget(FollowingActivityItemResponse activity)
    {
        // Older responses may omit TargetType. Infer only a matching activity with a valid ID.
        var type = string.IsNullOrEmpty(activity.TargetType) ? activity.ActivityType switch
        {
            "comment" => "music-comment", "story-posted" => "story",
            "playlist-created" => "music-playlist", "like" or "reaction" or "play" => "music", _ => ""
        } : activity.TargetType;
        return type switch
        {
            "music-comment" when Valid(activity.MusicPublicId) && Valid(activity.MusicCommentPublicId) => FollowingActivityTargetKind.MusicComment,
            "story" when Valid(activity.StoryPublicId) => FollowingActivityTargetKind.Story,
            "music-playlist" when Valid(activity.MusicPlaylistPublicId) => FollowingActivityTargetKind.MusicPlaylist,
            "music" when Valid(activity.MusicPublicId) => FollowingActivityTargetKind.Music,
            _ => FollowingActivityTargetKind.Unknown
        };
        static bool Valid(Guid? id) => id.HasValue && id.Value != Guid.Empty;
    }

    public void RefreshLocalization()
    {
        // Recompute presentation only; IDs, cached images and the collection remain intact.
        if (_activity is { } activity) Update(activity);
    }

    internal void SetProfile(string path) => ProfileImage = ImageSourceFactory.Create(path, 96);
    internal void SetCover(string path) => CoverImage = ImageSourceFactory.Create(path, 160);

    private string FormatTime(DateTimeOffset date)
    {
        var age = DateTimeOffset.UtcNow - date;
        return age.TotalMinutes < 1 ? Localization.GetString("MusicWidgets/JustNow")
            : age.TotalHours < 1 ? Localization.Format("MusicWidgets/MinutesAgo", Math.Max(1, (int)age.TotalMinutes))
            : age.TotalDays < 1 ? Localization.Format("MusicWidgets/HoursAgo", (int)age.TotalHours)
            : age.TotalDays < 7 ? Localization.Format("MusicWidgets/DaysAgo", (int)age.TotalDays) : date.ToLocalTime().ToString("d", CultureInfo.GetCultureInfo(Localization.LanguageTag));
    }
}
