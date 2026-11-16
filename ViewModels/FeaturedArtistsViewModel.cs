using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Operations.Persons;
using Mizon.API.Client.Responses.Persons;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace Mizon.Windows.ViewModels;

public sealed partial class FeaturedArtistsViewModel(MizonApi api, MediaDownloader downloader) : ViewModelBase
{
    private CancellationTokenSource? _lifetime;
    private Task? _request;
    private int _followRevision;
    private readonly HashSet<Guid> _pendingFollows = [];
    private readonly Dictionary<Guid, (int Revision, bool Following, int Count)> _confirmedFollows = [];
    private readonly SemaphoreSlim _mediaSlots = new(3);
    public IReadOnlyList<ArtistSpotlightItemViewModel> Spotlights { get => field; private set => SetField(ref field, value); } = [];
    public IReadOnlyList<TopArtistItemViewModel> Artists { get => field; private set => SetField(ref field, value); } = [];
    public bool IsLoading { get => field; private set => SetField(ref field, value); } = true;
    public bool HasError { get => field; private set => SetField(ref field, value); }
    public string StatusText => IsLoading ? string.Empty : LocalizationService.Current.GetString(HasError
        ? "MusicHome/FeaturedArtistsError" : Artists.Count == 0 ? "MusicHome/FeaturedArtistsEmpty" : string.Empty);
    public Microsoft.UI.Xaml.Visibility ArtistsVisibility => Visible(Artists.Count != 0);
    public Microsoft.UI.Xaml.Visibility StatusVisibility => Visible(ShowStatus);
    public Microsoft.UI.Xaml.Visibility RetryVisibility => Visible(!IsLoading && HasError);
    private static Microsoft.UI.Xaml.Visibility Visible(bool value) =>
        value ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private bool ShowStatus => !IsLoading && (HasError || Artists.Count == 0);

