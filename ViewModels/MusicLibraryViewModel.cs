using Microsoft.UI.Xaml;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Operations.Musics;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

// Each owner keeps one bounded page, never the entire library just to show a count.
public sealed partial class MusicLibraryViewModel(MizonApi mizonApi, MediaDownloader mediaDownloader) : ViewModelBase, IDisposable
{
    public LocalizationService Localization => LocalizationService.Current;
    private CancellationTokenSource? _loadCancellation;
    private string? _ownerToken;
    private int _version;
    private bool _disposed, _hasLoaded, _hasError;
    private int _totalPlaylists;
    private long _totalMusics;
    private string? _nextCursor;

    public ObservableCollection<MusicLibraryPlaylistItemViewModel> VisiblePlaylists { get; } = [];
    public int CoverDecodeWidth { get; init; } = 144;
    public bool IsLoading { get; private set; } = true;
    public bool HasLoaded => _hasLoaded;
    public bool HasError => _hasError;
    public bool IsInitialLoading => IsLoading && !_hasLoaded;
    public string? NextCursor => _nextCursor;
    public string PlaylistCountText => _hasLoaded ? _totalPlaylists.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo(Localization.LanguageTag)) : "—";
    public string PlaylistCountAutomationName => _hasLoaded ? Localization.Format("MusicWidgets/LibraryCount", _totalPlaylists) : Localization.GetString("MusicWidgets/LibraryCountPending");
    public string SummaryText => !_hasLoaded ? Localization.GetString("MusicWidgets/LibrarySummaryPending")
        : _totalPlaylists == 0 ? Localization.GetString("MusicWidgets/LibrarySummaryEmpty") : Localization.Format("MusicWidgets/LibrarySummary", _totalMusics);
    public string StatusText => _hasError ? Localization.GetString("MusicWidgets/LibraryError")
        : Localization.GetString("MusicWidgets/LibraryCreateHint");
    public Visibility EmptyVisibility => _hasLoaded && !_hasError && !IsLoading && VisiblePlaylists.Count == 0
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => _hasError ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlaylistsVisibility => VisiblePlaylists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MoreVisibility => _totalPlaylists > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string MoreText => Localization.Format("MusicWidgets/LibraryMore", _totalPlaylists);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => LoadPageAsync(null, 5, cancellationToken);

    public async Task<bool> LoadPageAsync(string? cursor, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;
        CancelLoading(false);
        var ownerToken = mizonApi.GetToken();
        if (_ownerToken != ownerToken)
        {
            _ownerToken = ownerToken;
            VisiblePlaylists.Clear();
            _hasLoaded = false;
            _totalPlaylists = 0;
            _totalMusics = 0;
            _nextCursor = null;
        }
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCancellation.Token;
        var version = _version;
        IsLoading = true;
        _hasError = false;
        NotifyPresentation();
        try
        {
            var response = await mizonApi.SendRequestAsync(new MusicLibraryOperation(new()
            {
                PageSize = Math.Clamp(pageSize, 1, 50), Cursor = cursor
            }), token);
            if (!IsCurrent(version, token)) return false;
            if (!response.IsSuccess || response.Content is not { } library)
            {
                _hasError = true;
                return false;
            }

            var existing = VisiblePlaylists.ToDictionary(item => item.PublicId);
            var items = library.Playlists.Select(playlist =>
            {
                if (!existing.TryGetValue(playlist.PublicId, out var item))
                    item = new() { PublicId = playlist.PublicId, CoverDecodeWidth = CoverDecodeWidth };
                item.Update(playlist.Title, playlist.MusicsCount, playlist.LastMusicCoverMediaPublicId, playlist.LastUpdatedAt);
                return item;
            }).ToArray();
            // Retain rows, decoded thumbnails and focus when refreshing the same page.
            for (var index = 0; index < items.Length; index++)
            {
                if (index < VisiblePlaylists.Count && ReferenceEquals(VisiblePlaylists[index], items[index])) continue;
                var oldIndex = VisiblePlaylists.IndexOf(items[index]);
                if (oldIndex >= 0) VisiblePlaylists.Move(oldIndex, index);
                else VisiblePlaylists.Insert(index, items[index]);
            }
            while (VisiblePlaylists.Count > items.Length) VisiblePlaylists.RemoveAt(VisiblePlaylists.Count - 1);
            _totalPlaylists = library.TotalPlaylistsCount;
            _totalMusics = library.TotalMusicsCount;
            _nextCursor = library.NextCursor;
            _hasLoaded = true;
            _ = LoadCoversAsync(items, version, token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception exception)
        {
            if (IsCurrent(version, token))
            {
                _hasError = true;
                AppLog.Error("Music library could not be loaded.", exception);
            }
            return false;
        }
        finally
        {
            if (version == _version && !_disposed)
            {
                IsLoading = false;
                NotifyPresentation();
            }
        }
    }

    private async Task LoadCoversAsync(MusicLibraryPlaylistItemViewModel[] items, int version, CancellationToken token)
    {
        // Three downloads at a time; reuse MediaDownloader's existing disk cache.
        foreach (var batch in items.Where(item => item.CoverImage is null && item.CoverMediaPublicId.HasValue)
            .GroupBy(item => item.CoverMediaPublicId!.Value).Chunk(3))
        {
            if (!IsCurrent(version, token)) return;
            await Task.WhenAll(batch.Select(async group =>
            {
                try
                {
                    var response = await mizonApi.SendRequestAsync(new MediaOperation(new() { MediaPublicId = group.Key }), token);
                    if (!IsCurrent(version, token) || !response.IsSuccess || response.Content is not { } media) return;
                    if (!Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri)) return;
                    var path = await mediaDownloader.DownloadToCacheAsync(media, uri, cancellationToken: token);
                    if (!IsCurrent(version, token)) return;
                    foreach (var item in group) item.SetCover(path);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception exception) { AppLog.Error("Library thumbnail download failed.", exception); }
            }));
        }
    }

    public void CancelLoading() => CancelLoading(true);
    private void CancelLoading(bool notify)
    {
        _version++;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        IsLoading = false;
        if (notify) NotifyPresentation();
    }

    private bool IsCurrent(int version, CancellationToken token) => !_disposed && version == _version
        && !token.IsCancellationRequested && _ownerToken == mizonApi.GetToken();

    private void NotifyPresentation()
    {
        foreach (var property in new[] { nameof(IsLoading), nameof(HasLoaded), nameof(HasError), nameof(IsInitialLoading),
            nameof(NextCursor), nameof(PlaylistCountText), nameof(PlaylistCountAutomationName), nameof(SummaryText), nameof(StatusText), nameof(EmptyVisibility),
            nameof(ErrorVisibility), nameof(PlaylistsVisibility), nameof(MoreVisibility), nameof(MoreText) }) OnPropertyChanged(property);
    }

    public void RefreshLocalization()
    {
        NotifyPresentation();
        foreach (var item in VisiblePlaylists) item.RefreshLocalization();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelLoading();
        VisiblePlaylists.Clear();
        _ownerToken = null;
    }
}
