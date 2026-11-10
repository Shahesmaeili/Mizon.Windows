using Symbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Xaml.Media;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Common;
using System;
using Windows.UI;

namespace Mizon.Windows.ViewModels.Items;

public sealed partial class FriendMusicActivityItemViewModel : ViewModelBase
{
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
        FollowingActivityTargetKind.MusicComment => "باز کردن آهنگ و رفتن به همین نظر",
        FollowingActivityTargetKind.Story => "باز کردن همین استوری",
        FollowingActivityTargetKind.MusicPlaylist => "نمایش آهنگ‌های همین پلی‌لیست",
        FollowingActivityTargetKind.Music => "نمایش جزئیات همین آهنگ",
        _ => "مقصد این فعالیت در دسترس نیست"
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
    public string DateText => OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
    public string AutomationName => $"{ActivityText}، {ContentTitle}، {Subtitle}، {TimeText}";
    public Symbol ActionSymbol { get; private set; }
    public Color AccentColor { get; private set; }
    public ImageSource? ProfileImage { get => field; private set => SetField(ref field, value); }
    public ImageSource? CoverImage { get => field; private set => SetField(ref field, value); }

    internal void Update(FollowingActivityItemResponse activity)
    {
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
            : fullName.Length > 0 ? fullName : activity.Username ?? "کاربر میزون";
        ContentTitle = !string.IsNullOrWhiteSpace(activity.PersianTitle) ? activity.PersianTitle : activity.Title;
        ContentKindTitle = TargetKind switch
        {
            FollowingActivityTargetKind.Story => "استوری",
            FollowingActivityTargetKind.MusicPlaylist => "پلی‌لیست",
            _ => activity.Domain == "music-video" ? "موزیک‌ویدیو" : "آهنگ"
        };
        if (TargetKind == FollowingActivityTargetKind.Story && string.IsNullOrWhiteSpace(ContentTitle))
            ContentTitle = "استوری تازه";
        (ActionText, ActionSymbol, AccentColor) = activity.ActivityType switch
        {
            "comment" => ("نظر گذاشت", Symbol.Comment, Color.FromArgb(255, 141, 190, 242)),
            "like" => ("پسندید", Symbol.Heart, Color.FromArgb(255, 238, 166, 190)),
            "reaction" => ("واکنش نشان داد", Symbol.Emoji, Color.FromArgb(255, 241, 205, 134)),
            "play" => ("گوش داد", Symbol.Headphones, Color.FromArgb(255, 157, 206, 196)),
            "playlist-created" => ("پلی‌لیست ساخت", Symbol.MusicNote2, Color.FromArgb(255, 195, 169, 239)),
            "story-posted" => ("استوری گذاشت", Symbol.Image, Color.FromArgb(255, 174, 185, 246)),
            _ => ("فعالیت تازه‌ای داشت", Symbol.People, Color.FromArgb(255, 157, 206, 196))
        };
        if (activity.ActivityType == "play" && activity.Domain == "music-video") ActionText = "تماشا کرد";
        Subtitle = activity.ActivityType switch
        {
            "comment" => activity.CommentText ?? string.Empty,
            "reaction" => activity.ReactionEmoji ?? "واکنش به موسیقی",
            "playlist-created" => $"{activity.PlaylistMusicsCount ?? 0:N0} آهنگ",
            "story-posted" => activity.TaggedMusicsCount > 0
                ? $"{activity.TaggedMusicsCount:N0} آهنگ تگ‌شده · {(activity.StoryViewedByMe ? "دیده‌شده" : "جدید")}" : "استوری تازه",
            _ => !string.IsNullOrWhiteSpace(activity.PersianTitle) ? activity.Title : ContentKindTitle
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

    internal void SetProfile(string path) => ProfileImage = ImageSourceFactory.Create(path, 96);
    internal void SetCover(string path) => CoverImage = ImageSourceFactory.Create(path, 160);

    private static string FormatTime(DateTimeOffset date)
    {
        var age = DateTimeOffset.UtcNow - date;
        return age.TotalMinutes < 1 ? "همین حالا"
            : age.TotalHours < 1 ? $"{Math.Max(1, (int)age.TotalMinutes)} دقیقه پیش"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} ساعت پیش"
            : age.TotalDays < 7 ? $"{(int)age.TotalDays} روز پیش" : date.ToLocalTime().ToString("yyyy/MM/dd");
    }
}
