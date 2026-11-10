using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Pages;
using Mizon.Windows.ViewModels;
using System;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

public sealed class PictureInPictureService : IDisposable
{
    private readonly MusicPageViewModel _music;
    private readonly IAudioPlayerService _audio;
    private readonly MusicPlaylistPlaybackService _queue;
    private readonly NavigationService _navigation;
    private PictureInPictureWindow? _window;
    private MediaPlayerElement? _videoOwner;
    private readonly Window? _owner;
    private bool _disposed;
    private int _revision;

    public PictureInPictureService(MusicPageViewModel music, IAudioPlayerService audio,
        MusicPlaylistPlaybackService queue, NavigationService navigation)
    {
        _music = music; _audio = audio; _queue = queue; _navigation = navigation;
        _owner = App.MainMizonWindow;
        if (_owner is not null) _owner.Closed += OwnerClosed;
        _navigation.MainNavigationChanged += MainNavigationChanged;
    }

    public async Task ShowMusicAsync()
    {
        EnsureAccess();
        if (_music.CurrentMusicPublicId == Guid.Empty) return;
        var revision = ++_revision;
        var musicId = _music.CurrentMusicPublicId;
        if (_window is not null) await _window.WaitForPendingCommandAsync();
        if (_disposed || revision != _revision || musicId != _music.CurrentMusicPublicId) return;
        if (_window is not null) await _window.PauseVideoAsync();
        if (_disposed || revision != _revision || musicId != _music.CurrentMusicPublicId) return;
        Show(new MusicPictureInPictureSession(_music, _audio, _queue, _navigation));
        _videoOwner = null;
    }

    // Pass the actual video's rendering surface, not a second source/player.
    // The caller must close PiP before disposing its MediaPlayer/owner view.
    public async Task ShowVideoAsync(MediaPlayerElement owner, string title, string? subtitle = null,
        string? artworkPath = null, Action? returnToApp = null,
        Func<Task>? next = null, Func<Task>? previous = null)
    {
        EnsureAccess();
        var revision = ++_revision;
        if (_window is not null && ReferenceEquals(owner, _videoOwner))
        {
            _window.UpdateVideoMetadata(title, subtitle, artworkPath);
            _window.Activate(); return;
        }
        // A pending Play/Next may still be opening its source. Let it finish
        // before pausing, otherwise it could start audio behind the new video.
        if (_window is not null) await _window.WaitForPendingCommandAsync();
        if (_disposed || revision != _revision) return;
        if (_window is not null) await _window.PauseVideoAsync();
        if (_disposed || revision != _revision) return;
        if (_audio.AudioPlayerState == AudioPlayerState.Playing) await _audio.PauseAsync();
        if (_disposed || revision != _revision) return;
        Show(new VideoPictureInPictureSession(owner, title, subtitle, artworkPath, () =>
        {
            if (App.MainMizonWindow is MainWindow window) window.ActivateExistingWindow();
            returnToApp?.Invoke();
        }, next, previous));
        _videoOwner = owner;
    }

    // Next/previous can update the playing episode without stealing window focus.
    public void UpdateVideoMetadata(MediaPlayerElement owner, string title, string? subtitle = null, string? artworkPath = null)
    {
        EnsureAccess();
        if (ReferenceEquals(owner, _videoOwner)) _window?.UpdateVideoMetadata(title, subtitle, artworkPath);
    }

    private void Show(IPictureInPictureSession session)
    {
        try
        {
            EnsureAccess();
            if (_window is null)
            {
                var window = new PictureInPictureWindow(session);
                window.Closed += WindowClosed;
                _window = window;
            }
            else _window.SetSession(session);
            _window.Activate();
        }
        catch
        {
            session.Dispose();
            Close();
            throw;
        }
    }

    private void EnsureAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_owner?.DispatcherQueue.HasThreadAccess != true)
            throw new InvalidOperationException("Open PiP on the main UI dispatcher.");
    }
    public void Close() { ++_revision; _window?.Close(); }
    private void WindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is Window window) window.Closed -= WindowClosed;
        _window = null;
        _videoOwner = null;
        ++_revision;
    }
    private void OwnerClosed(object sender, WindowEventArgs args) => Dispose();
    private void MainNavigationChanged(object? sender, NavigationChangedEventArgs args)
    {
        if (args.IsEnteringPage && args.ToPageType != typeof(HomePage)) Close();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_owner is not null) _owner.Closed -= OwnerClosed;
        _navigation.MainNavigationChanged -= MainNavigationChanged;
        Close();
    }
}
