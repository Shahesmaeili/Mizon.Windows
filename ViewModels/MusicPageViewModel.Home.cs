using Mizon.API.Client.Operations.Musics;
using Mizon.API.Client.Operations.Stories;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class MusicPageViewModel
{
    private CancellationTokenSource? _homeLoadCancellation;
    private int _homeLoadVersion;
    public Microsoft.UI.Xaml.Visibility HomeStoriesRetryVisibility { get => field; private set => SetField(ref field, value); } = Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility HomeGenresRetryVisibility { get => field; private set => SetField(ref field, value); } = Microsoft.UI.Xaml.Visibility.Collapsed;
    public bool IsHomeGenresLoading { get => field; private set => SetField(ref field, value); } = true;
    public string HomeGenresMessage
    {
        get => string.IsNullOrEmpty(field) ? string.Empty : LocalizationService.Current.GetString(field);
        private set
        {
            if (SetField(ref field, value)) OnPropertyChanged(nameof(HomeGenresStatusVisibility));
        }
    } = string.Empty;
    public Microsoft.UI.Xaml.Visibility HomeGenresStatusVisibility => string.IsNullOrEmpty(HomeGenresMessage)
        ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public async Task RefreshHomeWidgetsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancelHomeWidgetLoading(false);
        var cancellation = _homeLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var version = _homeLoadVersion;
        IsHomeStoriesLoading = true;
        IsHomeGenresLoading = true;
        try
        {
            // Independent widgets become ready independently; no sequence of API waits.
            await Task.WhenAll(LoadHomeGenresAsync(version, cancellation.Token), LoadHomeStoriesAsync(version, cancellation.Token));
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            if (ReferenceEquals(cancellation, _homeLoadCancellation))
                _homeLoadCancellation = null;
            // The request owner disposes only after both operations have drained.
            if (version == _homeLoadVersion)
            {
                IsHomeStoriesLoading = false;
                IsHomeGenresLoading = false;
            }
            cancellation.Dispose();
        }
    }

    public void RefreshHomeLocalization()
    {
        foreach (var story in HomeStories) story.RefreshLocalization();
        foreach (var genre in MusicGenreTypes) genre.RefreshLocalization();
        OnPropertyChanged(nameof(HomeGenresMessage));
        OnPropertyChanged(nameof(HomeStoriesMessage));
    }

    public void CancelHomeWidgetLoading() => CancelHomeWidgetLoading(true);

    private void CancelHomeWidgetLoading(bool notify)
    {
        _homeLoadVersion++;
        _homeLoadCancellation?.Cancel();
        _homeLoadCancellation = null;
        if (notify)
        {
            IsHomeStoriesLoading = false;
            IsHomeGenresLoading = false;
            foreach (var story in HomeStories) story.ReleaseImages();
        }
    }

    private bool IsCurrentHomeLoad(int version, CancellationToken token) => version == _homeLoadVersion && !token.IsCancellationRequested;

    private async Task LoadHomeGenresAsync(int version, CancellationToken token)
    {
        HomeGenresMessage = string.Empty;
        HomeGenresRetryVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        try
        {
            var response = await MizonApi.SendRequestAsync(new MusicGenreTypesOperation(new()), token);
            if (!IsCurrentHomeLoad(version, token)) return;
            if (!response.IsSuccess || response.Content is null)
            {
                HomeGenresMessage = "MusicHome/GenresFailed";
                HomeGenresRetryVisibility = Microsoft.UI.Xaml.Visibility.Visible;
                return;
            }
            var existing = MusicGenreTypes.ToDictionary(item => item.PublicId);
            var items = response.Content.MusicGenreTypes.Select(genre =>
            {
                if (existing.TryGetValue(genre.PublicId, out var item) && item.Title == genre.Title &&
                    item.PersianTitle == genre.PersianTitle && item.AccentColorHex == genre.AccentColorHex &&
                    item.IconSymbolName == genre.IconSymbol && item.MusicsCount == genre.MusicsCount) return item;
                return new MusicGenreTypeItemViewModel
                {
                    PublicId = genre.PublicId, Title = genre.Title, PersianTitle = genre.PersianTitle,
                    AccentColorHex = genre.AccentColorHex, IconSymbolName = genre.IconSymbol, MusicsCount = genre.MusicsCount
                };
            }).ToArray();
            SynchronizeWidgetItems(MusicGenreTypes, items);
            if (items.Length == 0) HomeGenresMessage = "MusicHome/NoGenres";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsCurrentHomeLoad(version, token)) return;
            AppLog.Error("Home genres could not be loaded.", exception);
            HomeGenresMessage = "MusicHome/GenresFailed";
            HomeGenresRetryVisibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        finally
        {
            if (IsCurrentHomeLoad(version, token)) IsHomeGenresLoading = false;
        }
    }

    private async Task LoadHomeStoriesAsync(int version, CancellationToken token)
    {
        HomeStoriesMessage = string.Empty;
        HomeStoriesRetryVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        try
        {
            var response = await MizonApi.SendRequestAsync(new StoriesOperation(), token);
            if (!IsCurrentHomeLoad(version, token)) return;
            if (!response.IsSuccess || response.Content is null)
            {
                HomeStoriesMessage = "MusicHome/StoriesFailed";
                HomeStoriesRetryVisibility = Microsoft.UI.Xaml.Visibility.Visible;
                return;
            }
            var existing = HomeStories.ToDictionary(item => item.PublicId);
            var items = response.Content.Stories.Select(story =>
            {
                if (existing.TryGetValue(story.PublicId, out var item) && item.PreviewMediaPublicId == story.PreviewMediaPublicId &&
                    item.Title == story.Title && item.CreatorFirstName == story.CreatorFirstName && item.CreatorLastName == story.CreatorLastName)
                {
                    item.ViewedByMe = story.ViewedByMe;
                    item.CreatorProfileMediaPublicId = story.CreatorProfileMediaPublicId;
                    return item;
                }
                return new StoryItemViewModel
                {
                    PublicId = story.PublicId, PreviewMediaPublicId = story.PreviewMediaPublicId,
                    CreatorProfileMediaPublicId = story.CreatorProfileMediaPublicId,
                    CreatorFirstName = story.CreatorFirstName, CreatorLastName = story.CreatorLastName,
                    Title = story.Title, ViewedByMe = story.ViewedByMe
                };
            }).ToArray();
            var retainedItems = items.ToHashSet();
            foreach (var oldItem in HomeStories)
                if (!retainedItems.Contains(oldItem)) oldItem.ReleaseImages();
            SynchronizeWidgetItems(HomeStories, items);
            if (items.Length == 0) HomeStoriesMessage = "MusicHome/NoStories";
            // Publish metadata immediately. The rail hydrates only realized cards and
            // owns the bounded image queue and each card's cancellation lifetime.
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsCurrentHomeLoad(version, token)) return;
            AppLog.Error("Home stories could not be loaded.", exception);
            HomeStoriesMessage = "MusicHome/StoriesFailed";
            HomeStoriesRetryVisibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        finally
        {
            if (IsCurrentHomeLoad(version, token)) IsHomeStoriesLoading = false;
        }
    }

    public async Task LoadHomeStoryImagesAsync(StoryItemViewModel story, CancellationToken cancellationToken)
    {
        var version = _homeLoadVersion;
        bool IsCurrent() => IsCurrentHomeLoad(version, cancellationToken) && HomeStories.Contains(story);
        var loadPreview = false;
        Guid? profileId = null;
        var loadProfile = false;
        if (!await UpdateStoryImagesOnDispatcherAsync(() =>
        {
            story.RestoreImages();
            loadPreview = string.IsNullOrEmpty(story.PreviewImagePath);
            profileId = story.CreatorProfileMediaPublicId;
            loadProfile = profileId.HasValue && string.IsNullOrEmpty(story.CreatorProfileImagePath);
        }, cancellationToken, IsCurrent)) return;

        if (loadPreview)
            await LoadStoryImageSafelyAsync(story, story.PreviewMediaPublicId, false, cancellationToken, IsCurrent);
        cancellationToken.ThrowIfCancellationRequested();
        if (loadProfile && profileId is Guid mediaPublicId)
            await LoadStoryImageSafelyAsync(story, mediaPublicId, true, cancellationToken,
                () => IsCurrent() && story.CreatorProfileMediaPublicId == mediaPublicId);
    }

    private static void SynchronizeWidgetItems<T>(System.Collections.ObjectModel.ObservableCollection<T> target, T[] items)
    {
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (index < target.Count && ReferenceEquals(target[index], item)) continue;
            var previous = target.IndexOf(item);
            if (previous >= 0) target.Move(previous, index);
            else target.Insert(index, item);
        }
        while (target.Count > items.Length) target.RemoveAt(target.Count - 1);
    }
}
