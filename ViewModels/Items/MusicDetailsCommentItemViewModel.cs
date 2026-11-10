using System;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;

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
    public string AutomationName => $"نظر {FullName}، {Text}، {TimestampText}";

    public string FullName => $"{FirstName} {LastName}".Trim();
    public string TimestampText => Timestamp is null ? string.Empty : Timestamp.Value.ToString(@"m\:ss");

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
