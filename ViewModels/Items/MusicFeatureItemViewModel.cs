using Mizon.Windows.Services;
using Microsoft.UI.Xaml.Media;
using Mizon.API.Client.Responses.Musics;
using Mizon.Windows.Common;
using System;
using System.Globalization;
using System.Linq;

namespace Mizon.Windows.ViewModels.Items;

/// <summary>One server-owned selection. Images are shared thumbnails; the hero uses the cached original path.</summary>
public sealed partial class MusicFeatureItemViewModel : ViewModelBase
{
    public LocalizationService Localization => LocalizationService.Current;

    public void RefreshLocalization()
    {
        UpdateLocalizedText();
        OnPropertyChanged(string.Empty);
    }

    private MusicFeatureItemResponse _response = new();
    private string _imagePath = string.Empty;
    private ImageSource? _previewImage;
    private ImageSource? _artistProfileImage;
    private string _artist = string.Empty;
    private string _genre = string.Empty;
    private bool _isLikeBusy;
    private bool _isArtworkLoading;
    private string _artworkErrorText = string.Empty;
    private Guid? _artistProfileMediaPublicId;

    public Guid PublicId => _response.PublicId;
    public Guid MusicPublicId => _response.MusicPublicId;
    public Guid MediaPublicId => _response.MediaPublicId;
    public Guid? PreviewMediaPublicId => _response.PreviewMediaPublicId;
    public Guid? CoverMediaPublicId => _response.CoverMediaPublicId;
    public Guid? ArtworkMediaPublicId => IsVideo ? PreviewMediaPublicId ?? CoverMediaPublicId : MediaPublicId;
    public Guid? ArtistProfileMediaPublicId => _artistProfileMediaPublicId;
    public string ContentTypeCode => _response.ContentTypeCode;
    public bool IsVideo => string.Equals(ContentTypeCode, "video", StringComparison.OrdinalIgnoreCase);
    public int DisplaySeconds => Math.Clamp(_response.DisplaySeconds, 3, 60);
    public string Title => _response.Title ?? string.Empty;
    public string? PersianTitle => _response.PersianTitle;
    public string DisplayTitle => Localization.IsPersian && !string.IsNullOrWhiteSpace(PersianTitle) ? PersianTitle : Title;
    public string Artist => _artist;
    public string ArtistText => _artist;
    public string GenreText => string.Join(" Â· ", _response.Genres.Select(genre => Localization.IsPersian && !string.IsNullOrWhiteSpace(genre.PersianTitle) ? genre.PersianTitle : genre.Title));
    public string GenreTitle => GenreText;
    public string? PrimaryAccentColor => _response.PrimaryAccentColor;
    public double DurationSeconds => _response.DurationSeconds is > 0 and < 86400 ? _response.DurationSeconds.Value : 0;
    public string DurationText => DurationSeconds > 0
        ? TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture)
        : string.Empty;
    public int LikesCount => Math.Max(0, _response.LikesCount);
    public int ViewsCount => Math.Max(0, _response.ViewsCount);
    public string LikesCountText => LikesCount.ToString("N0", CultureInfo.GetCultureInfo(Localization.LanguageTag));
    public string ViewsCountText => Localization.Format("MusicHome/ViewsCount", ViewsCount);
    public bool LikedByMe => _response.LikedByMe;
    public bool IsLikeBusy { get => _isLikeBusy; internal set => SetField(ref _isLikeBusy, value); }
    public string ImagePath => _imagePath;
    public string ArtworkPath => _imagePath;
    public string CoverImagePath => _imagePath;
    public ImageSource? PreviewImage => _previewImage;
    public ImageSource? CoverImage => _previewImage;
    public ImageSource? ArtistProfileImage => _artistProfileImage;
    public bool IsArtworkLoading => _isArtworkLoading;
    public string ArtworkErrorText => string.IsNullOrEmpty(_artworkErrorText) ? string.Empty : Localization.GetString(_artworkErrorText);
    public string AutomationName => Localization.Format("MusicHome/FeatureName", Localization.GetString(IsVideo ? "MusicHome/Video" : "MusicHome/Image"), DisplayTitle, Artist, LikesCountText);

    internal void Update(MusicFeatureItemResponse response)
    {
        var oldArtworkId = ArtworkMediaPublicId;
        var oldProfileId = _artistProfileMediaPublicId;
        _response = response;
        UpdateLocalizedText();
        _artistProfileMediaPublicId = response.Contributors.FirstOrDefault()?.ProfileMediaPublicId;
        if (oldArtworkId != ArtworkMediaPublicId)
        {
            _imagePath = string.Empty;
            _previewImage = null;
        }
        if (oldProfileId != _artistProfileMediaPublicId) _artistProfileImage = null;
        if (_previewImage is null)
        {
            _isArtworkLoading = ArtworkMediaPublicId is { } id && id != Guid.Empty;
            _artworkErrorText = string.Empty;
        }
        // One typed binding refresh, only when server data arrives; never during resize.
        OnPropertyChanged(string.Empty);
    }

    private void UpdateLocalizedText()
    {
        _artist = string.Join(Localization.IsPersian ? "، " : ", ", _response.Contributors.Select(person =>
            string.Join(" ", new[] { person.FirstName, person.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))))
            .Where(name => name.Length > 0));
        _genre = string.Join(" · ", _response.Genres.Select(genre =>
            Localization.IsPersian && !string.IsNullOrWhiteSpace(genre.PersianTitle) ? genre.PersianTitle : genre.Title));
    }

    internal void SetArtwork(string path)
    {
        SetArtworkState(false, string.Empty);
        if (string.Equals(_imagePath, path, StringComparison.Ordinal)) return;
        _imagePath = path;
        _previewImage = ImageSourceFactory.Create(path, 160);
        OnPropertyChanged(nameof(PreviewImage));
        OnPropertyChanged(nameof(CoverImage));
        OnPropertyChanged(nameof(ImagePath));
        OnPropertyChanged(nameof(ArtworkPath));
        OnPropertyChanged(nameof(CoverImagePath));
    }

    internal void BeginArtworkLoad() => SetArtworkState(true, string.Empty);
    internal void CancelArtworkLoad() => SetArtworkState(false, _artworkErrorText);
    internal void SetArtworkError(string message) => SetArtworkState(false, message);

    private void SetArtworkState(bool loading, string error)
    {
        SetField(ref _isArtworkLoading, loading, nameof(IsArtworkLoading));
        SetField(ref _artworkErrorText, error, nameof(ArtworkErrorText));
    }

    internal void SetArtistProfile(string path)
    {
        _artistProfileImage = ImageSourceFactory.Create(path, 96);
        OnPropertyChanged(nameof(ArtistProfileImage));
    }

    internal void ApplyLike(bool liked, int likesCount)
    {
        if (_response.LikedByMe == liked && _response.LikesCount == likesCount) return;
        _response.LikedByMe = liked;
        _response.LikesCount = Math.Max(0, likesCount);
        OnPropertyChanged(nameof(LikedByMe));
        OnPropertyChanged(nameof(LikesCount));
        OnPropertyChanged(nameof(LikesCountText));
        OnPropertyChanged(nameof(AutomationName));
    }
}
