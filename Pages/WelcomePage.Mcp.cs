using Mizon.Windows.Services;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class WelcomePage
{
    internal bool IsMcpInitializing => _initializing || ViewModel.IsBusy;

    internal async Task<bool> RetryMcpWelcomeAsync(CancellationToken cancellationToken)
    {
        if (!await McpAuthenticationService.WaitForLoadedAsync(this, cancellationToken)
            || _pageCancellation is null || IsMcpInitializing
            || !ReferenceEquals(ViewModel.NavigationService.CurrentMainPage, this)) return false;

        // EnterAsync owns the normal validation, profile handoff and Home navigation.
        await EnterAsync(cancellationToken);
        return true;
    }
}
