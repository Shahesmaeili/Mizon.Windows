using System.Collections.Generic;
using System;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;

namespace Mizon.Windows.ViewModels.Items;

public sealed class RecentlyPlayedMusicItemViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string DurationText { get; init; } = string.Empty;
    public ImageSource? CoverImage { get; private set; }
    public string CoverImagePath
    {
        get => field;
        init
        {
            field = value;
            CoverImage = ImageSourceFactory.Create(value, 480);
        }
    } = string.Empty;
    public int LikesCount { get; init; }
    public string LikesCountText => LikesCount.ToString("N0");
    public IReadOnlyList<MusicCardContributorItemViewModel> Contributors { get; init; } = [];
}

public sealed class MusicCardContributorItemViewModel
{
    public string DisplayName { get; init; } = string.Empty;
    public string Initials => string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(part => part[..1])).ToUpperInvariant();
    public ImageSource? ProfileImage { get; private set; }
    public string? ProfileImagePath
    {
        get => field;
        init
        {
            field = value;
            ProfileImage = ImageSourceFactory.Create(value, 96);
        }
    }
}
