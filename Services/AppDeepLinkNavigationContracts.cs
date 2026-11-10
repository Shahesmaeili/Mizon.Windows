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

internal sealed record AppNavigationOutcome(bool Success, string Code, string Message);
