namespace Mizon.Windows.Mcp.Contracts;

public sealed record McpMusicDetailsRequest
{
    public bool? RepeatEnabled { get; init; }
    public bool? CoverOpen { get; init; }
}

public sealed record McpMusicDetailsState
{
    public Guid MusicPublicId { get; init; }
    public string? Title { get; init; }
    public string? Duration { get; init; }
    public string? Language { get; init; }
    public bool MusicVideoAvailable { get; init; }
    public int? PlaybackDeviceVolume { get; init; }
    public bool? PlaybackDeviceSilent { get; init; }
    public int PlaybackDeviceCount { get; init; }
    public bool PlaybackDeviceSelected { get; init; }
    public bool IsPlaying { get; init; }
    public bool RepeatEnabled { get; init; }
    public bool MixerAvailable { get; init; }
    public bool MixerEnabled { get; init; }
    public bool MixerLoading { get; init; }
    public bool MixerActive { get; init; }
    public double MixerBalance { get; init; }
    public int WaveformCommentCount { get; init; }
    public bool CoverAvailable { get; init; }
    public bool CoverOpen { get; init; }
    public int ContributorCount { get; init; }
    public int ContributorsWithPortrait { get; init; }
}
