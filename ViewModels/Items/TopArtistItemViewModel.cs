using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using System;
using System.Globalization;
using Windows.Foundation;
using Windows.UI;

namespace Mizon.Windows.ViewModels.Items;

/// <summary>Presentation data for the sample artist discovery cards.</summary>
public sealed partial class TopArtistItemViewModel : ViewModelBase
{
    private readonly Point[] _activityPoints;

    public Guid PublicId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string DisplayName => $"{FirstName} {LastName}".Trim();
    public string GenreTitle { get; init; } = string.Empty;
    public int FollowersCount { get; init; }
    public int MonthlyListenersCount { get; init; }
    public Color AccentColor { get; init; }
    public ImageSource? AvatarImage { get; }
    public ImageSource? CoverImage { get; }
    public string FollowersText => CompactNumber(FollowersCount + (IsFollowed ? 1 : 0));
    public string MonthlyListenersText => CompactNumber(MonthlyListenersCount);
    public string FollowersFullText => $"{FollowersCount + (IsFollowed ? 1 : 0):N0} دنبال‌کننده";
    public string MonthlyListenersFullText => $"{MonthlyListenersCount:N0} شنوندهٔ ماهانه";
    public string FollowButtonText => IsFollowed ? "دنبال می‌کنید" : "دنبال کردن";
    public string ProfileAutomationName => $"پروفایل {DisplayName}، {GenreTitle}، {FollowersFullText}، {MonthlyListenersFullText}";
    public string FollowAutomationName => IsFollowed ? $"لغو دنبال‌کردن آزمایشی {DisplayName}" : $"دنبال‌کردن آزمایشی {DisplayName}";
    public FluentIcons.Common.Symbol FollowSymbol => IsFollowed ? FluentIcons.Common.Symbol.Checkmark : FluentIcons.Common.Symbol.PersonAdd;

    public bool IsFollowed
    {
        get => field;
        set
        {
            if (!SetField(ref field, value)) return;
            OnPropertyChanged(nameof(FollowButtonText));
            OnPropertyChanged(nameof(FollowersText));
            OnPropertyChanged(nameof(FollowersFullText));
            OnPropertyChanged(nameof(ProfileAutomationName));
            OnPropertyChanged(nameof(FollowAutomationName));
            OnPropertyChanged(nameof(FollowSymbol));
        }
    }

    public TopArtistItemViewModel(string avatarPath, string coverPath, ReadOnlySpan<double> activity)
    {
        AvatarImage = ImageSourceFactory.Create(avatarPath, 144);
        CoverImage = ImageSourceFactory.Create(coverPath, 640);
        _activityPoints = new Point[activity.Length];
        for (var index = 0; index < activity.Length; index++)
            _activityPoints[index] = new Point(index * 250d / Math.Max(1, activity.Length - 1),
                56 - Math.Clamp(activity[index], 0, 1) * 50);
    }

    private static string CompactNumber(int value) => value >= 1_000_000
        ? (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M"
        : value >= 1000 ? (value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "K"
        : value.ToString(CultureInfo.InvariantCulture);

    // WinUI geometry has one visual owner. Share the points, not the Geometry,
    // because the same artist can appear in the spotlight and several cards.
    public PathGeometry CreateActivityPath()
    {
        var geometry = new PathGeometry();
        if (_activityPoints.Length == 0) return geometry;
        var figure = new PathFigure { StartPoint = _activityPoints[0], IsClosed = false, IsFilled = false };
        for (var index = 1; index < _activityPoints.Length; index++)
            figure.Segments.Add(new LineSegment { Point = _activityPoints[index] });
        geometry.Figures.Add(figure);
        return geometry;
    }
}
