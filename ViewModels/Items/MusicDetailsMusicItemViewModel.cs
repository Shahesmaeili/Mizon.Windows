using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicDetailsMusicItemViewModel : ViewModelBase
{
    public Guid PublicId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? PersianTitle { get; init; }
    public int LikesCount { get; init; }
    public Guid? CoverMediaPublicId { get; init; }
    public ObservableCollection<MusicDetailsContributorItemViewModel> Contributors { get; } = [];

    public string? CoverImagePath
    {
        get => field;
        set
        {
            if (!SetField(ref field, value)) return;
            CoverImage = ImageSourceFactory.Create(value, 480);
        }
    }

    public ImageSource? CoverImage
    {
        get => field;
        private set => SetField(ref field, value);
    }

    public string LikesCountText => $"{LikesCount:N0}";
}

public sealed class MusicDetailsContributorItemViewModel
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
}
