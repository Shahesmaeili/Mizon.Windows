using Microsoft.UI.Windowing;
using Mizon.Windows.Common;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using Mizon.Windows.Services.Gamepad;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class LoginPage
{
    private AuthGamepadNavigation? _gamepadNavigation;

    private void AuthenticationSubmissionCompleted(object? sender, AuthSubmissionCompletedEventArgs args)
    {
        if (!IsLoaded || _pageCancellation is null || !ReferenceEquals(ViewModel.NavigationService.CurrentMainPage, this)) return;
        if (args.Outcome == AuthSubmissionOutcome.Success)
        {
            if (_hostWindow is not null && _gamepadNavigation?.CreateSuccessFeedback(_hostWindow) is { } ticket)
                args.NavigationParameters = new() { [AuthGamepadSuccessFeedback.ParameterKey] = ticket };
        }
        else
        {
            var lifetime = _pageCancellation;
            var preferPassword = !ViewModel.IsRegistrationMode;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded && ReferenceEquals(_pageCancellation, lifetime) && ViewModel.ControlsIsEnabled
                    && ViewModel.HasError && ReferenceEquals(ViewModel.NavigationService.CurrentMainPage, this))
                    _gamepadNavigation?.RestoreFocusAfterError(preferPassword);
            });
            _gamepadNavigation?.PlayFeedback(args.Outcome == AuthSubmissionOutcome.Validation
                ? AuthGamepadFeedback.Validation : AuthGamepadFeedback.Error);
        }
    }

    private void InitializeGamepad()
    {
        _windowActive = AuthGamepadNavigation.IsForeground(_hostWindow);
        var window = _hostWindow as MainWindow;
        _gamepadNavigation ??= new AuthGamepadNavigation(LoginRoot, LoginScroller,
            AuthForms.HandleGamepadActionAsync, () => AuthForms.GamepadFields,
            AuthForms.GetGamepadFieldId, AuthForms.HideGamepadPasswords,
            windowAction: window is null ? null : action => window.RequestAccountWindowAction(this, action),
            canRestoreWindow: window is null ? null : () => window.CanRestoreAccountWindow,
            isExternalNoticeFocus: window is null ? null : window.IsMcpNoticeFocus);
        UpdateGamepadActivity();
    }

    private void UpdateGamepadActivity() => _gamepadNavigation?.SetActive(
        IsLoaded && _pageCancellation is not null && _windowActive
        && _hostAppWindow is { IsVisible: true } appWindow
        && appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized });

    internal McpAuthGamepadState GetGamepadState() => _gamepadNavigation?.GetState() ?? new();

    internal Task<bool> ApplyGamepadActionAsync(McpAuthGamepadAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLoaded || !ReferenceEquals(ViewModel.NavigationService.CurrentMainPage, this)
            || !ViewModel.ControlsIsEnabled || _gamepadNavigation is null) return Task.FromResult(false);
        return _gamepadNavigation.ApplyAsync(action, cancellationToken);
    }

    private void DisposeGamepad()
    {
        _gamepadNavigation?.Dispose();
        _gamepadNavigation = null;
    }

    private void HostWindow_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        ViewModel.SubmissionCompleted -= AuthenticationSubmissionCompleted;
        _windowActive = false;
        DisposeGamepad();
        DetachHostWindow();
    }
}
