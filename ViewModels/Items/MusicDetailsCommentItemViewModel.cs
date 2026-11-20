using Mizon.Windows.Services;
using System;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.API.Client.Responses.Musics;
using System.Collections.Generic;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicDetailsCommentItemViewModel : ViewModelBase
{
    public Guid PublicId { get; init; }
    public Guid ClientPublicId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string Text { get; init; } = string.Empty;
    public TimeSpan? Timestamp { get; init; }
    public Guid? ProfileMediaPublicId { get; init; }
    public Guid? ParentPublicId { get; init; }
    public string? Platform { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int ReplyCount { get; init; }
    public bool HasReplies => ReplyCount > 0;
    public bool HasTimestamp => Timestamp.HasValue;
    public bool HasTags => Tags.Count != 0;
    public List<MusicCommentTagItem> Tags { get; init; } = [];
    public int Likes { get => field; set => SetField(ref field, value); }
    public int Dislikes { get => field; set => SetField(ref field, value); }
    public int MyVote
    {
        get => field;
        set
        {
            if (!SetField(ref field, value)) return;
            OnPropertyChanged(nameof(Liked));
            OnPropertyChanged(nameof(Disliked));
        }
    }
    public bool Liked => MyVote == 1;
    public bool Disliked => MyVote == -1;
    public bool IsVoting
    {
        get => field;
        set { if (SetField(ref field, value)) OnPropertyChanged(nameof(CanVote)); }
    }
    public bool CanVote => !IsVoting;
    public void ApplyVotes(int likes, int dislikes, int mine)
    {
        Likes = likes;
        Dislikes = dislikes;
        MyVote = mine;
        OnPropertyChanged(nameof(Liked));
        OnPropertyChanged(nameof(Disliked));
    }
    public string PlatformLabel(int revision) => string.IsNullOrWhiteSpace(Platform)
        ? Localization.GetString("MusicDetails/CommentUnknownPlatform", revision) : Platform;
    public string PlatformGlyph => Platform?.ToLowerInvariant() switch
    {
        "windows" => "\uE7F4",
        "mac" or "macos" => "\uE770",
        "ios" or "iphone" => "\uE8EA",
        "android" => "\uE8CC",
        "web" => "\uE774",
        _ => "\uE897"
    };
    public string CommentDate(int revision) => CreatedAt == default ? string.Empty :
        CreatedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(Localization.LanguageTag));
    public bool IsActivityTarget
    {
        get => field;
        set
        {
            if (!SetField(ref field, value)) return;
            OnPropertyChanged(nameof(ActivityHighlightOpacity));
        }
    }
    public double ActivityHighlightOpacity => IsActivityTarget ? 1 : 0;
    public LocalizationService Localization => LocalizationService.Current;
    public string Label(string key, int revision) => Localization.GetString("MusicDetails/" + key, revision);
    public string RepliesLabel(int revision) => Localization.Format("MusicDetails/CommentReplies", ReplyCount);
    public string TimestampAutomationName(int revision) => Localization.Format("MusicDetails/CommentPlayAt", TimestampText);
    public string AutomationName(int revision) => Timestamp is null
        ? Localization.Format("MusicDetails/CommentBy", FullName, Text)
        : Localization.Format("MusicDetails/TimedCommentBy", FullName, Text, TimestampText);

    public string FullName => $"{FirstName} {LastName}".Trim();
    public string TimestampText => Timestamp is not { } position ? string.Empty :
        position.ToString(position.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");

    public string? ProfileImagePath
    {
        get => field;
        set
        {
            if (!SetField(ref field, value)) return;
            ProfileImage = ImageSourceFactory.Create(value, 96);
        }
    }

    public ImageSource? ProfileImage
    {
        get => field;
        private set => SetField(ref field, value);
    }
}
