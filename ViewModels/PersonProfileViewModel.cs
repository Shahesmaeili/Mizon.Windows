using Mizon.API;
using Mizon.API.Client.Operations.Persons;
using Mizon.API.Client.Responses.Musics;
using Mizon.API.Client.Responses.Persons;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

internal sealed class PersonProfileViewModel(MizonApi api)
{
    private readonly HashSet<Guid> _loadedTracks = [];
    internal PersonProfileResponse? Profile { get; private set; }
    internal Guid? NextCursor { get; private set; }
    internal ObservableCollection<PersonProfileTrackViewModel> Tracks { get; } = [];

    internal async Task<int?> LoadProfileAsync(Guid id, CancellationToken token)
    {
        var response = await api.SendRequestAsync(new PersonProfileOperation(new() { PersonPublicId = id }), token);
        token.ThrowIfCancellationRequested();
        if (!response.IsSuccess || response.Content is not { } data || data.PersonPublicId != id)
            return response.Error?.Code ?? 0;
        Profile = data;
        return null;
    }

    internal async Task<bool> LoadTracksAsync(Guid id, CancellationToken token)
    {
        var before = NextCursor;
        var response = await api.SendRequestAsync(new PersonMusicsOperation(new()
        {
            PersonPublicId = id, BeforeMusicPublicId = before
        }), token);
        token.ThrowIfCancellationRequested();
        if (!response.IsSuccess || response.Content is not { } page) return false;

        foreach (var item in page.Musics)
            if (item.PublicId != Guid.Empty && _loadedTracks.Add(item.PublicId))
                Tracks.Add(new(item));
        NextCursor = page.NextBeforeMusicPublicId != before ? page.NextBeforeMusicPublicId : null;
        return true;
    }

    internal async Task<bool> SetFollowingAsync(bool follow, CancellationToken token)
    {
        if (Profile is not { CanFollow: true } profile) return false;
        var response = await api.SendRequestAsync(new PersonFollowOperation(new()
        {
            PersonPublicId = profile.PersonPublicId,
            Following = follow
        }), token);
        token.ThrowIfCancellationRequested();
        if (!response.IsSuccess || response.Content is not { } result) return false;
        profile.IsFollowing = result.IsFollowing;
        profile.FollowersCount = result.FollowersCount;
        return true;
    }

    internal void Clear()
    {
        Profile = null;
        Tracks.Clear();
        _loadedTracks.Clear();
        NextCursor = null;
    }
}

public sealed class PersonProfileTrackViewModel(MusicTabMusicItemResponse track) : ViewModelBase
{
    public Guid PublicId => track.PublicId;
    public Guid? CoverMediaPublicId => track.CoverMediaPublicId;
    public string Title => LocalizationService.Current.IsPersian && !string.IsNullOrWhiteSpace(track.PersianTitle)
        ? track.PersianTitle : track.Title;
    public string Credits { get; } = string.Join(" · ", track.Contributors
        .Select(person => $"{person.FirstName} {person.LastName}".Trim()).Where(name => name.Length != 0).Distinct());
    public string Likes => LocalizationService.Current.Format("PersonProfile/TrackLikes", track.LikesCount);

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Likes));
    }
}
