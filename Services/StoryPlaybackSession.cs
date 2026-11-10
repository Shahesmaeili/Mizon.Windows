using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Mizon.Windows.Services;

// One story owns one decoder. All public members belong to the surface's UI
// dispatcher; native media events are marshalled back before touching the viewer.
public sealed class StoryPlaybackSession : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private MediaPlayerElement? _surface;
    private MediaPlayer? _player;
    private MediaSource? _source;
    private volatile bool _disposed;
    private bool _openedOnce;
    private bool _playRequested;
    private bool _failed;

    public StoryPlaybackSession(MediaPlayerElement surface, DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _surface = surface;
        _dispatcher = dispatcher;
        VerifyThread();
        surface.AutoPlay = false;
        surface.AreTransportControlsEnabled = false;
    }

    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;

    public bool IsReady { get; private set; }
    public bool IsPlaying => Read(static player => player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing);
    public TimeSpan Position => Read(static player => player.PlaybackSession.Position);
    public TimeSpan Duration => Read(static player => player.PlaybackSession.NaturalDuration);
    public double VideoWidth => Read(static player => (double)player.PlaybackSession.NaturalVideoWidth);
    public double VideoHeight => Read(static player => (double)player.PlaybackSession.NaturalVideoHeight);

    public void Open(Uri uri)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("file" or "http" or "https" or "ms-appx" or "ms-appdata"))
            throw new ArgumentException("The story video needs an absolute media URI.", nameof(uri));
        if (_openedOnce) throw new InvalidOperationException("A story playback session opens one video only.");
        _openedOnce = true;

        try
        {
            var player = _player = new MediaPlayer { AutoPlay = false, IsLoopingEnabled = false };
            // Inline stories must not replace the application's music media session.
            player.CommandManager.IsEnabled = false;
            player.SystemMediaTransportControls.IsEnabled = false;
            player.MediaOpened += Player_Opened;
            player.MediaEnded += Player_Ended;
            player.MediaFailed += Player_Failed;
            _source = MediaSource.CreateFromUri(uri);
            _surface!.SetMediaPlayer(player);
            player.Source = _source;
        }
        catch
        {
            ReleasePlayer();
            throw;
        }
    }

    public void Play()
    {
        VerifyThread();
        if (_disposed || _failed) return;
        _playRequested = true;
        if (IsReady) Control(static player => player.Play());
    }

    public void Pause()
    {
        VerifyThread();
        if (_disposed) return;
        _playRequested = false;
        // Pause immediately, including while an opening callback is queued.
        Control(static player => player.Pause());
    }

    private void Player_Opened(MediaPlayer sender, object args)
    {
        if (_disposed) return;
        _dispatcher.TryEnqueue(() =>
        {
            if (!IsCurrent(sender)) return;
            IsReady = true;
            if (_playRequested) Control(static player => player.Play());
            if (IsCurrent(sender)) Opened?.Invoke(this, EventArgs.Empty);
        });
    }

    private void Player_Ended(MediaPlayer sender, object args)
    {
        if (_disposed) return;
        _dispatcher.TryEnqueue(() =>
        {
            if (!IsCurrent(sender)) return;
            _playRequested = false;
            Ended?.Invoke(this, EventArgs.Empty);
        });
    }

    private void Player_Failed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if (_disposed) return;
        // Event args belong to the native callback; never read them after dispatch.
        string message;
        Exception? error;
        try
        {
            message = args.ErrorMessage;
            error = args.ExtendedErrorCode;
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            message = "پخش ویدیو امکان‌پذیر نیست.";
            error = exception;
        }
        _dispatcher.TryEnqueue(() =>
        {
            if (IsCurrent(sender)) Fail(message, error);
        });
    }

    private bool IsCurrent(MediaPlayer player) => !_disposed && !_failed && ReferenceEquals(player, _player);

    private T Read<T>(Func<MediaPlayer, T> read)
    {
        VerifyThread();
        if (_disposed || _player is null || _failed) return default!;
        try { return read(_player); }
        catch (Exception exception)
        {
            Fail("پخش ویدیو امکان‌پذیر نیست.", exception);
            return default!;
        }
    }

    private void Control(Action<MediaPlayer> control)
    {
        if (_disposed || _player is null || _failed) return;
        try { control(_player); }
        catch (Exception exception) { Fail("پخش ویدیو امکان‌پذیر نیست.", exception); }
    }

    private void Fail(string message, Exception? exception)
    {
        if (_disposed || _failed) return;
        _failed = true;
        IsReady = false;
        _playRequested = false;
        if (exception is not null) AppLog.Error("Story video playback failed.", exception);
        ReleasePlayer();
        Failed?.Invoke(this, string.IsNullOrWhiteSpace(message) ? "پخش ویدیو امکان‌پذیر نیست." : message);
    }

    private void ReleasePlayer()
    {
        var player = _player;
        var source = _source;
        _player = null;
        _source = null;
        IsReady = false;
        if (player is not null)
        {
            Release(() => player.MediaOpened -= Player_Opened);
            Release(() => player.MediaEnded -= Player_Ended);
            Release(() => player.MediaFailed -= Player_Failed);
            // A stale session must never detach a newer story's rendering surface.
            Release(() =>
            {
                if (_surface is not null && ReferenceEquals(_surface.MediaPlayer, player))
                    _surface.SetMediaPlayer(null);
            });
            Release(player.Pause);
            Release(() => player.Source = null);
            Release(player.Dispose);
        }
        if (source is not null) Release(source.Dispose);
    }

    private static void Release(Action release)
    {
        try { release(); }
        catch (Exception exception) { AppLog.Error("Story media resource could not be released.", exception); }
    }

    private void VerifyThread()
    {
        if (!_dispatcher.HasThreadAccess)
            throw new InvalidOperationException("Story playback must be accessed on its UI dispatcher.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        VerifyThread();
        _disposed = true;
        _playRequested = false;
        Opened = null;
        Ended = null;
        Failed = null;
        ReleasePlayer();
        _surface = null;
    }
}
