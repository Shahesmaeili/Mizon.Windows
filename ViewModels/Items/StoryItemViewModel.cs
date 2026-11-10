using System;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;

namespace Mizon.Windows.ViewModels.Items;

public sealed class StoryItemViewModel : ViewModelBase
{
    public Guid PublicId { get; init; }
    public Guid PreviewMediaPublicId { get; init; }
    public Guid? CreatorProfileMediaPublicId
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            // An account can replace or remove its avatar while the story remains active.
            CreatorProfileImagePath = string.Empty;
            OnPropertyChanged();
        }
    }
    public string CreatorFirstName { get; init; } = string.Empty;
    public string CreatorLastName { get; init; } = string.Empty;
    public string? Title { get; init; }
    public bool ViewedByMe
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
            {
                OnPropertyChanged(nameof(RingOpacity));
                OnPropertyChanged(nameof(ViewStateText));
                OnPropertyChanged(nameof(StoryAutomationName));
            }
        }
    }
    public double RingOpacity => ViewedByMe ? 0.3 : 1;
    public string ViewStateText => ViewedByMe ? "دیده‌شده" : "جدید";
    public string CreatorInitials => string.Concat(
        string.IsNullOrWhiteSpace(CreatorFirstName) ? string.Empty : CreatorFirstName[..1],
        string.IsNullOrWhiteSpace(CreatorLastName) ? string.Empty : CreatorLastName[..1]);
    public string CreatorDisplayName => string.Join(" ", new[] { CreatorFirstName, CreatorLastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string StoryAutomationName => string.Join("، ",
        new[] { "استوری", CreatorDisplayName, Title, ViewStateText }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public ImageSource? PreviewImage { get; private set; }
    public ImageSource? CreatorProfileImage { get; private set; }

    public string PreviewImagePath
    {
        get => field;
        set
        {
            if (!SetField(ref field, value) && PreviewImage is not null) return;
            PreviewImage = ImageSourceFactory.Create(value, 320);
            OnPropertyChanged(nameof(PreviewImage));
        }
    } = string.Empty;

    public string CreatorProfileImagePath
    {
        get => field;
        set
        {
            if (!SetField(ref field, value) && CreatorProfileImage is not null) return;
            CreatorProfileImage = ImageSourceFactory.Create(value, 96);
            OnPropertyChanged(nameof(CreatorProfileImage));
        }
    } = string.Empty;

    // Realized cards own decoded XAML sources; the small cache paths survive recycling.
    // Both methods run on the UI dispatcher, like the image-path setters above.
    public void ReleaseImages()
    {
        if (PreviewImage is not null)
        {
            PreviewImage = null;
            OnPropertyChanged(nameof(PreviewImage));
        }
        if (CreatorProfileImage is not null)
        {
            CreatorProfileImage = null;
            OnPropertyChanged(nameof(CreatorProfileImage));
        }
    }

    public void RestoreImages()
    {
        if (PreviewImage is null && !string.IsNullOrEmpty(PreviewImagePath))
        {
            PreviewImage = ImageSourceFactory.Create(PreviewImagePath, 320);
            OnPropertyChanged(nameof(PreviewImage));
        }
        if (CreatorProfileImage is null && !string.IsNullOrEmpty(CreatorProfileImagePath))
        {
            CreatorProfileImage = ImageSourceFactory.Create(CreatorProfileImagePath, 96);
            OnPropertyChanged(nameof(CreatorProfileImage));
        }
    }
}
