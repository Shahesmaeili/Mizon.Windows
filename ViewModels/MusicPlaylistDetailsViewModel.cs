using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Operations.Musics;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public sealed class MusicPlaylistDetailsViewModel(MizonApi api, MediaDownloader downloader) : ViewModelBase, IDisposable
{
    private CancellationTokenSource? _load;
    private int _version;
    private bool _disposed;
    private string? _ownerToken;
    private Guid? _coverMediaPublicId;
    public ObservableCollection<DiscoveryMusicItemViewModel> Musics { get; } = [];
    public ImageSource? CoverImage { get => field; private set => SetField(ref field, value); }
    public Guid PublicId { get; private set; }
    public string Title { get; private set; } = "پلی‌لیست";
    public bool IsOwner { get; private set; }
    public int MusicsCount { get; private set; }
    public string CountText => $"{MusicsCount:N0} آهنگ";
    public bool IsLoading { get; private set; }
    public bool HasError { get; private set; }
    public bool HasLoaded { get; private set; }
    public bool CanPlay => HasLoaded && !HasError && !IsLoading && MusicsCount > 0;
    public string? NextCursor { get; private set; }
    public Visibility EmptyVisibility => HasLoaded && !IsLoading && !HasError && MusicsCount == 0 ? Visibility.Visible : Visibility.Collapsed;

    public async Task<bool> LoadAsync(Guid id, string? cursor = null, int startIndex = 0)
    {
        if (_disposed) return false;
        _load?.Cancel(); _load?.Dispose();
        _load = new();
        var token = _load.Token;
        var owner = api.GetToken();
        var version = ++_version;
        if (PublicId != id || _ownerToken != owner)
        {
            _ownerToken = owner;
            PublicId = id; Musics.Clear(); Title = "پلی‌لیست"; IsOwner = false; HasLoaded = false; MusicsCount = 0; NextCursor = null;
            _coverMediaPublicId = null; CoverImage = null;
        }
        IsLoading = true; HasError = false; Notify();
        try
        {
            var response = await api.SendRequestAsync(new MusicPlaylistDetailsOperation(new()
            { MusicPlaylistPublicId = id, PageSize = 20, Cursor = cursor }), token);
            if (!IsCurrent()) return false;
            if (!response.IsSuccess || response.Content is not { } detail)
            {
                HasError = true;
                NextCursor = null;
                return false;
            }
            Title = detail.Title; IsOwner = detail.IsOwner; MusicsCount = detail.MusicsCount; NextCursor = detail.NextCursor;
            var items = detail.Musics.Select((music, index) => new DiscoveryMusicItemViewModel
            {
                PublicId = music.PublicId, Title = music.Title, PersianTitle = music.PersianTitle,
                Rank = Math.Max(0, startIndex) + index + 1,
                LikesCount = music.LikesCount, CoverMediaPublicId = music.CoverMediaPublicId, CoverDecodeWidth = 144,
                Contributors = music.Contributors.Select(contributor => new DiscoveryContributorItemViewModel
                { FirstName = contributor.FirstName, LastName = contributor.LastName }).ToArray()
            }).ToArray();
            if (cursor is null)
            {
                var coverId = items.FirstOrDefault()?.CoverMediaPublicId;
                // The first page defines the playlist artwork. Paging through songs
                // keeps it stable, and refreshing the same cover avoids a blank flash.
                if (_coverMediaPublicId != coverId)
                {
                    _coverMediaPublicId = coverId;
                    CoverImage = null;
                }
            }
            Musics.Clear();
            foreach (var item in items) Musics.Add(item);
            HasLoaded = true;
            _ = LoadCoversAsync(items, version, owner, token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception exception)
        {
            if (IsCurrent()) { HasError = true; NextCursor = null; AppLog.Error("Playlist details failed.", exception); }
            return false;
        }
        finally { if (IsCurrent()) { IsLoading = false; Notify(); } }
        bool IsCurrent() => !_disposed && version == _version && !token.IsCancellationRequested && api.GetToken() == owner;
    }

    private async Task LoadCoversAsync(DiscoveryMusicItemViewModel[] items, int version, string? owner, CancellationToken token)
    {
        var covers = new Dictionary<Guid, DiscoveryMusicItemViewModel[]>();
        // A page change can cancel the first page's thumbnail download. Carry its
        // pending hero into this page's bounded batch without duplicating a request.
        if (CoverImage is null && _coverMediaPublicId is Guid heroId) covers[heroId] = [];
        foreach (var group in items.Where(item => item.CoverMediaPublicId.HasValue)
            .GroupBy(item => item.CoverMediaPublicId!.Value))
            covers[group.Key] = group.ToArray();

        foreach (var batch in covers.Chunk(3))
        {
            if (!IsCurrent()) return;
            await Task.WhenAll(batch.Select(async cover =>
            {
                try
                {
                    var response = await api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = cover.Key }), token);
                    if (!IsCurrent() || !response.IsSuccess || response.Content is not { } media) return;
                    if (!Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri)) return;
                    var path = await downloader.DownloadToCacheAsync(media, uri, cancellationToken: token);
                    if (IsCurrent())
                    {
                        foreach (var item in cover.Value) item.CoverImagePath = path;
                        // Reuse the same cached download; only the hero gets a larger decode.
                        if (CoverImage is null && cover.Key == _coverMediaPublicId)
                            CoverImage = ImageSourceFactory.Create(path, 400);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception exception) { AppLog.Error("Playlist music cover failed.", exception); }
            }));
        }
        bool IsCurrent() => !_disposed && version == _version && !token.IsCancellationRequested && api.GetToken() == owner;
    }

    private void Notify()
    {
        foreach (var name in new[] { nameof(PublicId), nameof(Title), nameof(MusicsCount), nameof(CountText), nameof(IsLoading),
            nameof(HasError), nameof(HasLoaded), nameof(IsOwner), nameof(CanPlay), nameof(NextCursor), nameof(EmptyVisibility) }) OnPropertyChanged(name);
    }
    public void CancelLoading()
    {
        _version++; _load?.Cancel(); _load?.Dispose(); _load = null;
        IsLoading = false; Notify();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _version++; _load?.Cancel(); _load?.Dispose(); _load = null; Musics.Clear();
        _ownerToken = null; _coverMediaPublicId = null; CoverImage = null;
    }
}
