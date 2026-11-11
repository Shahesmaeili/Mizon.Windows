using System.Globalization;
using System.Text;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}

var fa = LocalizationService.ReadResources("fa-IR");
var en = LocalizationService.ReadResources("en-US");
Check(fa.Count > 100 && fa.Keys.Order().SequenceEqual(en.Keys.Order()), "Every widget resource has both languages.");
foreach (var (key, value) in fa)
{
    Check(!string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(en[key]), key + " has nonempty translations.");
    Check(CompositeFormat.Parse(value).MinimumArgumentCount == CompositeFormat.Parse(en[key]).MinimumArgumentCount,
        key + " keeps matching format parameters.");
}

var localization = LocalizationService.Current;
var cultureBefore = CultureInfo.CurrentCulture.Name;
var rhythm = new WeeklyRhythmViewModel();
var days = rhythm.Days.ToArray();
var duration = rhythm.TotalDurationText;
var firstHeight = days[0].MusicBarHeight;
var peak = days.Single(day => day.IsPeak);
Check(days[0].DayName == "شنبه" && duration == "08:42", "Persian rhythm starts with the existing Saturday preview.");
var rhythmChanged = new HashSet<string?>();
rhythm.PropertyChanged += (_, args) => rhythmChanged.Add(args.PropertyName);
localization.SetLanguage("en-US");
rhythm.RefreshLocalization();
Check(days[0].DayName == "Saturday" && days[0].ShortName == "Sat", "Daily labels switch to English.");
Check(rhythm.TotalAccessibleText.Contains("8 hours and 42 minutes") && rhythm.ComparisonDescription == "More than last week",
    "Summary and Narrator describe the same statistics in English.");
Check(rhythm.Days.SequenceEqual(days) && rhythm.TotalDurationText == duration && days[0].MusicBarHeight == firstHeight
    && ReferenceEquals(peak, rhythm.Days.Single(day => day.IsPeak)), "Language changes preserve days, chart geometry and peak selection identity.");
Check(rhythmChanged.Contains(nameof(rhythm.TotalAccessibleText)) && rhythmChanged.Contains(nameof(rhythm.ComparisonDescription)),
    "Existing compiled bindings are notified without replacing the summary.");

var news = MusicNewsItemViewModel.CreateSamples();
var article = news[0];
var image = article.Image;
var englishTitle = article.Title;
Check(englishTitle == "The night the stage comes alive" && article.Body.Contains("does not report a real concert"),
    "English editorial text remains clearly marked as a sample.");
localization.SetLanguage("fa-IR");
foreach (var item in news) item.RefreshLocalization();
Check(article.Title == "شبی که صحنه با نور و موسیقی جان می‌گیرد" && ReferenceEquals(article.Image, image),
    "The displayed news article changes language without decoding a new image or selecting another article.");

var playlist = new MusicLibraryPlaylistItemViewModel { PublicId = Guid.NewGuid() };
playlist.Update("My mix", 12, Guid.NewGuid(), new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));
playlist.SetCover("file:///C:/synthetic-cover.jpg");
var cover = playlist.CoverImage;
var coverCalls = ImageSourceFactory.Calls;
localization.SetLanguage("en-US");
playlist.RefreshLocalization();
Check(playlist.MusicsCountText == "12 tracks" && playlist.UpdatedText.StartsWith("Updated:"), "Library count and date prefix localize.");
Check(playlist.Title == "My mix" && playlist.CanPlay && ReferenceEquals(cover, playlist.CoverImage)
    && ImageSourceFactory.Calls == coverCalls, "Playlist content, playback availability and cached artwork are preserved.");

var musicId = Guid.NewGuid();
var commentId = Guid.NewGuid();
var storyId = Guid.NewGuid();
var playlistId = Guid.NewGuid();
var response = new FollowingActivityItemResponse
{
    ActivityId = "synthetic-1", ActivityType = "comment", Domain = "music", MusicPublicId = musicId,
    MusicCommentPublicId = commentId, ClientPublicId = Guid.NewGuid(), NickName = "Alex",
    Title = "A track", PersianTitle = "یک آهنگ", CommentText = "A user-written comment",
    CreateDateTime = DateTimeOffset.UtcNow.AddMinutes(-3), ProfileMediaPublicId = Guid.NewGuid(), CoverMediaPublicId = Guid.NewGuid()
};
var activity = new FriendMusicActivityItemViewModel();
activity.Update(response);
activity.SetProfile("file:///C:/synthetic-avatar.jpg");
activity.SetCover("file:///C:/synthetic-art.jpg");
var avatar = activity.ProfileImage;
var activityCover = activity.CoverImage;
var calls = ImageSourceFactory.Calls;
Check(activity.ActionText == "commented" && activity.ContentTitle == "A track" && activity.NavigationHelpText.Contains("this comment"),
    "Comment label and navigation help use English while retaining the comment destination.");
Check(activity.Subtitle == response.CommentText && activity.TargetKind == FollowingActivityTargetKind.MusicComment,
    "User-written comments are never translated or redirected.");
localization.SetLanguage("fa-IR");
activity.RefreshLocalization();
Check(activity.ActionText == "نظر گذاشت" && activity.ContentTitle == "یک آهنگ" && activity.TimeText.Contains("دقیقه پیش"),
    "Existing activity labels, preferred title and relative time update to Persian.");
Check(activity.MusicPublicId == musicId && activity.MusicCommentPublicId == commentId
    && ReferenceEquals(avatar, activity.ProfileImage) && ReferenceEquals(activityCover, activity.CoverImage)
    && ImageSourceFactory.Calls == calls, "Localization leaves target IDs and decoded activity images intact.");

localization.SetLanguage("en-US");
foreach (var (kind, expected) in new[] { ("like", "liked"), ("reaction", "reacted"), ("play", "listened"),
    ("playlist-created", "created a playlist"), ("story-posted", "posted a story") })
{
    response.ActivityType = kind;
    response.MusicPlaylistPublicId = playlistId;
    response.StoryPublicId = storyId;
    response.TaggedMusicsCount = 2;
    response.PlaylistMusicsCount = 4;
    activity.Update(response);
    Check(activity.ActionText == expected && activity.CanOpen, kind + " remains a localized actionable activity.");
}
Check(activity.Subtitle == "2 tagged tracks · New" && activity.TargetKind == FollowingActivityTargetKind.Story,
    "Story tags and viewed status localize without changing the story destination.");
response.StoryViewedByMe = true;
activity.Update(response);
Check(activity.Subtitle == "2 tagged tracks · Viewed", "The viewed state remains distinguishable.");
response.ActivityType = "play";
response.Domain = "music-video";
activity.Update(response);
Check(activity.ActionText == "watched" && activity.ContentKindTitle == "Music video", "Music-video playback uses watching terminology.");
response.ActivityType = "unknown";
response.TargetType = "";
activity.Update(response);
Check(!activity.CanOpen && activity.NavigationHelpText.Contains("unavailable"), "Unknown activity destinations stay unavailable.");
Check(CultureInfo.CurrentCulture.Name == cultureBefore, "Presentation language never changes the process data culture.");

Console.WriteLine($"PASS: {checks} music-home localization checks. No Windows UI, account writes, API calls or application consent.");
