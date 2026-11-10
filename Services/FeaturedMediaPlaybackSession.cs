using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Responses.Media;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Mizon.Windows.Services;

/// <summary>
/// Owns the featured video's decoder and rendering surface. Images are managed by
/// the feature view model. All public members are used on the surface's dispatcher.
/// </summary>
public sealed partial class FeaturedMediaPlaybackSession : IDisposable
{
    private readonly MediaPlayerElement _surface;
    private readonly MediaDownloader _downloader;
    private readonly DispatcherQueue _dispatcher;
    private MediaPlayer? _player;
    private MediaSource? _source;
    private CancellationTokenSource? _load;
    private int _version;
    private int _stateNotificationQueued;
    private bool _disposed;
    private bool _active = true;
    private bool _playRequested;
    private bool _muted = true;
    private double _volume = .7;
    private MediaPlaybackState _lastPlaybackState;
    private MediaPlaybackState? _requestedNativeState;

    public FeaturedMediaPlaybackSession(MediaPlayerElement surface, MediaDownloader downloader)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(downloader);
        _surface = surface;
        _downloader = downloader;
        _dispatcher = surface.DispatcherQueue;
        VerifyThread();
        _surface.AutoPlay = false;
        _surface.AreTransportControlsEnabled = false;
    }

    public event EventHandler? Changed;
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;

    public bool IsLoading { get; private set; }
    public bool IsReady { get; private set; }
    public double DownloadProgress { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public string? CachedPath { get; private set; }
    public bool WantsToPlay => !_disposed && _playRequested;
    public bool IsPlaying => IsReady && Read(static player => player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing);
    public bool IsBuffering => Read(static player => player.PlaybackSession.PlaybackState is MediaPlaybackState.Opening or MediaPlaybackState.Buffering);
    public bool CanSeek => IsReady && Read(static player => player.PlaybackSession.CanSeek);
    public TimeSpan Position => Read(static player => player.PlaybackSession.Position);
    public TimeSpan Duration => Read(static player => player.PlaybackSession.NaturalDuration);

    public bool IsMuted
    {
        get => Read(static player => player.IsMuted, _muted);
        set
        {
            VerifyThread();
            if (_disposed || IsMuted == value) return;
            _muted = value;
            if (_player is not null) TryControl(player => player.IsMuted = value);
            OnChanged();
        }
    }

    public double Volume
    {
        get => Read(static player => player.Volume, _volume);
        set
        {
            VerifyThread();
            if (_disposed || !double.IsFinite(value)) return;
            value = Math.Clamp(value, 0, 1);
            if (Volume == value) return;
            _volume = value;
            if (_player is not null) TryControl(player => player.Volume = value);
            OnChanged();
        }
    }

    /// <summary>
    /// Reuses the shared media cache, including partial download resume. The file
    /// is complete before the decoder opens it, so seeking never reads an incomplete
    /// MP4 index. Returns when the source is attached; IsReady changes at MediaOpened.
    /// </summary>
    public async Task<bool> OpenVideoAsync(MediaResponse media, Uri storageUri, bool autoPlay = false,
        CancellationToken cancellationToken = default)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(storageUri);
        if (!storageUri.IsAbsoluteUri || storageUri.Scheme is not ("http" or "https" or "file"))
            throw new ArgumentException("The featured video needs an absolute HTTP(S) or file URI.", nameof(storageUri));

        StopCore();
        var version = _version;
        var cancellation = _load = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cancellation.Token;
        _playRequested = autoPlay;
        IsLoading = true;
        OnChanged();
        try
        {
            var progress = new DownloadProgressReporter(this, version, token);
            var path = await _downloader.DownloadToCacheAsync(media, storageUri,
                progress: progress, cancellationToken: token);
            if (!IsCurrent(version, token)) return false;

            CachedPath = path;
            DownloadProgress = 100;
            var player = new MediaPlayer
            {
                AutoPlay = false,
                IsLoopingEnabled = false,
                IsMuted = _muted,
                Volume = _volume
            };
            _player = player;
            // This is an inline video, not a second Windows music session. The
            // application's AudioGraph player keeps all existing SMTC ownership.
            player.CommandManager.IsEnabled = false;
            player.SystemMediaTransportControls.IsEnabled = false;
            player.MediaOpened += Player_Opened;
            player.MediaFailed += Player_Failed;
            player.MediaEnded += Player_Ended;
            player.IsMutedChanged += Player_AudioChanged;
            player.VolumeChanged += Player_AudioChanged;
            player.PlaybackSession.PlaybackStateChanged += Playback_StateChanged;
            player.PlaybackSession.NaturalDurationChanged += Playback_InfoChanged;
            _source = MediaSource.CreateFromUri(new Uri(path, UriKind.Absolute));
            _surface.SetMediaPlayer(player);
            player.Source = _source;
            OnChanged();
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (version == _version && !_disposed)
            {
                IsLoading = false;
                _playRequested = false;
                ReleasePlayer();
                OnChanged();
            }
            return false;
        }
        catch (Exception exception)
        {
            if (IsCurrent(version, token)) ShowFailure("پخش ویدیو انجام نشد؛ دوباره تلاش کن.", exception);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_load, cancellation)) _load = null;
            cancellation.Dispose();
        }
    }

    public void Play()
    {
        VerifyThread();
        if (_disposed) return;
        _playRequested = true;
        if (!IsReady || !_active) return;
        TryControl(player =>
        {
            var duration = player.PlaybackSession.NaturalDuration;
            if (duration > TimeSpan.Zero && player.PlaybackSession.Position >= duration && player.PlaybackSession.CanSeek)
                player.PlaybackSession.Position = TimeSpan.Zero;
            RequestNativePlayback(player, true);
        });
    }

    public void Pause()
    {
        VerifyThread();
        if (_disposed) return;
        _playRequested = false;
        if (_player is not null) TryControl(player => RequestNativePlayback(player, false));
    }

    public void Toggle()
    {
        if (_playRequested) Pause();
        else Play();
    }

    public void Seek(TimeSpan position)
    {
        VerifyThread();
        if (_disposed || !CanSeek) return;
        var duration = Duration;
        if (duration <= TimeSpan.Zero) return;
        var target = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks));
        TryControl(player => player.PlaybackSession.Position = target);
        OnChanged();
    }

    /// <summary>Pause offscreen/minimized playback without losing a manual pause.</summary>
    public void SetActive(bool active)
    {
        VerifyThread();
        if (_disposed || _active == active) return;
        _active = active;
        if (_player is null) return;
        if (!active)
        {
            // A visibility pause is not a change to the user's playback choice.
            // Reset the observed baseline before native callbacks are dispatched.
            _lastPlaybackState = MediaPlaybackState.Paused;
            TryControl(player => RequestNativePlayback(player, false));
        }
        else if (_playRequested && IsReady) TryControl(player => RequestNativePlayback(player, true));
    }

    public void Stop()
    {
        VerifyThread();
        if (_disposed) return;
        StopCore();
        OnChanged();
    }

    private void StopCore()
    {
        _version++;
        _load?.Cancel();
        // OpenVideoAsync owns this cancellation source until its finally block.
        _load = null;
        _playRequested = false;
        _lastPlaybackState = MediaPlaybackState.None;
        _requestedNativeState = null;
        IsLoading = IsReady = false;
        DownloadProgress = 0;
        ErrorMessage = string.Empty;
        CachedPath = null;
        ReleasePlayer();
    }

    private bool IsCurrent(int version, CancellationToken token) =>
        !_disposed && version == _version && !token.IsCancellationRequested;

    private sealed class DownloadProgressReporter(FeaturedMediaPlaybackSession owner, int version, CancellationToken token)
        : IProgress<MediaDownloader.MediaDownloadProgress>
    {
        private int _percentage = -1;
        private int _queued;

        public void Report(MediaDownloader.MediaDownloadProgress value)
        {
            // Coalesce before dispatch, not inside Progress<T>'s per-chunk UI
            // callback. At most one pending update retains this load at a time.
            var percentage = (int)Math.Floor(value.Percentage);
            if (Interlocked.Exchange(ref _percentage, percentage) == percentage) return;
            if (Interlocked.Exchange(ref _queued, 1) != 0) return;
            if (!owner._dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _queued, 0);
                if (!owner.IsCurrent(version, token)) return;
                var latest = Volatile.Read(ref _percentage);
                if (owner.DownloadProgress == latest) return;
                owner.DownloadProgress = latest;
                owner.OnChanged();
            })) Interlocked.Exchange(ref _queued, 0);
        }
    }

    private void Player_Opened(MediaPlayer sender, object args) => _dispatcher.TryEnqueue(() =>
    {
        if (_disposed || !ReferenceEquals(sender, _player)) return;
        IsLoading = false;
        IsReady = true;
        _lastPlaybackState = MediaPlaybackState.Paused;
        if (_playRequested && _active) TryControl(player => RequestNativePlayback(player, true));
        OnChanged();
    });

    private void Player_Ended(MediaPlayer sender, object args) => _dispatcher.TryEnqueue(() =>
    {
        if (_disposed || !ReferenceEquals(sender, _player)) return;
        _playRequested = false;
        OnChanged();
        Ended?.Invoke(this, EventArgs.Empty);
    });

    private void Player_Failed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        // Read native event data while the event is alive, never inside a delayed callback.
        var message = args.ErrorMessage;
        var error = args.ExtendedErrorCode;
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed || !ReferenceEquals(sender, _player)) return;
            ShowFailure("این ویدیو قابل پخش نیست؛ دوباره تلاش کن.",
                error ?? new InvalidOperationException(message));
        });
    }

    private void Playback_StateChanged(MediaPlaybackSession sender, object args)
    {
        MediaPlaybackState state;
        try { state = sender.PlaybackState; }
        catch (Exception exception) when (exception is COMException or ObjectDisposedException) { return; }
        var wasActive = _active;
        // State transitions are infrequent, unlike position/progress. Preserve
        // their order so a native fullscreen Play followed by Pause is not lost.
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed || _player is null || !ReferenceEquals(sender, _player.PlaybackSession)) return;
            // An older queued native state must not undo a newer application
            // Play/Pause command. Native fullscreen controls are inferred only
            // when no application command is waiting for acknowledgement.
            var commandPending = _requestedNativeState.HasValue;
            if (_requestedNativeState == state) _requestedNativeState = null;
            if (!commandPending && IsReady && _active && wasActive)
            {
                if (state == MediaPlaybackState.Playing) _playRequested = true;
                else if (state == MediaPlaybackState.Paused
                    && _lastPlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Buffering)
                    _playRequested = false;
            }
            _lastPlaybackState = state;
            OnChanged();
        });
    }

    private void Player_AudioChanged(MediaPlayer sender, object args) => QueueNativeInfoRefresh();
    private void Playback_InfoChanged(MediaPlaybackSession sender, object args) => QueueNativeInfoRefresh();

    private void QueueNativeInfoRefresh()
    {
        if (Interlocked.Exchange(ref _stateNotificationQueued, 1) != 0) return;
        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Interlocked.Exchange(ref _stateNotificationQueued, 0);
            if (_disposed || _player is null) return;
            // Report the current session's state. Old callbacks can only schedule
            // a harmless refresh and cannot change the newly selected source.
            _muted = IsMuted;
            _volume = Volume;
            OnChanged();
        })) Interlocked.Exchange(ref _stateNotificationQueued, 0);
    }

    private void TryControl(Action<MediaPlayer> action)
    {
        var player = _player;
        if (_disposed || player is null) return;
        try { action(player); }
        catch (Exception exception) when (exception is COMException or ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            ShowFailure("کنترل ویدیو در دسترس نیست؛ دوباره تلاش کن.", exception);
        }
    }

    private void RequestNativePlayback(MediaPlayer player, bool playing)
    {
        var desired = playing ? MediaPlaybackState.Playing : MediaPlaybackState.Paused;
        _requestedNativeState = player.PlaybackSession.PlaybackState == desired ? null : desired;
        if (playing) player.Play(); else player.Pause();
    }

    private T Read<T>(Func<MediaPlayer, T> read, T fallback = default!)
    {
        var player = _player;
        if (_disposed || player is null) return fallback;
        try { return read(player); }
        catch (Exception exception) when (exception is COMException or ObjectDisposedException)
        {
            // Reading position/state during native teardown must not tear down the
            // XAML event stack recursively. A later command/event handles failure.
            return fallback;
        }
    }

    private void ShowFailure(string message, Exception exception)
    {
        if (_disposed) return;
        IsLoading = IsReady = false;
        _playRequested = false;
        _requestedNativeState = null;
        ErrorMessage = message;
        ReleasePlayer();
        AppLog.Error("Featured video playback failed.", exception);
        OnChanged();
        Failed?.Invoke(this, message);
    }

    private void ReleasePlayer()
    {
        var player = _player;
        var source = _source;
        _player = null;
        _source = null;
        try
        {
            if (player is not null)
            {
                try
                {
                    // Detach before touching a possibly closed native decoder.
                    // Never clear a different player supplied by a later owner.
                    if (ReferenceEquals(_surface.MediaPlayer, player)) _surface.SetMediaPlayer(null);
                }
                catch (Exception exception) when (exception is COMException or ObjectDisposedException)
                {
                    AppLog.Error("Featured video surface was already closed during cleanup.", exception);
                }
                try
                {
                    player.MediaOpened -= Player_Opened;
                    player.MediaFailed -= Player_Failed;
                    player.MediaEnded -= Player_Ended;
                    player.IsMutedChanged -= Player_AudioChanged;
                    player.VolumeChanged -= Player_AudioChanged;
                    player.PlaybackSession.PlaybackStateChanged -= Playback_StateChanged;
                    player.PlaybackSession.NaturalDurationChanged -= Playback_InfoChanged;
                    player.Pause();
                    player.Source = null;
                }
                catch (Exception exception) when (exception is COMException or ObjectDisposedException)
                {
                    AppLog.Error("Featured video decoder was already closed during cleanup.", exception);
                }
                finally
                {
                    try { player.Dispose(); }
                    catch (Exception exception) when (exception is COMException or ObjectDisposedException)
                    { AppLog.Error("Featured video decoder cleanup failed.", exception); }
                }
            }
        }
        finally
        {
            try { source?.Dispose(); }
            catch (Exception exception) when (exception is COMException or ObjectDisposedException)
            { AppLog.Error("Featured video source cleanup failed.", exception); }
        }
    }

    private void OnChanged()
    {
        if (!_disposed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void VerifyThread()
    {
        if (!_dispatcher.HasThreadAccess)
            throw new InvalidOperationException("Use featured media playback on its UI dispatcher.");
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed) return;
        _disposed = true;
        StopCore();
        Changed = null;
        Ended = null;
        Failed = null;
    }
}
