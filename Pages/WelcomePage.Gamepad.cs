using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Mizon.Windows.Common;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services.Gamepad;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Mizon.Windows.Pages;

public sealed partial class WelcomePage
{
    private AuthGamepadNavigation? _gamepadNavigation;
    private AuthGamepadSuccessFeedback? _successFeedback;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _successFeedback?.Dispose();
        _successFeedback = null;
        if (e.Parameter is Dictionary<string, object> parameters
            && parameters.Remove(AuthGamepadSuccessFeedback.ParameterKey, out var value)
            && value is AuthGamepadSuccessFeedback ticket) _successFeedback = ticket;
        // Remove the ticket from the navigation journal; cached/back navigation
        // must not replay success or retain a native controller reference.
        if (IsLoaded && _gamepadNavigation is not null) ConsumeSuccessFeedback();
    }

    private void InitializeGamepad()
    {
        _windowActive = AuthGamepadNavigation.IsForeground(_hostWindow);
        _gamepadNavigation ??= new AuthGamepadNavigation(WelcomeRoot, WelcomeScroller,
            HandleGamepadAsync, () => [], _ => null, () => { }, welcome: true,
            caption: AuthCaptionWindow.Create(_hostWindow, _hostAppWindow));
        UpdateGamepadActivity();
        ConsumeSuccessFeedback();
    }

    private void ConsumeSuccessFeedback()
    {
        var ticket = _successFeedback;
        _successFeedback = null;
        try
        {
            if (ticket is not null && _hostWindow is not null)
                _gamepadNavigation?.ConsumeSuccessFeedback(ticket, _hostWindow);
        }
        finally
        {
            ticket?.Dispose();
        }
    }

    private void UpdateGamepadActivity() => _gamepadNavigation?.SetActive(
        IsLoaded && _pageCancellation is not null && _windowActive
        && _hostAppWindow is { IsVisible: true } appWindow
        && appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized });

    private async Task HandleGamepadAsync(AuthGamepadAction action)
    {
        if (!IsLoaded || _initializing || ViewModel.IsBusy || ViewModel.ErrorVisibility != Visibility.Visible) return;
        switch (action)
        {
            case AuthGamepadAction.Back:
            case AuthGamepadAction.PreviousSection:
                ViewModel.GoToLogin();
                break;
            case AuthGamepadAction.Submit:
                await EnterAsync();
                break;
            case AuthGamepadAction.FocusPrimary:
                WelcomeRetryButton.Focus(FocusState.Keyboard);
                break;
            case AuthGamepadAction.ResetFocus:
            case AuthGamepadAction.NextSection:
                WelcomeLoginButton.Focus(FocusState.Keyboard);
                break;
        }
    }

    internal McpAuthGamepadState GetGamepadState() => _gamepadNavigation?.GetState() ?? new();

    internal Task<bool> ApplyGamepadActionAsync(McpAuthGamepadAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLoaded || !ReferenceEquals(ViewModel.NavigationService.CurrentMainPage, this)
            || _initializing || ViewModel.IsBusy || _gamepadNavigation is null) return Task.FromResult(false);
        return _gamepadNavigation.ApplyAsync(action, cancellationToken);
    }

    private void DisposeGamepad()
    {
        _successFeedback?.Dispose();
        _successFeedback = null;
        _gamepadNavigation?.Dispose();
        _gamepadNavigation = null;
    }
}
