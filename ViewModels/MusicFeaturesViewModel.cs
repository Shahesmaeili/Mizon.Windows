using Microsoft.UI.Xaml;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Operations.Musics;
using Mizon.API.Client.Responses.Media;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

/// <summary>Bounded server selections with shared disk-cache downloads and no video prefetch.</summary>
public sealed partial class MusicFeaturesViewModel(MizonApi api, MediaDownloader downloader) : ViewModelBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Guid> _likesInProgress = [];
    private CancellationTokenSource? _loadCancellation;
    private string? _ownerToken;
    private int _version;
    private bool _disposed;

    public ObservableCollection<MusicFeatureItemViewModel> Items { get; } = [];
    public bool IsLoading { get; private set; } = true;
    public bool HasLoaded { get; private set; }
    public bool HasError { get; private set; }
    public bool HasItems => Items.Count > 0;
    public string StatusText => HasError ? "دریافت پیشنهادهای میزون انجام نشد. دوباره امتحان کن."
        : "هنوز پیشنهادی برای نمایش ثبت نشده است.";
    public string ActionStatusText { get => field; private set => SetField(ref field, value); } = string.Empty;
    public Visibility EmptyVisibility => HasLoaded && !IsLoading && !HasError && !HasItems ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        CancelLoading(false);
        var owner = api.GetToken();
        if (owner != _ownerToken)
        {
            Items.Clear();
            HasLoaded = false;
        }
        _ownerToken = owner;
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var token = _loadCancellation.Token;
        var version = _version;
        IsLoading = true;
        HasError = false;
        ActionStatusText = string.Empty;
        NotifyPresentation();
        try
        {
            var result = await api.SendRequestAsync(new MusicFeaturesOperation(new() { Limit = 12 }), token);
            if (!IsCurrent(version, token)) return;
            if (!result.IsSuccess || result.Content is not { } content) { HasError = true; return; }
            var previous = Items.ToDictionary(item => item.PublicId);
            var received = content.Items.Take(12).Select(response =>
            {
                var item = previous.GetValueOrDefault(response.PublicId) ?? new MusicFeatureItemViewModel();
                item.Update(response);
                item.IsLikeBusy = _likesInProgress.Contains(item.MusicPublicId);
                return item;
            }).ToArray();
            for (var index = 0; index < received.Length; index++)
            {
                if (index < Items.Count && ReferenceEquals(Items[index], received[index])) continue;
                var oldIndex = Items.IndexOf(received[index]);
                if (oldIndex >= 0) Items.Move(oldIndex, index); else Items.Insert(index, received[index]);
            }
            while (Items.Count > received.Length) Items.RemoveAt(Items.Count - 1);
            HasLoaded = true;
            _ = LoadPreviewsAsync(received, version, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!IsCurrent(version, token)) return;
            HasError = true;
            AppLog.Error("Music features could not be loaded.", error);
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

    public async Task<MediaResponse?> GetMediaAsync(MusicFeatureItemViewModel item, CancellationToken cancellationToken = default)
    {
        if (_disposed || !Items.Contains(item)) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var version = _version;
        var response = await api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = item.MediaPublicId }), linked.Token);
        if (!IsCurrent(version, linked.Token) || !response.IsSuccess || response.Content is not { } media) return null;
        return media;
    }

    private async Task LoadPreviewsAsync(MusicFeatureItemViewModel[] items, int version, CancellationToken token)
    {
        foreach (var item in items)
        {
            if (item.PreviewImage is not null) continue;
            if (item.ArtworkMediaPublicId is { } id && id != Guid.Empty) item.BeginArtworkLoad();
            else if (!item.IsVideo) item.SetArtworkError("تصویر این پیشنهاد در دسترس نیست.");
        }
        var ids = items.SelectMany(item => new[]
        {
            item.PreviewImage is null ? item.ArtworkMediaPublicId : null,
            item.ArtistProfileImage is null ? item.ArtistProfileMediaPublicId : null
        }).OfType<Guid>().Where(id => id != Guid.Empty).Distinct().ToArray();
        // Every image uses the same media metadata/cache path as the rest of Mizon.
        // Only thumbnails and profiles are fetched here; a video is requested on selection.
        foreach (var batch in ids.Chunk(3))
        {
            if (!IsCurrent(version, token)) return;
            await Task.WhenAll(batch.Select(async id =>
            {
                try
                {
                    var response = await api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = id }), token);
                    if (!IsCurrent(version, token)) return;
                    if (!response.IsSuccess || response.Content is not { } media)
                    {
                        MarkArtworkError(id);
                        return;
                    }
                    if (!Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri))
                    {
                        MarkArtworkError(id);
                        return;
                    }
                    var path = await downloader.DownloadToCacheAsync(media, uri, cancellationToken: token);
                    if (!IsCurrent(version, token)) return;
                    foreach (var item in items)
                    {
                        if (item.ArtworkMediaPublicId == id) item.SetArtwork(path);
                        if (item.ArtistProfileMediaPublicId == id) item.SetArtistProfile(path);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error)
                {
                    if (!IsCurrent(version, token)) return;
                    MarkArtworkError(id);
                    AppLog.Error("Music feature preview download failed.", error);
                }
            }));
            // The hero observes the selected item directly; other visible thumbnails
            // need one coalesced feed notification when their images become ready.
            if (IsCurrent(version, token)) OnPropertyChanged(nameof(Items));
        }

        void MarkArtworkError(Guid id)
        {
            if (!IsCurrent(version, token)) return;
            foreach (var item in items)
                if (item.ArtworkMediaPublicId == id && item.PreviewImage is null)
                    item.SetArtworkError("دریافت تصویر انجام نشد. دوباره تلاش کن.");
        }
    }

    public async Task<bool> ToggleLikeAsync(MusicFeatureItemViewModel item, CancellationToken cancellationToken = default)
    {
        if (_disposed || !Items.Contains(item) || !_likesInProgress.Add(item.MusicPublicId)) return false;
        var musicId = item.MusicPublicId;
        var owner = _ownerToken;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        foreach (var candidate in Items.Where(candidate => candidate.MusicPublicId == musicId)) candidate.IsLikeBusy = true;
        ActionStatusText = string.Empty;
        try
        {
            var response = await api.SendRequestAsync(new MusicLikeOperation(new()
            {
                MusicPublicId = musicId, Liked = !item.LikedByMe
            }), linked.Token);
            if (_disposed || linked.IsCancellationRequested || owner != api.GetToken() || owner != _ownerToken) return false;
            if (!response.IsSuccess || response.Content is not { } content)
            {
                ActionStatusText = "ثبت پسند انجام نشد. دوباره امتحان کن.";
                return false;
            }
            foreach (var candidate in Items.Where(candidate => candidate.MusicPublicId == musicId))
                candidate.ApplyLike(content.LikedByMe, content.LikesCount);
            ActionStatusText = content.LikedByMe ? "آهنگ پسندیده شد." : "پسند آهنگ برداشته شد.";
            return true;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            if (!_disposed && owner == _ownerToken)
            {
                ActionStatusText = "ثبت پسند انجام نشد. دوباره امتحان کن.";
                AppLog.Error("Music feature like could not be updated.", error);
            }
            return false;
        }
        finally
        {
            _likesInProgress.Remove(musicId);
            foreach (var candidate in Items.Where(candidate => candidate.MusicPublicId == musicId)) candidate.IsLikeBusy = false;
        }
    }

    private bool IsCurrent(int version, CancellationToken token) => !_disposed && version == _version
        && !token.IsCancellationRequested && _ownerToken == api.GetToken();

    public void CancelLoading() => CancelLoading(true);

    private void CancelLoading(bool notify)
    {
        _version++;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        IsLoading = false;
        if (notify)
        {
            foreach (var item in Items) item.CancelArtworkLoad();
            NotifyPresentation();
        }
    }

    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasLoaded));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(ErrorVisibility));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        CancelLoading(false);
        _lifetime.Dispose();
        Items.Clear();
        _ownerToken = null;
    }
}
