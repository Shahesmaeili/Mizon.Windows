using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using System;
using System.Linq;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage : IMcpMusicDetailsPage
{
    internal bool IsMusicDetailsActive => _pageActive && MusicPublicId != Guid.Empty && ViewModel.CurrentMusicPublicId == MusicPublicId;

    McpMusicDetailsState IMcpMusicDetailsPage.ReadState() => new()
    {
        MusicPublicId = MusicPublicId,
        Title = ViewModel.Title,
        Duration = ViewModel.Duration,
        Language = Localization.LanguageTag,
        MetadataLoading = ViewModel.IsTrackMetadataLoading,
        MetadataLoadFailed = ViewModel.TrackMetadataLoadFailed,
        WaveformLoading = ViewModel.IsTrackWaveformLoading,
        WaveformCommentsLoading = ViewModel.IsTrackCommentsLoading,
        AudiencePopup = _openAudience,
        ViewsCount = ViewModel.ViewsCount,
        OnlineViewersCount = ViewModel.LiveViewsCount,
        LiveListenerCount = ViewModel.MusicListeners.Count,
        MusicVideoAvailable = ViewModel.HasMusicVideo,
        PlaybackDeviceVolume = ViewModel.SelectedAudioDevice?.OutputVolume?.Percentage,
        PlaybackDeviceSilent = ViewModel.SelectedAudioDevice?.OutputVolume?.IsSilent,
        PlaybackDeviceCount = ViewModel.AudioDevices.Count,
        PlaybackDeviceSelected = ViewModel.SelectedAudioDevice is not null,
        IsPlaying = ViewModel.IsPlaying,
        RepeatEnabled = ViewModel.IsRepeatEnabled,
        MixerAvailable = ViewModel.IsStemMixAvailable,
        MixerEnabled = ViewModel.IsStemMixerEnabled,
        MixerLoading = ViewModel.IsStemMixerLoading,
        MixerActive = ViewModel.IsStemMixerActive,
        MixerBalance = ViewModel.StemMixBalance,
        WaveformCommentCount = ViewModel.EqualizerComments.Count,
        CoverAvailable = !string.IsNullOrWhiteSpace(ViewModel.CoverImagePath),
        CoverOpen = _isCoverPreviewOpen,
        ContributorCount = ViewModel.Contributors.Count,
        ContributorsWithPortrait = ViewModel.Contributors.Count(credit => credit.Portrait is not null)
    };

    bool IMcpMusicDetailsPage.SetCoverOpen(bool open) => SetCoverPreview(open, restoreFocus: true);

    void IMcpMusicDetailsPage.SetRepeatEnabled(bool enabled) => ViewModel.IsRepeatEnabled = enabled;

    bool IMcpMusicDetailsPage.SetAudience(string view) => SetTrackAudience(view);
}
