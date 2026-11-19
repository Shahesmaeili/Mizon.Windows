using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class MusicPageViewModel
{
    private Task _musicLoadTask = Task.CompletedTask;
    private Task<bool> _musicDetailsTask = Task.FromResult(false);

    private void BeginMusicLoad(Guid musicPublicId)
    {
        _musicLoadCancellationTokenSource?.Cancel();
        _musicLoadCancellationTokenSource?.Dispose();
        _musicLoadCancellationTokenSource = new CancellationTokenSource();
        ResetMusicPresentation();
        _musicPublicId = musicPublicId;
        UpdateTrackPlaybackPresentation();
        if (HasCurrentTrackSource)
            _lastMusicPosition = _audioPlayerService.Position;
        var details = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _musicDetailsTask = details.Task;
        _musicLoadTask = LoadMusicAfterNavigationAsync(musicPublicId, _musicLoadCancellationTokenSource.Token, details);
    }

    private async Task LoadMusicAfterNavigationAsync(Guid musicPublicId, CancellationToken token,
        TaskCompletionSource<bool> details)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => ready.TrySetResult()))
        {
            details.TrySetResult(false);
            return;
        }
        try
        {
            // Give navigation/layout/input a turn before starting all detail requests.
            await ready.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            details.TrySetResult(false);
            return;
        }
        if (!IsCurrentMusicLoad(musicPublicId, token))
        {
            details.TrySetResult(false);
            return;
        }

        _ = LoadMusicDetailsAsync(musicPublicId, token, details);
        await LoadMusicAsync(musicPublicId, token);
    }

    private async Task LoadMusicDetailsAsync(Guid musicPublicId, CancellationToken token, TaskCompletionSource<bool> details)
    {
        try { details.TrySetResult(await GetMusicAsync(musicPublicId, token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { details.TrySetResult(false); }
        catch (Exception error)
        {
            details.TrySetResult(false);
            AppLog.Error("Music details could not be loaded.", error);
        }
    }

    internal async Task<bool> WaitForMusicDetailsAsync(Guid musicPublicId, CancellationToken token)
    {
        if (!IsCurrentMusicLoad(musicPublicId, token) || _musicLoadCancellationTokenSource is not { } load) return false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(load.Token, token);
        var currentDetails = _musicDetailsTask;
        var loaded = await currentDetails.WaitAsync(cancellation.Token);
        return loaded && ReferenceEquals(currentDetails, _musicDetailsTask) && IsCurrentMusicLoad(musicPublicId, token);
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
