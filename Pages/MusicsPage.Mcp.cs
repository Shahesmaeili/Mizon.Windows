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
        IsPlaying = ViewModel.IsPlaying,
        RepeatEnabled = ViewModel.IsRepeatEnabled,
        CoverAvailable = !string.IsNullOrWhiteSpace(ViewModel.CoverImagePath),
        CoverOpen = _isCoverPreviewOpen,
        ContributorCount = ViewModel.Contributors.Count,
        ContributorsWithPortrait = ViewModel.Contributors.Count(credit => credit.Portrait is not null)
    };

    bool IMcpMusicDetailsPage.SetCoverOpen(bool open) => SetCoverPreview(open, restoreFocus: true);

    void IMcpMusicDetailsPage.SetRepeatEnabled(bool enabled) => ViewModel.IsRepeatEnabled = enabled;
}
