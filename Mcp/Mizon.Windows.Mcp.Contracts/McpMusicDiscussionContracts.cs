namespace Mizon.Windows.Mcp.Contracts;

public sealed record McpDiscussionTag(string Type, Guid PublicId, string? Label = null);

public sealed record McpMusicDiscussionRequest
{
    public string Action { get; init; } = string.Empty;
    public Guid? CommentPublicId { get; init; }
    public string? Sort { get; init; }
    public int? Vote { get; init; }
    public string? Text { get; init; }
    public int? TimestampMilliseconds { get; init; }
    public McpDiscussionTag[]? Tags { get; init; }
    public string? TagType { get; init; }
    public string? Query { get; init; }

    public bool IsValid()
    {
        if (CommentPublicId == Guid.Empty || Sort is not (null or "newest" or "oldest" or "likes" or "dislikes") ||
            Vote is < -1 or > 1 || TimestampMilliseconds is < 0 or >= 86400000 ||
            (Tags is not null && (Tags.Length > 8 || Tags.Any(tag => tag is null ||
                tag.PublicId == Guid.Empty || !IsTagType(tag.Type) || tag.Label is not null))))
            return false;
        var hasPostFields = Text is not null || TimestampMilliseconds.HasValue || Tags is not null;
        var hasLookup = TagType is not null || Query is not null;
        return Action switch
        {
            "post" => Text?.Trim() is { Length: > 0 and <= 2000 } && Sort is null && Vote is null && !hasLookup,
            "tag_search" => !hasPostFields && CommentPublicId is null && Sort is null && Vote is null &&
                IsTagType(TagType) && Query?.Trim() is { Length: >= 2 and <= 80 },
            "load" => !hasPostFields && !hasLookup && CommentPublicId is null && Vote is null,
            "next" => !hasPostFields && !hasLookup && CommentPublicId is null && Vote is null && Sort is null,
            "replies" or "focus" or "play_timestamp" => !hasPostFields && !hasLookup && CommentPublicId.HasValue && Vote is null && Sort is null,
            "vote" => !hasPostFields && !hasLookup && CommentPublicId.HasValue && Vote.HasValue && Sort is null,
            _ => false
        };
    }

    private static bool IsTagType(string? type) => type is "person" or "client" or "music" or "music-video";
}

public sealed record McpMusicDiscussionState(Guid MusicPublicId, bool IsOpen, string Sort, Guid? ParentCommentPublicId,
    bool HasMore, bool IsLoading, McpDiscussionComment[] Comments, McpDiscussionTag[]? TagResults = null);

public sealed record McpDiscussionComment(Guid PublicId, string Text, string Author, string? Platform, long? TimestampMilliseconds,
    int Likes, int Dislikes, int MyVote, int Replies, string? Link, McpDiscussionTag[] Tags);
