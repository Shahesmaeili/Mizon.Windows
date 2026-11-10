using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class MusicPageViewModel
{
    internal Guid PictureInPicturePlayingMusicId => _playingMusicPublicId;
    internal bool IsPictureInPicturePlaybackBusy => _isPlaybackTransitioning || _isStemMixerSwitching;
    internal TimeSpan PictureInPictureMusicDuration => _musicDuration;

    internal async Task TogglePictureInPictureAsync(Guid musicId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (musicId == Guid.Empty || IsPictureInPicturePlaybackBusy
            || NavigationService.CurrentMainPage is not HomePage) return;

        // A floating player may refer to the audible song while the main window is
        // browsing a different detail page. Toggle that source without reloading it.
        if (_playingMusicPublicId == musicId && _audioPlayerService.AudioPlayerState != AudioPlayerState.Idle)
        {
            _isPlaybackTransitioning = true;
            try
            {
                if (_audioPlayerService.AudioPlayerState == AudioPlayerState.Playing)
                    await _audioPlayerService.PauseAsync();
                else
                    await _audioPlayerService.ResumeAsync();
                UpdateColors();
            }
            finally { _isPlaybackTransitioning = false; }
            return;
        }

        // Explicit play of an unstarted/different song uses the existing activation
        // path: cache/stream, stems, transition guards and SMTC all remain shared.
        NavigationService.InternalNavigate(typeof(MusicsPage), new Dictionary<string, object>
        {
            [PageConstants.MUSIC_ID] = musicId
        });
        await ActivateMusicAsync(musicId, MusicActivationAction.Play, cancellationToken);
    }
}
