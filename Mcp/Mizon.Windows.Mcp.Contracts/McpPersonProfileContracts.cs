namespace Mizon.Windows.Mcp.Contracts;

public sealed record McpPersonProfileRequest
{
    public bool? Following { get; init; }
    public Guid? MusicPublicId { get; init; }
}

public sealed record McpPersonProfileState
{
    public Guid PersonPublicId { get; init; }
    public string? DisplayName { get; init; }
    public bool IsLoading { get; init; }
    public bool IsUpdatingFollow { get; init; }
    public bool CanFollow { get; init; }
    public bool IsFollowing { get; init; }
    public int FollowersCount { get; init; }
    public int? FollowingCount { get; init; }
    public int TracksCount { get; init; }
    public bool HasCover { get; init; }
    public bool HasModel { get; init; }
    public bool HasMoreTracks { get; init; }
    public string? ErrorCode { get; init; }
    public McpPersonTrack[] Tracks { get; init; } = [];
}

public sealed record McpPersonTrack(Guid PublicId, string Title);