    public Task RefreshAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        _lifetime ??= new();
        return (_request is { IsCompleted: false } ? _request : _request = LoadAsync(_lifetime.Token, token)).WaitAsync(token);
    }

    private async Task LoadAsync(CancellationToken lifetime, CancellationToken caller)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime, caller);
        var token = request.Token;
        var followRevision = _followRevision;
        IsLoading = true;
        HasError = false;
        NotifyStatus();
        try
        {
            var response = await api.SendRequestAsync(new FeaturedPersonsOperation(new() { TypeCode = "music" }), token);
            token.ThrowIfCancellationRequested();
            if (!response.IsSuccess || response.Content is not { } content)
            {
                HasError = true;
                return;
            }
            var spotlights = content.Persons.Where(x => x.PersonPublicId != Guid.Empty).Select(Create).ToArray();
            foreach (var item in spotlights)
            {
                var artist = item.Artist;
                artist.IsFollowBusy = _pendingFollows.Contains(artist.PublicId);
                if (_confirmedFollows.TryGetValue(artist.PublicId, out var write) && write.Revision > followRevision)
                    artist.UpdateFollowing(write.Following, write.Count);
            }
            Artists = spotlights.Select(x => x.Artist).ToArray();
            Spotlights = spotlights;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) HasError = true;
            AppLog.Error("Featured artists could not be loaded.", error);
        }
        finally
        {
            if (!lifetime.IsCancellationRequested)
            {
                IsLoading = false;
                NotifyStatus();
            }
        }
    }

    public void RefreshLocalization()
    {
        foreach (var spotlight in Spotlights)
        {
            spotlight.Artist.RefreshLocalization();
            foreach (var track in spotlight.LatestMusics) track.RefreshLocalization();
        }
        NotifyStatus();
    }

    public void Clear()
    {
        var lifetime = _lifetime;
        _lifetime = null;
        lifetime?.Cancel();
        lifetime?.Dispose();
        _request = null;
        _pendingFollows.Clear();
        _confirmedFollows.Clear();
        _followRevision = 0;
        Artists = [];
        Spotlights = [];
        HasError = false;
        IsLoading = true;
        NotifyStatus();
    }

    internal async Task<bool> SetFollowingAsync(TopArtistItemViewModel artist, bool follow, CancellationToken caller)
    {
        if (_lifetime is not { } lifetime || !artist.CanToggleFollow || !Artists.Contains(artist)) return false;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, caller);
        if (artist.IsFollowed == follow) return true;
        if (!_pendingFollows.Add(artist.PublicId)) return false;
        artist.IsFollowBusy = true;
        try
        {
            var response = await api.SendRequestAsync(new PersonFollowOperation(new()
            {
                PersonPublicId = artist.PublicId, Following = follow
            }), request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!response.IsSuccess || response.Content is not { } result) return false;
            _confirmedFollows[artist.PublicId] = (++_followRevision, result.IsFollowing, result.FollowersCount);
            artist.UpdateFollowing(result.IsFollowing, result.FollowersCount);
            Artists.FirstOrDefault(x => x.PublicId == artist.PublicId && !ReferenceEquals(x, artist))
                ?.UpdateFollowing(result.IsFollowing, result.FollowersCount);
            return true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            AppLog.Error("The artist follow status could not be updated.", error);
            return false;
        }
        finally
        {
            artist.IsFollowBusy = false;
            if (ReferenceEquals(_lifetime, lifetime))
            {
                _pendingFollows.Remove(artist.PublicId);
                var visible = Artists.FirstOrDefault(x => x.PublicId == artist.PublicId && !ReferenceEquals(x, artist));
                visible?.IsFollowBusy = false;
            }
        }
    }

    internal async Task<string> GetMediaPathAsync(Guid id, long maximumBytes, CancellationToken token)
    {
        // The same shared downloader owns the disk cache. Bound concurrent reads
        // when a row of cards is realized after a resize or navigation.
        await _mediaSlots.WaitAsync(token);
        try
        {
            var response = await api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = id }), token);
            token.ThrowIfCancellationRequested();
            if (!response.IsSuccess || response.Content is not { } media || media.FileSize <= 0 || media.FileSize > maximumBytes
                || !Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri)
                || (!uri.IsFile && uri.Scheme is not ("https" or "http")))
                throw new IOException("Featured artist media is unavailable or outside its size limit.");
            return await downloader.DownloadToCacheAsync(media, uri, cancellationToken: token);
        }
        finally { _mediaSlots.Release(); }
    }

    private void NotifyStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusVisibility));
        OnPropertyChanged(nameof(RetryVisibility));
        OnPropertyChanged(nameof(ArtistsVisibility));
    }

    private ArtistSpotlightItemViewModel Create(FeaturedPersonItem person)
    {
        var peak = person.DailyPlays.Length == 0 ? 0 : person.DailyPlays.Max();
        var activity = peak > 0 ? person.DailyPlays.Select(x => (double)x / peak).ToArray() : [];
        var artist = new TopArtistItemViewModel(this, activity)
        {
            PublicId = person.PersonPublicId, FirstName = person.FirstName, LastName = person.LastName,
            AccentColor = ParseColor(person.AccentColor), MonthlyListenersCount = person.MonthlyListenersCount,
            ProfileMediaId = person.ProfileMediaPublicId,
            CoverMediaId = person.CoverMediaPublicId ?? person.LatestMusics.FirstOrDefault()?.CoverMediaPublicId,
            GenreTitle = person.GenreTitle, PersianGenreTitle = person.PersianGenreTitle,
            CanFollow = person.CanFollow
        };
        artist.UpdateFollowing(person.IsFollowing, person.FollowersCount);
        return new()
        {
            Artist = artist, SongCount = person.MusicsCount, DailyPlays = person.DailyPlays,
            ModelMediaId = person.ModelMediaPublicId, ModelAttribution = person.ModelAttribution,
            ModelYawInDegrees = person.ModelYawInDegrees,
            ModelViewScale = Math.Clamp(person.ModelViewScale, .5f, 3f),
            ModelVerticalFocus = Math.Clamp(person.ModelVerticalFocus, -1f, 1f),
            LatestMusics = person.LatestMusics.Select(track => new DiscoveryMusicItemViewModel
            {
                PublicId = track.PublicId, Title = track.Title, PersianTitle = track.PersianTitle,
                CoverMediaPublicId = track.CoverMediaPublicId, CoverDecodeWidth = 96,
                Duration = track.Duration ?? TimeSpan.Zero, HasKnownDuration = track.Duration is not null, LikesCount = track.LikesCount
            }).ToArray()
        };
    }

    private static Color ParseColor(string? value) => value is { Length: 7 } && value[0] == '#'
        && uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)
        ? Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
        : Color.FromArgb(255, 18, 166, 237);
}
