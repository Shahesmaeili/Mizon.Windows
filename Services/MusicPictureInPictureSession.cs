using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Pages;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// The UI polls this lightweight snapshot. Playback always belongs to AudioGraph;
// closing this view only releases its own observers. An explicit playback command
// belongs to the shared player and may finish after this presentation is closed.
public sealed class MusicPictureInPictureSession : IPictureInPictureSession
{
    private readonly MusicPageViewModel _music;
    private readonly IAudioPlayerService _audio;
    private readonly MusicPlaylistPlaybackService _queue;
    private readonly NavigationService _navigation;
    private Guid _musicId;
    private Guid _observedPlayingId;
    private string _title = "موسیقی";
    private string _subtitle = "Mizon";
    private string? _artwork;
    private string? _accentColor;
    private TimeSpan _duration;
    private bool _metadataDirty = true;
    private bool _busy;
    private bool _disposed;

    public MusicPictureInPictureSession(MusicPageViewModel music, IAudioPlayerService audio,
        MusicPlaylistPlaybackService queue, NavigationService navigation)
    {
        _music = music;
        _audio = audio;
        _queue = queue;
        _navigation = navigation;
        _musicId = music.CurrentMusicPublicId;
        _observedPlayingId = music.PictureInPicturePlayingMusicId;
        _music.PropertyChanged += MetadataChanged;
        _music.Contributors.CollectionChanged += ContributorsChanged;
        RefreshMetadata();
    }

    public string Title { get { RefreshMetadata(); return _title; } }
    public string Subtitle { get { RefreshMetadata(); return _subtitle; } }
    public string? ArtworkPath { get { RefreshMetadata(); return _artwork; } }
    public string? AccentColor { get { RefreshMetadata(); return _accentColor; } }
    public bool IsVideo => false;
    public object MediaIdentity { get { RefreshMetadata(); return _musicId; } }
    private bool OwnsCurrentSource
    {
        get
        {
            RefreshMetadata();
            return !_disposed && _musicId != Guid.Empty && _music.PictureInPicturePlayingMusicId == _musicId;
        }
    }
    public bool IsPlaying => OwnsCurrentSource && _audio.AudioPlayerState == AudioPlayerState.Playing;
    public bool IsBusy => _disposed || _busy || _queue.IsTransitioning || _music.IsPictureInPicturePlaybackBusy;
    public bool CanSeek => !IsBusy && OwnsCurrentSource
        && _audio.AudioPlayerState != AudioPlayerState.Idle && _audio.TrackDuration > TimeSpan.Zero;
    public bool CanNext => !IsBusy && OwnsCurrentSource && _queue.CanNext;
    public bool CanPrevious => !IsBusy && OwnsCurrentSource && _queue.CanPrevious;
    public TimeSpan Position => OwnsCurrentSource ? _audio.Position : TimeSpan.Zero;
    public TimeSpan Duration
    {
        get
        {
            RefreshMetadata();
            return OwnsCurrentSource && _audio.TrackDuration > TimeSpan.Zero ? _audio.TrackDuration : _duration;
        }
    }
    public double Volume
    {
        get => _audio.Volume;
        set
        {
            if (!_disposed && double.IsFinite(value)) _music.Volume = Math.Clamp(value, 0, 1) * 100;
        }
    }
    public float Intensity
    {
        get
        {
            var value = _music.ServerWaveformIntensity;
            return IsPlaying && _music.CurrentMusicPublicId == _musicId && float.IsFinite(value)
                ? Math.Clamp(value, 0, 1) : 0;
        }
    }

    public async Task ToggleAsync()
    {
        if (IsBusy) return;
        RefreshMetadata();
        _busy = true;
        try { await _music.TogglePictureInPictureAsync(_musicId, CancellationToken.None); }
        finally { _busy = false; }
    }
    public Task SeekAsync(TimeSpan position)
    {
        if (CanSeek) _audio.SeekTo(position);
        return Task.CompletedTask;
    }
    public async Task NextAsync()
    {
        if (!CanNext) return;
        _busy = true;
        try { await _queue.NextAsync(); }
        finally { _busy = false; }
    }
    public async Task PreviousAsync()
    {
        if (!CanPrevious) return;
        _busy = true;
        try { await _queue.PreviousAsync(); }
        finally { _busy = false; }
    }
    public void ReturnToApp()
    {
        if (_disposed) return;
        RefreshMetadata();
        if (App.MainMizonWindow is MainWindow window) window.ActivateExistingWindow();
        if (_musicId != Guid.Empty && _navigation.CurrentMainPage is HomePage)
            _navigation.InternalNavigate(typeof(MusicsPage), new Dictionary<string, object>
            {
                [PageConstants.MUSIC_ID] = _musicId
            });
    }
    public void AttachVideo(MediaPlayerElement surface) { }
    public void DetachVideo() { }

    private void MetadataChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MusicPageViewModel.Title) or nameof(MusicPageViewModel.PersianTitle)
            or nameof(MusicPageViewModel.CoverImagePath) or nameof(MusicPageViewModel.PrimaryAccentColor)
            or nameof(MusicPageViewModel.Duration))
            _metadataDirty = true;
    }
    private void ContributorsChanged(object? sender, NotifyCollectionChangedEventArgs args) => _metadataDirty = true;

    private void RefreshMetadata()
    {
        if (_disposed) return;
        var playingId = _music.PictureInPicturePlayingMusicId;
        // Merely opening a different detail page does not replace PiP's identity.
        // Follow only a newly-started source, including playlist next/previous.
        if (playingId != _observedPlayingId)
        {
            _observedPlayingId = playingId;
            if (playingId != Guid.Empty)
            {
                _musicId = playingId;
                _title = "موسیقی";
                _subtitle = "Mizon";
                _artwork = null;
                _accentColor = null;
                _duration = TimeSpan.Zero;
                _metadataDirty = true;
            }
        }
        if (!_metadataDirty || _music.CurrentMusicPublicId != _musicId) return;
        _metadataDirty = false;
        if (!string.IsNullOrWhiteSpace(_music.Title)) _title = _music.Title;
        else if (!string.IsNullOrWhiteSpace(_music.PersianTitle)) _title = _music.PersianTitle.Trim('(', ')');
        var names = string.Join("، ", _music.Contributors.Select(item => item.FullName)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());
        _subtitle = string.IsNullOrWhiteSpace(names) ? "Mizon" : names;
        if (!string.IsNullOrWhiteSpace(_music.CoverImagePath)) _artwork = _music.CoverImagePath;
        _accentColor = string.IsNullOrWhiteSpace(_music.PrimaryAccentColor) ? null : _music.PrimaryAccentColor;
        if (_music.PictureInPictureMusicDuration > TimeSpan.Zero) _duration = _music.PictureInPictureMusicDuration;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _music.PropertyChanged -= MetadataChanged;
        _music.Contributors.CollectionChanged -= ContributorsChanged;
        _artwork = null;
        _accentColor = null;
    }
}
