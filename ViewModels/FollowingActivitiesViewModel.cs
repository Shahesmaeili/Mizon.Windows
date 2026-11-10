using Microsoft.UI.Xaml;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.API.Client.Operations.Media;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

// Owned by its page. The feed holds only the pages requested by the user.
public sealed partial class FollowingActivitiesViewModel(MizonApi api, MediaDownloader downloader,
    string scope = "all", int pageSize = 20) : ViewModelBase, IDisposable
{
    private CancellationTokenSource? _loadCancellation;
    private string? _ownerToken, _nextCursor;
    private int _version;
    private bool _disposed;
    public ObservableCollection<FriendMusicActivityItemViewModel> Items { get; } = [];
    public bool IsLoading { get; private set; } = true;
    public bool HasLoaded { get; private set; }
    public bool HasError { get; private set; }
    public bool HasMore { get; private set; }
    public bool HasFollowings { get; private set; }
    public bool IsInitialLoading => IsLoading && !HasLoaded;
    public bool IsEmpty => HasLoaded && !HasError && Items.Count == 0;
    public bool CanLoadMore => HasMore && !IsLoading;
    public string CountText => Items.Count.ToString("N0");
    public string EmptyTitle => HasFollowings ? "هنوز خبری از دوستانت نیست" : "اینجا با دوستانت زنده می‌شود";
    public string EmptyDescription => !HasFollowings
        ? "اول چند نفر را فالو کن؛ فعالیت‌هایشان را اینجا می‌بینی."
        : scope == "music" ? "هنوز فعالیت موسیقی از کسانی که دنبال می‌کنی ثبت نشده؛ پخش، نظر، پسند، پلی‌لیست و استوری‌های دارای تگ آهنگشان اینجا می‌آید."
        : "کسانی که دنبال می‌کنی هنوز فعالیتی ثبت نکرده‌اند. فعالیت تازه‌شان را اینجا می‌بینی.";
    public string StatusText => "دریافت فعالیت‌ها انجام نشد. دوباره امتحان کن.";
    public Visibility EmptyVisibility => IsEmpty && !IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ItemsVisibility => Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MoreVisibility => HasMore ? Visibility.Visible : Visibility.Collapsed;

    public Task RefreshAsync(CancellationToken token = default) => LoadAsync(false, token);
    public Task LoadMoreAsync(CancellationToken token = default) => !CanLoadMore || _nextCursor is null
        ? Task.CompletedTask : LoadAsync(true, token);

    private async Task LoadAsync(bool append, CancellationToken cancellationToken)
    {
        if (_disposed) return;
        var owner = api.GetToken();
        if (!append || owner != _ownerToken)
        {
            CancelLoading(false);
            if (owner != _ownerToken)
            {
                Items.Clear();
                HasLoaded = HasFollowings = HasMore = false;
                _nextCursor = null;
                append = false;
            }
            _ownerToken = owner;
            _loadCancellation = new CancellationTokenSource();
        }
        _loadCancellation ??= new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_loadCancellation.Token, cancellationToken);
        var token = linked.Token;
        var version = _version;
        IsLoading = true;
        HasError = false;
        NotifyPresentation();
        try
        {
            var result = await api.SendRequestAsync(new FollowingActivitiesOperation(new()
            {
                Scope = scope, PageSize = Math.Clamp(pageSize, 1, 40), Cursor = append ? _nextCursor : null
            }), token);
            if (!IsCurrent(version, token)) return;
            if (!result.IsSuccess || result.Content is not { } response) { HasError = true; return; }
            var existing = Items.ToDictionary(item => item.Id);
            var received = response.Activities.Select(activity =>
            {
                var item = existing.TryGetValue(activity.ActivityId, out var value) ? value : new FriendMusicActivityItemViewModel();
                item.Update(activity);
                return item;
            }).ToArray();
            if (append)
            {
                foreach (var item in received) if (!existing.ContainsKey(item.Id)) Items.Add(item);
            }
            else
            {
                for (var index = 0; index < received.Length; index++)
                {
                    if (index < Items.Count && ReferenceEquals(Items[index], received[index])) continue;
                    var previous = Items.IndexOf(received[index]);
                    if (previous >= 0) Items.Move(previous, index); else Items.Insert(index, received[index]);
                }
                while (Items.Count > received.Length) Items.RemoveAt(Items.Count - 1);
            }
            HasFollowings = response.HasFollowings;
            HasMore = response.HasMore;
            _nextCursor = response.NextCursor;
            HasLoaded = true;
            _ = LoadImagesAsync(received, version, _loadCancellation.Token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrent(version, token)) { HasError = true; AppLog.Error("Following activities could not be loaded.", exception); }
        }
        finally
        {
            if (version == _version && !_disposed) { IsLoading = false; NotifyPresentation(); }
        }
    }

    private async Task LoadImagesAsync(FriendMusicActivityItemViewModel[] items, int version, CancellationToken token)
    {
        // Deduplicate avatars/covers per page; existing MediaDownloader owns the disk cache.
        var ids = items.SelectMany(item => new[]
        {
            item.ProfileImage is null ? item.ProfileMediaPublicId : null,
            item.CoverImage is null ? item.CoverMediaPublicId : null
        }).OfType<Guid>().Distinct();
        foreach (var batch in ids.Chunk(3))
        {
            if (!IsCurrent(version, token)) return;
            await Task.WhenAll(batch.Select(async id =>
            {
                try
                {
                    var result = await api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = id }), token);
                    if (!IsCurrent(version, token) || !result.IsSuccess || result.Content is not { } media) return;
                    if (!Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri)) return;
                    var path = await downloader.DownloadToCacheAsync(media, uri, cancellationToken: token);
                    if (!IsCurrent(version, token)) return;
                    foreach (var item in items)
                    {
                        if (item.ProfileMediaPublicId == id) item.SetProfile(path);
                        if (item.CoverMediaPublicId == id) item.SetCover(path);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception exception) { AppLog.Error("Following activity image download failed.", exception); }
            }));
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
        if (notify) NotifyPresentation();
    }
    private void NotifyPresentation()
    {
        foreach (var property in new[] { nameof(IsLoading), nameof(IsInitialLoading), nameof(HasLoaded),
            nameof(HasError), nameof(HasMore), nameof(HasFollowings), nameof(IsEmpty), nameof(CanLoadMore),
            nameof(CountText), nameof(EmptyTitle), nameof(EmptyDescription), nameof(StatusText),
            nameof(EmptyVisibility), nameof(ErrorVisibility), nameof(ItemsVisibility), nameof(MoreVisibility) }) OnPropertyChanged(property);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelLoading(false);
        Items.Clear();
        _ownerToken = null;
    }
}
