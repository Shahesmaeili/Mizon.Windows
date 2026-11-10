using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class MusicPageViewModel
{
    private Task _musicLoadTask = Task.CompletedTask;

    private void BeginMusicLoad(Guid musicPublicId)
    {
        _musicLoadCancellationTokenSource?.Cancel();
        _musicLoadCancellationTokenSource?.Dispose();
        _musicLoadCancellationTokenSource = new CancellationTokenSource();
        ResetMusicPresentation();
        _musicPublicId = musicPublicId;
        _musicLoadTask = LoadMusicAfterNavigationAsync(musicPublicId, _musicLoadCancellationTokenSource.Token);
    }

    private async Task LoadMusicAfterNavigationAsync(Guid musicPublicId, CancellationToken token)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => ready.TrySetResult())) return;
        try
        {
            // Give navigation/layout/input a turn before starting all detail requests.
            // No artificial delay and no UI work is moved to a worker thread.
            await ready.Task.WaitAsync(token);
            if (IsCurrentMusicLoad(musicPublicId, token)) await LoadMusicAsync(musicPublicId, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal async Task<bool> PlayFromPlaylistAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_musicPublicId != musicPublicId || _musicLoadCancellationTokenSource?.IsCancellationRequested != false)
            BeginMusicLoad(musicPublicId);

        await _musicLoadTask.WaitAsync(cancellationToken);
        if (!IsCurrentMusicLoad(musicPublicId, cancellationToken) || _musicMediaResponse is null || _isPlaybackTransitioning)
            return false;

        // Stop first so PlayAsync cannot accidentally pause the old song or resume its position.
        await _audioPlayerService.StopAsync();
        if (!IsCurrentMusicLoad(musicPublicId, cancellationToken)) return false;
        await PlayAsync(null, cancellationToken);
        if (!IsCurrentMusicLoad(musicPublicId, cancellationToken))
        {
            // AudioGraph's create-node API is not cancellable. If cancellation arrived
            // during it, release this source before the next queued start acquires playback.
            if (_musicPublicId == musicPublicId) await _audioPlayerService.StopAsync();
            return false;
        }

        // Background queue advances share the player, but do not leave page-only presence timers running.
        if (NavigationService.CurrentInternalPage is not MusicsPage)
        {
            StopAudioRenderWatching();
            _musicListenerPositionTimer.Stop();
            _ = MizonSocket.LeaveEntityAsync();
        }
        return _audioPlayerService.AudioPlayerState == AudioPlayerState.Playing;
    }
}
