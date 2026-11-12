using Microsoft.UI.Windowing;
using Mizon.Windows.Common;
using Mizon.Windows.Mcp.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class LoginPage
{
    private AuthGamepadNavigation? _gamepadNavigation;

    private void InitializeGamepad()
    {
        _windowActive = AuthGamepadNavigation.IsForeground(_hostWindow);
        _gamepadNavigation ??= new AuthGamepadNavigation(LoginRoot, LoginScroller,
            AuthForms.HandleGamepadActionAsync, () => AuthForms.GamepadFields,
            AuthForms.GetGamepadFieldId, AuthForms.HideGamepadPasswords);
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
        _windowActive = false;
        DisposeGamepad();
        if (_hostWindow is not null)
        {
            _hostWindow.Activated -= HostWindow_Activated;
            _hostWindow.Closed -= HostWindow_Closed;
        }
        if (_hostAppWindow is not null) _hostAppWindow.Changed -= HostWindow_Changed;
        _hostWindow = null;
        _hostAppWindow = null;
    }
}
