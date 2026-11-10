using Mizon.API.Client.Responses.Media;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class MusicPageViewModel
{
    private readonly Dictionary<Guid, Task> _activeMusicDownloads = [];
    private Guid _playingMusicPublicId;
    internal Guid CurrentMusicPublicId => _musicPublicId;

    // An explicit "play" activation is idempotent. The on-page button remains a toggle.
    internal async Task<bool> ActivateMusicAsync(Guid musicPublicId, MusicActivationAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_musicPublicId != musicPublicId || _musicLoadCancellationTokenSource?.IsCancellationRequested != false)
            BeginMusicLoad(musicPublicId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _musicLoadCancellationTokenSource!.Token);
        var token = linked.Token;
        await _musicLoadTask.WaitAsync(token);
        var media = _musicMediaResponse;
        if (!IsCurrentMusicLoad(musicPublicId, token) || media is null) return false;

        if (action == MusicActivationAction.Download)
        {
            await EnsureMusicDownloadAsync(media).WaitAsync(token);
            return IsCurrentMusicLoad(musicPublicId, token);
        }
        if (action != MusicActivationAction.Play) return true;

        // A pending on-page AudioGraph transition must finish before a protocol action.
        while (_isPlaybackTransitioning || _isStemMixerSwitching) await Task.Delay(25, token);
        token.ThrowIfCancellationRequested();
        if (_playingMusicPublicId == musicPublicId)
        {
            if (_audioPlayerService.AudioPlayerState == AudioPlayerState.Playing) return true;
            if (_audioPlayerService.AudioPlayerState == AudioPlayerState.Paused)
            {
                _isPlaybackTransitioning = true;
                try { await _audioPlayerService.ResumeAsync(); }
                finally { _isPlaybackTransitioning = false; }
                token.ThrowIfCancellationRequested();
                return IsCurrentMusicLoad(musicPublicId, token);
            }
        }
        // Hold the same guard as the on-page toggle across stop AND startup. In particular,
        // never let a toggle resume the previous source while AudioGraph is replacing it.
        _isPlaybackTransitioning = true;
        try
        {
            await _audioPlayerService.StopAsync();
            token.ThrowIfCancellationRequested();
            var mixed = IsStemMixerEnabled && IsStemMixAvailable
                && await PlayStemMixAsync(TimeSpan.Zero, AudioPlayerState.Playing, token);
            if (!mixed)
            {
                token.ThrowIfCancellationRequested();
                _ = EnsureMusicDownloadAsync(media);
                await PlayMainMusicAsync(media, TimeSpan.Zero, AudioPlayerState.Playing, token);
            }
            // Native AudioGraph node creation itself cannot be cancelled. Retire a late
            // source before a newer activation gets the playback guard.
            if (token.IsCancellationRequested || _musicPublicId != musicPublicId)
            {
                await _audioPlayerService.StopAsync();
                token.ThrowIfCancellationRequested();
                return false;
            }
            _playingMusicPublicId = musicPublicId;
            if (_audioPlayerService.AudioPlayerState == AudioPlayerState.Playing)
                _playbackActivity.RecordStarted(musicPublicId);
            UpdateColors();
            return _audioPlayerService.AudioPlayerState == AudioPlayerState.Playing;
        }
        finally { _isPlaybackTransitioning = false; }
    }

    private Task EnsureMusicDownloadAsync(MediaResponse media)
    {
        if (File.Exists(_mediaDownloader.GetCachedFilePath(media)))
        {
            IsMusicDownloading = false;
            MusicDownloadProgress = 100;
            return Task.CompletedTask;
        }
        if (_activeMusicDownloads.TryGetValue(media.MediaPublicId, out var existing))
        {
            IsMusicDownloading = true;
            return existing;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _activeMusicDownloads.Add(media.MediaPublicId, completion.Task);
        _ = DownloadMusicAndCompleteAsync(media, _musicPublicId, completion);
        return completion.Task;
    }

    private async Task DownloadMusicAndCompleteAsync(MediaResponse media, Guid musicPublicId, TaskCompletionSource completion)
    {
        try
        {
            await DownloadMusicAsync(media, musicPublicId);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            AppLog.Error($"Music cache download failed. MusicPublicId='{musicPublicId}'.", exception);
            completion.TrySetException(exception);
            // Playback streams independently, so its cache download can have no awaiting
            // caller. Explicit activation callers still receive this same failure.
            _ = completion.Task.Exception;
        }
        finally { _activeMusicDownloads.Remove(media.MediaPublicId); }
    }
}
