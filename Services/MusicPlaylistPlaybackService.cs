using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.API;
using Mizon.API.Client.Operations.Musics;
using Mizon.Windows.Pages;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

/// <summary>A bounded, paged queue using the application's existing audio pipeline.</summary>
public sealed class MusicPlaylistPlaybackService : IDisposable
{
    private sealed class Session(Guid playlistId, string? ownerToken) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        public Guid PlaylistId { get; } = playlistId;
        public string? OwnerToken { get; } = ownerToken;
        public CancellationToken Cancellation => _cancellation.Token;
        public Queue<Guid> Pending { get; } = new();
        public string? Cursor { get; set; }
        public bool HasLoaded { get; set; }
        public Guid CurrentMusic { get; set; }
        public List<Guid> History { get; } = new();
        public int HistoryIndex = -1;
        public volatile bool IsStarting = true;
        public int TrackRevision;
        public void Dispose() { _cancellation.Cancel(); _cancellation.Dispose(); }
    }

    private readonly MizonApi _api;
    private readonly NavigationService _navigation;
    private readonly MusicPageViewModel _music;
    private readonly IAudioPlayerService _audio;
    private readonly DispatcherQueue _dispatcher;
    private readonly Window? _window;
    private readonly ITaskbarService _taskbar;
    private Session? _session;
    private Task _transition = Task.CompletedTask;
    private bool _queueNavigation;
    private bool _disposed;

    // The initiating playlist page is leaving as part of this start operation.
    // Its data requests can stop, but its caller token must survive this hand-off.
    internal bool IsNavigatingToPlayback => _queueNavigation;

    public MusicPlaylistPlaybackService(MizonApi api, NavigationService navigation,
        MusicPageViewModel music, IAudioPlayerService audio, DispatcherQueue dispatcher, ITaskbarService taskbar)
    {
        _api = api;
        _navigation = navigation;
        _music = music;
        _audio = audio;
        _dispatcher = dispatcher;
        _taskbar = taskbar;
        _taskbar.NextRequested += NextRequested;
        _taskbar.PreviousRequested += PreviousRequested;
        _window = App.MainMizonWindow;
        if (_window is not null) _window.Closed += WindowClosed;
        _audio.TrackEnded += TrackEnded;
        _navigation.InternalNavigationChanged += NavigationChanged;
        _navigation.MainNavigationChanged += MainNavigationChanged;
    }

    public async Task<bool> StartAsync(Guid playlistPublicId, CancellationToken cancellationToken = default,
        Guid? startMusicPublicId = null, Image? coverSource = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_dispatcher.HasThreadAccess) throw new InvalidOperationException("Start playlist playback on the UI dispatcher.");
        if (playlistPublicId == Guid.Empty) return false;
        Cancel();
        var session = new Session(playlistPublicId, _api.GetToken());
        _session = session;
        using var startCancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation, cancellationToken);
        var token = startCancellation.Token;
        var previous = _transition;
        if (startMusicPublicId is Guid selectedMusic)
        {
            // The row already supplied the identity. Open it before any queue/API await.
            session.CurrentMusic = selectedMusic;
            _queueNavigation = true;
            try { _navigation.InternalNavigate(typeof(MusicsPage), new() { [PageConstants.MUSIC_ID] = selectedMusic }, coverSource); }
            finally { _queueNavigation = false; }
            coverSource = null;
        }
        var start = StartCoreAsync(session, token, previous, startMusicPublicId, coverSource);
        _transition = start;
        return await start;
    }

    private async Task<bool> StartCoreAsync(Session session, CancellationToken token, Task previous, Guid? startMusic, Image? coverSource)
    {
        try
        {
            await previous.WaitAsync(token);
            if (startMusic is Guid selectedMusic)
            {
                var started = await PlayAsync(session, selectedMusic, navigate: false, token);
                if (!started) return false;
                // Populate previous/next independently of opening and starting this song.
                if (IsCurrent(session, token)) await PopulateSelectedQueueAsync(session, selectedMusic, session.Cancellation);
                return true;
            }
            while (IsCurrent(session, token))
            {
                var next = await TakeNextAsync(session, token);
                if (next is null) { Complete(session); return false; }
                // A virtualized row can be recycled while its playlist is loading.
                if (coverSource?.DataContext is ViewModels.Items.DiscoveryMusicItemViewModel item && item.PublicId != next.Value)
                    coverSource = null;
                return await PlayAsync(session, next.Value, navigate: true, token, coverSource: coverSource);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Complete(session); }
        catch (Exception exception)
        {
            AppLog.Error($"Playlist playback failed. PlaylistPublicId='{session.PlaylistId}'.", exception);
            Complete(session);
        }
        return false;
    }

    private async Task PopulateSelectedQueueAsync(Session session, Guid selectedMusic, CancellationToken token)
    {
        try
        {
            var preceding = new List<Guid>();
            while (IsCurrent(session, token))
            {
                var next = await TakeNextAsync(session, token);
                if (next is null || next == selectedMusic)
                {
                    if (!IsCurrent(session, token)) return;
                    if (next == selectedMusic)
                    {
                        session.History.Clear();
                        session.History.AddRange(preceding);
                        session.History.Add(selectedMusic);
                        session.HistoryIndex = session.History.Count - 1;
                    }
                    UpdateNavigation();
                    return;
                }
                preceding.Add(next.Value);
                if (preceding.Count > 127) preceding.RemoveAt(0);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            // A queue lookup failure must not stop the selected song.
            AppLog.Error("Unable to prepare the remaining playlist queue.", error);
            if (IsCurrent(session, token))
            {
                session.Pending.Clear(); session.Cursor = null; session.HasLoaded = true;
                UpdateNavigation();
            }
        }
    }

    private async Task<Guid?> TakeNextAsync(Session session, CancellationToken token)
    {
        while (IsCurrent(session, token))
        {
            if (session.Pending.TryDequeue(out var musicId)) return musicId;
            if (session.HasLoaded && session.Cursor is null) return null;
            var response = await _api.SendRequestAsync(new MusicPlaylistDetailsOperation(new()
            {
                MusicPlaylistPublicId = session.PlaylistId,
                PageSize = 30,
                Cursor = session.Cursor
            }), token);
            if (!IsCurrent(session, token)) return null;
            if (!response.IsSuccess || response.Content is null)
                throw new InvalidOperationException("The playlist is unavailable for this account.");
            var previousCursor = session.Cursor;
            session.Cursor = response.Content.NextCursor;
            session.HasLoaded = true;
            foreach (var music in response.Content.Musics)
                if (music.PublicId != Guid.Empty) session.Pending.Enqueue(music.PublicId);
            if (session.Pending.Count == 0 && (session.Cursor is null || session.Cursor == previousCursor)) return null;
        }
        return null;
    }

    private async Task<bool> PlayAsync(Session session, Guid musicId, bool navigate, CancellationToken token, bool addHistory = true, Image? coverSource = null)
    {
        if (!IsCurrent(session, token)) return false;
        Interlocked.Increment(ref session.TrackRevision);
        session.CurrentMusic = musicId;
        if (navigate)
        {
            _queueNavigation = true;
            try { _navigation.InternalNavigate(typeof(MusicsPage), new() { [PageConstants.MUSIC_ID] = musicId }, coverSource); }
            finally { _queueNavigation = false; }
        }
        if (!IsCurrent(session, token)) return false;
        var started = await _music.PlayFromPlaylistAsync(musicId, token);
        if (!started) Complete(session);
        else
        {
            if (addHistory)
            {
                session.History.Add(musicId);
                if (session.History.Count > 128) session.History.RemoveAt(0);
                session.HistoryIndex = session.History.Count - 1;
            }
            session.IsStarting = false;
            UpdateNavigation();
        }
        return started;
    }

    private void TrackEnded(object? sender, TrackEndedEventArgs args)
    {
        // AudioGraph raises the event off the UI thread. Never touch navigation/XAML there.
        // Capture the source session now: an old queued notification must not skip a
        // newly started playlist after the UI dispatcher becomes available again.
        var session = Volatile.Read(ref _session);
        if (session is null || session.IsStarting) return;
        var revision = Volatile.Read(ref session.TrackRevision);
        _dispatcher.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_session, session) || session.IsStarting || session.TrackRevision != revision
                || _disposed || !_transition.IsCompleted || _audio.AudioPlayeRepeatMode == AudioPlayeRepeatMode.RepeatOne) return;
            var token = session.Cancellation;
            session.IsStarting = true;
            _transition = ContinueAsync(session, token);
        });
    }

    private async Task ContinueAsync(Session session, CancellationToken token, bool manual = false)
    {
        try
        {
            if (!IsCurrent(session, token)) { Complete(session); return; }
            if (!manual && _audio.AudioPlayeRepeatMode == AudioPlayeRepeatMode.RepeatOne) { session.IsStarting = false; UpdateNavigation(); return; }
            UpdateNavigation();
            if (session.HistoryIndex + 1 < session.History.Count)
            {
                await PlayAsync(session, session.History[++session.HistoryIndex], _navigation.CurrentInternalPage is MusicsPage, token, addHistory: false);
                return;
            }
            var next = await TakeNextAsync(session, token);
            if (next is null) { session.IsStarting = false; UpdateNavigation(); return; }
            // Browsing another part of the shell must not force the detail page to the front.
            await PlayAsync(session, next.Value, _navigation.CurrentInternalPage is MusicsPage, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error($"Unable to advance playlist. PlaylistPublicId='{session.PlaylistId}'.", exception);
            Complete(session);
        }
    }

    // Queue paging can continue after a source starts. Only the playback hand-off
    // blocks shared transport commands; background queue preparation must not.
    public bool IsTransitioning => !_disposed && _session is { IsStarting: true };
    public bool CanPrevious => !_disposed && _session is { IsStarting: false, HistoryIndex: > 0 };
    public bool CanNext => !_disposed && _session is { IsStarting: false } session &&
        (session.HistoryIndex + 1 < session.History.Count || session.Pending.Count > 0 || !session.HasLoaded || session.Cursor is not null);
    public event EventHandler? PlaybackNavigationChanged;
    private void UpdateNavigation()
    {
        _taskbar.SetNavigationAvailability(CanPrevious, CanNext);
        PlaybackNavigationChanged?.Invoke(this, EventArgs.Empty);
    }
    public Task NextAsync()
    {
        if (!_dispatcher.HasThreadAccess) throw new InvalidOperationException("Playback navigation requires the UI dispatcher.");
        if (!CanNext || !_transition.IsCompleted || _session is not { } session) return Task.CompletedTask;
        session.IsStarting = true;
        UpdateNavigation();
        return _transition = ContinueAsync(session, session.Cancellation, manual: true);
    }
    public Task PreviousAsync()
    {
        if (!_dispatcher.HasThreadAccess) throw new InvalidOperationException("Playback navigation requires the UI dispatcher.");
        if (!CanPrevious || !_transition.IsCompleted || _session is not { } session) return Task.CompletedTask;
        session.IsStarting = true;
        UpdateNavigation();
        return _transition = PreviousCoreAsync(session, session.Cancellation);
    }
    private async Task PreviousCoreAsync(Session session, CancellationToken token)
    {
        try
        {
            if (!IsCurrent(session, token)) return;
            await PlayAsync(session, session.History[--session.HistoryIndex], _navigation.CurrentInternalPage is MusicsPage, token, addHistory: false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Unable to play the previous playlist track.", error); Complete(session); }
    }
    private async void NextRequested(object? sender, EventArgs args) => await NextAsync();
    private async void PreviousRequested(object? sender, EventArgs args) => await PreviousAsync();
    private bool IsCurrent(Session session, CancellationToken token) => !_disposed && !token.IsCancellationRequested
        && ReferenceEquals(_session, session) && session.OwnerToken == _api.GetToken();

    private void NavigationChanged(object? sender, NavigationChangedEventArgs args)
    {
        if (_queueNavigation || !args.IsEnteringPage || args.ToPageType != typeof(MusicsPage) || _session is not { } session) return;
        if (args.Parameter?.TryGetValue(PageConstants.MUSIC_ID, out var value) == true
            && Guid.TryParse(value?.ToString(), out var musicId) && musicId == session.CurrentMusic) return;
        Cancel();
    }

    private void MainNavigationChanged(object? sender, NavigationChangedEventArgs args)
    {
        if (args.IsEnteringPage || args.FromPageType != typeof(HomePage)) return;
        Cancel();
    }

    private void Complete(Session session)
    {
        if (ReferenceEquals(_session, session)) Cancel();
    }

    public void Cancel()
    {
        var session = _session;
        _session = null;
        session?.Dispose();
        UpdateNavigation();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _audio.TrackEnded -= TrackEnded;
        _taskbar.NextRequested -= NextRequested;
        _taskbar.PreviousRequested -= PreviousRequested;
        _navigation.InternalNavigationChanged -= NavigationChanged;
        _navigation.MainNavigationChanged -= MainNavigationChanged;
        if (_window is not null) _window.Closed -= WindowClosed;
        Cancel();
        PlaybackNavigationChanged = null;
    }

    private void WindowClosed(object sender, WindowEventArgs args) => Dispose();
}

