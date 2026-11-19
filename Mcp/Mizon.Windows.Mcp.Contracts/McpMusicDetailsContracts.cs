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
    public bool IsPlaying { get; init; }
    public bool RepeatEnabled { get; init; }
    public bool CoverAvailable { get; init; }
    public bool CoverOpen { get; init; }
    public int ContributorCount { get; init; }
    public int ContributorsWithPortrait { get; init; }
}
