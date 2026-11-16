using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Mizon.Windows.ViewModels.Items;

public sealed partial class TopArtistItemViewModel : ViewModelBase
{
    private static LocalizationService Localization => LocalizationService.Current;
    private readonly FeaturedArtistsViewModel _owner;
    private readonly Point[] _activityPoints;

    public Guid PublicId { get; init; }
    public Guid? ProfileMediaId { get; init; }
    public Guid? CoverMediaId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string DisplayName => $"{FirstName} {LastName}".Trim();
    public string? GenreTitle { get; init; }
    public string? PersianGenreTitle { get; init; }
    public string DisplayGenre => (Localization.IsPersian ? PersianGenreTitle ?? GenreTitle : GenreTitle ?? PersianGenreTitle) ?? string.Empty;
    public int FollowersCount { get; private set; }
    public int MonthlyListenersCount { get; init; }
    public Color AccentColor { get; init; }
    public ImageSource? AvatarImage { get => field; private set => SetField(ref field, value); }
    public ImageSource? CoverImage { get => field; private set => SetField(ref field, value); }
    public string FollowersText => CompactNumber(FollowersCount);
    public string MonthlyListenersText => CompactNumber(MonthlyListenersCount);
    public string FollowersFullText => Localization.Format("MusicHome/FollowersCount", FollowersCount);
    public string MonthlyListenersFullText => Localization.Format("MusicHome/ListenersCount", MonthlyListenersCount);
    public string FollowButtonText => Localization.GetString(IsFollowed ? "MusicHome/Following" : "MusicHome/Follow");
    public string ProfileAutomationName => string.Join(" Â· ", Localization.Format("MusicHome/ArtistProfileName", DisplayName),
        FollowersFullText, MonthlyListenersFullText);
    public string FollowAutomationName => $"{FollowButtonText} Â· {DisplayName}";
    public FluentIcons.Common.Symbol FollowSymbol => IsFollowed ? FluentIcons.Common.Symbol.Checkmark : FluentIcons.Common.Symbol.PersonAdd;
    public bool CanFollow { get; init; }
    public bool CanToggleFollow => CanFollow && !IsFollowBusy;
    public bool IsFollowed { get; private set; }
    public bool IsFollowBusy
    {
        get => field;
        internal set
        {
            if (SetField(ref field, value)) OnPropertyChanged(nameof(CanToggleFollow));
        }
    }

    internal TopArtistItemViewModel(FeaturedArtistsViewModel owner, ReadOnlySpan<double> activity)
    {
        _owner = owner;
        _activityPoints = new Point[activity.Length];
        for (var index = 0; index < activity.Length; index++)
            _activityPoints[index] = new Point(index * 250d / Math.Max(1, activity.Length - 1),
                56 - Math.Clamp(activity[index], 0, 1) * 50);
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    internal void UpdateFollowing(bool following, int count)
    {
        IsFollowed = following;
        FollowersCount = count;
        OnPropertyChanged(string.Empty);
    }

    internal Task<string> GetMediaPathAsync(Guid id, long maximumBytes, CancellationToken token) =>
        _owner.GetMediaPathAsync(id, maximumBytes, token);

    internal async Task LoadArtworkAsync(bool includeCover, CancellationToken token)
    {
        if (ProfileMediaId is Guid profile && AvatarImage is null &&
            await LoadImageAsync(profile, 176, token) is { } avatar && !token.IsCancellationRequested)
            AvatarImage ??= avatar;
        if (includeCover && CoverMediaId is Guid cover && CoverImage is null && !token.IsCancellationRequested &&
            await LoadImageAsync(cover, 560, token) is { } image && !token.IsCancellationRequested)
            CoverImage ??= image;
    }

    private async Task<ImageSource?> LoadImageAsync(Guid id, int width, CancellationToken token)
    {
        try
        {
            var path = await GetMediaPathAsync(id, 12 * 1024 * 1024, token);
            token.ThrowIfCancellationRequested();
            return await ImageSourceFactory.LoadFileAsync(path, width, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Featured artist artwork could not be loaded.", error); }
        return null;
    }

    private static string CompactNumber(int value) => value >= 1_000_000
        ? (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M"
        : value >= 1000 ? (value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "K"
        : value.ToString("N0", CultureInfo.GetCultureInfo(Localization.LanguageTag));

    // A geometry has one visual owner. An absent series has no native shape.
    public PathGeometry? CreateActivityPath()
    {
        if (_activityPoints.Length < 2) return null;
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = _activityPoints[0], IsClosed = false, IsFilled = false };
        for (var index = 1; index < _activityPoints.Length; index++)
            figure.Segments.Add(new LineSegment { Point = _activityPoints[index] });
        geometry.Figures.Add(figure);
        return geometry;
    }
}
