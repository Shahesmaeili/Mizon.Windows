using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Media.Playback;

namespace Mizon.Windows.Services;

/// <summary>
/// Borrows an existing video player for music videos, movies and episodes. The
/// caller retains ownership of the player and must close PiP before disposing it.
/// Next/previous callbacks should change Source on that same player, then call
/// UpdateMetadata. To replace the player itself, open a new PiP session instead.
/// All members are used on the owner's UI dispatcher.
/// </summary>
public sealed class VideoPictureInPictureSession : IPictureInPictureSession
{
    private readonly MediaPlayerElement _owner;
    private readonly MediaPlayer _player;
    private readonly Action? _returnToApp;
    private readonly Func<Task>? _next;
    private readonly Func<Task>? _previous;
    private MediaPlayerElement? _surface;
    private bool _disposed;
    private bool _unavailable;
    private bool _commandInProgress;

    public VideoPictureInPictureSession(MediaPlayerElement owner, string title,
        string? subtitle = null, string? artworkPath = null, Action? returnToApp = null,
        Func<Task>? next = null, Func<Task>? previous = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        VerifyThread();
        _player = owner.MediaPlayer ?? throw new ArgumentException("The video surface must have an existing MediaPlayer.", nameof(owner));
        _returnToApp = returnToApp;
        _next = next;
        _previous = previous;
        Title = title ?? string.Empty;
        Subtitle = subtitle ?? string.Empty;
        ArtworkPath = artworkPath;
    }

    public string Title { get; private set; }
    public string Subtitle { get; private set; }
    public string? ArtworkPath { get; private set; }
    public string? AccentColor => null;
    public bool IsVideo => true;
    public object? MediaIdentity => Read(static player => player.Source);
    public float Intensity => 0;
    public bool IsPlaying => Read(static player => player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing);
    public bool IsBusy => _commandInProgress || Read(static player => player.PlaybackSession.PlaybackState is MediaPlaybackState.Opening or MediaPlaybackState.Buffering);
    public bool CanSeek => !_commandInProgress && Duration > TimeSpan.Zero && Read(static player => player.PlaybackSession.CanSeek);
    public bool CanNext => !_disposed && !_unavailable && !_commandInProgress && _next is not null;
    public bool CanPrevious => !_disposed && !_unavailable && !_commandInProgress && _previous is not null;
    public TimeSpan Position => Read(static player => player.PlaybackSession.Position);
    public TimeSpan Duration => Read(static player => player.PlaybackSession.NaturalDuration);
    public double Volume
    {
        get => Read(static player => player.Volume);
        set
        {
            VerifyThread();
            if (_disposed || _unavailable || !double.IsFinite(value)) return;
            try { _player.Volume = Math.Clamp(value, 0, 1); }
            catch (Exception error) when (IsClosed(error)) { MarkUnavailable(error); }
        }
    }

    public void UpdateMetadata(string title, string? subtitle = null, string? artworkPath = null)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Title = title ?? string.Empty;
        Subtitle = subtitle ?? string.Empty;
        ArtworkPath = artworkPath;
    }

    public Task ToggleAsync()
    {
        VerifyThread();
        if (_disposed || _unavailable || _commandInProgress) return Task.CompletedTask;
        try
        {
            if (IsPlaying) _player.Pause();
            else _player.Play();
        }
        catch (Exception error) when (IsClosed(error)) { MarkUnavailable(error); }
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        VerifyThread();
        if (!CanSeek) return Task.CompletedTask;
        try
        {
            _player.PlaybackSession.Position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Duration.Ticks));
        }
        catch (Exception error) when (IsClosed(error)) { MarkUnavailable(error); }
        return Task.CompletedTask;
    }

    public Task NextAsync() => InvokeNavigationAsync(_next);
    public Task PreviousAsync() => InvokeNavigationAsync(_previous);

    private async Task InvokeNavigationAsync(Func<Task>? command)
    {
        VerifyThread();
        if (_disposed || _unavailable || _commandInProgress || command is null) return;
        _commandInProgress = true;
        try { await command(); }
        catch (Exception error) when (IsClosed(error)) { MarkUnavailable(error); }
        finally { _commandInProgress = false; }
    }

    public void ReturnToApp()
    {
        VerifyThread();
        if (!_disposed) _returnToApp?.Invoke();
    }

    public void AttachVideo(MediaPlayerElement surface)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        if (ReferenceEquals(surface, _owner)) throw new ArgumentException("PiP requires a separate rendering surface.", nameof(surface));
        if (_unavailable) throw new ObjectDisposedException(nameof(MediaPlayer));
        if (ReferenceEquals(_surface, surface)) return;
        DetachVideo();
        // Do not clear Source, pause, seek or create a second player. Only transfer
        // the presentation surface; audio and the playback clock keep running.
        _owner.SetMediaPlayer(null);
        try
        {
            surface.SetMediaPlayer(_player);
            _surface = surface;
        }
        catch
        {
            TryRestoreOwner();
            throw;
        }
    }

    public void DetachVideo()
    {
        VerifyThread();
        var surface = _surface;
        _surface = null;
        if (surface is null) return;
        try { surface.SetMediaPlayer(null); }
        catch (Exception error) when (error is COMException or ObjectDisposedException)
        {
            AppLog.Error("Unable to detach the closing PiP video surface.", error);
        }
        finally { TryRestoreOwner(); }
    }

    private void TryRestoreOwner()
    {
        if (_unavailable) return;
        try
        {
            // Navigation may have assigned a different player while PiP was open.
            // Never replace that new owner session with this older borrowed player.
            if (_owner.MediaPlayer is null) _owner.SetMediaPlayer(_player);
        }
        catch (Exception error) when (error is COMException or ObjectDisposedException)
        {
            MarkUnavailable(error);
        }
    }

    private T Read<T>(Func<MediaPlayer, T> read)
    {
        if (_disposed || _unavailable) return default!;
        try { return read(_player); }
        catch (Exception error) when (IsClosed(error)) { MarkUnavailable(error); return default!; }
    }

    private static bool IsClosed(Exception error) => error is ObjectDisposedException
        // MediaPlayer may report E_ABORT after Dispose, while other WinRT objects
        // use RO_E_CLOSED or RPC_E_DISCONNECTED for the same lifetime transition.
        || error.HResult is unchecked((int)0x80000013) or unchecked((int)0x80010108)
            or unchecked((int)0x80004004);

    private void MarkUnavailable(Exception error)
    {
        if (!_unavailable) AppLog.Error("The caller-owned PiP video player is no longer available.", error);
        _unavailable = true;
    }

    private void VerifyThread()
    {
        if (!_owner.DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("Use the PiP video session on its UI dispatcher.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        DetachVideo();
        _disposed = true;
        // MediaPlayer, its source, transports and playback are owned by the caller.
    }
}
