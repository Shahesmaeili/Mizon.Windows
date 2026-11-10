using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Mizon.API;
using Mizon.API.Client.Operations.Musics;
using Mizon.Windows.Pages;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Record successful source starts, never page visits, seeks, resumes or mixer changes.
public sealed class MusicPlaybackActivityService : IDisposable
{
    private readonly MizonApi _api;
    private readonly NavigationService _navigation;
    private readonly IAudioPlayerService _audio;
    private readonly DispatcherQueue _dispatcher;
    private readonly Window? _window;
    private readonly object _stateGate = new();
    private CancellationTokenSource _session = new();
    private Guid _lastStartedMusicPublicId;
    private long _playbackRevision;
    private bool _disposed;

    public MusicPlaybackActivityService(MizonApi api, NavigationService navigation,
        IAudioPlayerService audio, DispatcherQueue dispatcher)
    {
        _api = api;
        _navigation = navigation;
        _audio = audio;
        _dispatcher = dispatcher;
        _audio.TrackRestarted += TrackRestarted;
        _navigation.MainNavigationChanged += NavigationChanged;
        _window = App.MainMizonWindow;
        if (_window is not null) _window.Closed += WindowClosed;
    }

    public void RecordStarted(Guid musicPublicId)
    {
        if (musicPublicId == Guid.Empty) return;
        // A bounded, cancellable background write must never delay AudioGraph or its UI.
        var owner = _api.GetToken();
        if (string.IsNullOrEmpty(owner)) return;
        CancellationToken token;
        lock (_stateGate)
        {
            if (_disposed) return;
            _lastStartedMusicPublicId = musicPublicId;
            _playbackRevision++;
            token = _session.Token;
        }
        var playbackId = Guid.CreateVersion7();
        _ = Task.Run(() => RecordAsync(musicPublicId, playbackId, owner, token));
    }

    private void TrackRestarted(object? sender, EventArgs args)
    {
        Guid musicPublicId;
        long revision;
        lock (_stateGate)
        {
            if (_disposed || _lastStartedMusicPublicId == Guid.Empty) return;
            musicPublicId = _lastStartedMusicPublicId;
            revision = _playbackRevision;
        }
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            lock (_stateGate)
            {
                if (_disposed || revision != _playbackRevision) return;
            }
            RecordStarted(musicPublicId);
        });
    }

    private async Task RecordAsync(Guid musicId, Guid playbackId, string? owner, CancellationToken sessionToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var token = timeout.Token;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (Volatile.Read(ref _disposed) || token.IsCancellationRequested || owner != _api.GetToken()) return;
                try
                {
                    var response = await _api.SendRequestAsync(new MusicPlayOperation(new()
                    {
                        MusicPublicId = musicId, PlaybackPublicId = playbackId
                    }), token).ConfigureAwait(false);
                    if (response.IsSuccess) return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    if (attempt == 1) AppLog.Error("Music playback activity could not be recorded.", exception);
                }
                if (attempt == 0) await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("Music playback activity could not be recorded.", exception);
        }
    }

    private void NavigationChanged(object? sender, NavigationChangedEventArgs args)
    {
        if (!args.IsEnteringPage && args.FromPageType == typeof(HomePage))
        {
            CancellationTokenSource previousSession;
            lock (_stateGate)
            {
                if (_disposed) return;
                _lastStartedMusicPublicId = Guid.Empty;
                _playbackRevision++;
                previousSession = _session;
                _session = new();
            }
            previousSession.Cancel();
            previousSession.Dispose();
        }
    }
    private void WindowClosed(object sender, WindowEventArgs args) => Dispose();
    public void Dispose()
    {
        CancellationTokenSource session;
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            _lastStartedMusicPublicId = Guid.Empty;
            _playbackRevision++;
            session = _session;
        }
        _audio.TrackRestarted -= TrackRestarted;
        _navigation.MainNavigationChanged -= NavigationChanged;
        if (_window is not null) _window.Closed -= WindowClosed;
        session.Cancel();
        session.Dispose();
    }
}
