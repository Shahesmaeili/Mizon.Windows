using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Testable navigation boundary. It exposes only known app locations, never
// service resolution, credentials, arbitrary URIs or execution commands.
internal interface IAppDeepLinkNavigation
{
    AppNavigationSnapshot GetState();
    Task<AppNavigationOutcome> OpenAsync(AppActivationRequest request, CancellationToken cancellationToken);
}

internal sealed record AppNavigationSnapshot(string Destination, string? Uri, string? PendingUri,
    bool CanGoBack, bool HasSession);

internal sealed record AppNavigationOutcome(bool Success, string Code, string Message, AppDeepLinkResultData? Data = null)
{
    internal static bool WaitsForCompletion(AppActivationRequest request) => request.RouteKey is
        "music-details" or "music-play" or "music-download" or "music-video" or "music-comment"
        or "playlist" or "playlists" or "client" or "person" or "story" or "activities";

    internal static AppNavigationOutcome ForDestination(AppActivationRequest request, string message)
    {
        var stage = WaitsForCompletion(request) ? "completed" : "opened";
        return new(true, stage, message, new()
        {
            Destination = request.RouteKey!,
            Uri = request.ToUri()!,
            CompletionStage = stage,
            PublicId = request.PersonPublicId ?? request.ClientPublicId ?? request.MusicPublicId
                ?? request.PlaylistPublicId ?? request.StoryPublicId,
            CommentPublicId = request.MusicCommentPublicId,
            SettingsSection = request.SettingsSection
        });
    }
}
