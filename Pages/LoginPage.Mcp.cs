using Mizon.Windows.Services;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class LoginPage
{
    internal async Task<bool> SelectMcpModeAsync(bool registration, CancellationToken cancellationToken)
    {
        if (!await McpAuthenticationService.WaitForLoadedAsync(this, cancellationToken)
            || _pageCancellation is not { } lifetime || !ViewModel.ControlsIsEnabled
            || !ReferenceEquals(ViewModel.NavigationService.CurrentMainPage, this)) return false;

        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        // Use the same lazy registration form, flip, title and accessibility path as the UI.
        await AuthForms.SelectModeAsync(registration).WaitAsync(request.Token);
        return IsLoaded && ReferenceEquals(ViewModel.NavigationService.CurrentMainPage, this)
            && ViewModel.IsRegistrationMode == registration;
    }

    internal async Task SubmitMcpFormAsync(CancellationToken cancellationToken)
    {
        if (!await McpAuthenticationService.WaitForLoadedAsync(this, cancellationToken)
            || _pageCancellation is not { } lifetime
            || !ReferenceEquals(ViewModel.NavigationService.CurrentMainPage, this)) return;

        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        await ViewModel.SubmitAsync(request.Token);
        // Successful sign-in unloads this page and cancels its lifetime. Do not
        // turn that expected navigation into a failed MCP authentication result.
        if (!cancellationToken.IsCancellationRequested)
            App.ResumePendingActivation();
    }
}
