using Microsoft.UI.Xaml;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Pages;
using Mizon.Windows.ViewModels;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Invoked only after the app's per-connection approval gate, on the UI dispatcher.
// Reuse the live page; resolving the transient auth VMs from DI would create a
// disconnected form and bypass the page's cancellation/navigation lifecycle.
internal sealed class McpAuthenticationService : IDisposable
{
    private readonly NavigationService _navigation;
    private readonly LocalStorageService _localStorage;
    private bool _hasSession;
    private bool _disposed;

    internal McpAuthenticationService(NavigationService navigation, LocalStorageService localStorage)
    {
        _navigation = navigation;
        _localStorage = localStorage;
        _hasSession = !string.IsNullOrWhiteSpace(localStorage.GetJwtToken());
        _localStorage.AuthenticationChanged += AuthenticationChanged;
    }

    private void AuthenticationChanged(object? sender, bool hasSession) => _hasSession = hasSession;

    internal async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return new() { Code = "unavailable", Message = "The app connection has closed." };
        if (App.MainMizonWindow?.DispatcherQueue.HasThreadAccess != true)
            throw new InvalidOperationException("MCP authentication operations require the app dispatcher.");

        try
        {
            return request.Operation switch
            {
                "get_state" => Success("state", "Current authentication-page state."),
                "open_page" => await OpenPageAsync(request.Page, cancellationToken),
                "set_form" => SetForm(request.Page, request.Form),
                "submit" => await SubmitAsync(request.Page, cancellationToken),
                "clear_form" => ClearForm(request.Page),
                "retry_welcome" => await RetryWelcomeAsync(cancellationToken),
                "set_language" => SetLanguage(request.Language),
                _ => Failure("unknown_operation", "This operation is not available.")
            };
        }
        catch (TimeoutException)
        {
            return Failure("page_not_ready", "The requested page did not become ready in time.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure("page_changed", "The page changed before the operation completed.");
        }
    }

    private async Task<McpResponse> OpenPageAsync(string? destination, CancellationToken cancellationToken)
    {
        if (destination is not ("login" or "register" or "welcome"))
            return Failure("invalid_page", "Choose login, register or welcome.");
        if (_navigation.CurrentMainPage is LoginPage { ViewModel.ControlsIsEnabled: false })
            return Failure("busy", "An authentication request is already in progress.");
        if (_navigation.CurrentMainPage is HomePage { ViewModel.IsSigningOut: true })
            return Failure("busy", "Sign-out is still in progress. No navigation was queued.");

        if (destination == "welcome")
        {
            if (!_hasSession)
                return Failure("session_required", "Welcome validates an existing session. Sign in first.");
            App.PendingDestination = null;
            if (_navigation.CurrentMainPage is not WelcomePage)
                _navigation.MainNavigate(typeof(WelcomePage));
            if (_navigation.CurrentMainPage is WelcomePage welcome)
                await WaitForLoadedAsync(welcome, cancellationToken);
            return Success("opened", "Welcome uses the normal session check and may continue to Home or Login.");
        }

        // Opening a form does not revoke or replace an existing account session.
        // Clear a stale queued form route before selecting the requested mode.
        App.PendingDestination = null;
        if (_navigation.CurrentMainPage is not LoginPage)
            _navigation.MainNavigate(typeof(LoginPage));
        if (_navigation.CurrentMainPage is not LoginPage login
            || !await login.SelectMcpModeAsync(destination == "register", cancellationToken))
            return Failure("page_changed", "The requested authentication form is no longer active.");
        return Success("opened", "The authentication form is ready.");
    }

    private McpResponse SetForm(string? page, McpAuthForm? form)
    {
        if (!TryGetForm(page, out var login, out var failure)) return failure!;
        if (form is null || !HasFields(form))
            return Failure("invalid_form", "Provide at least one form field; omitted fields are unchanged.");
        if (!ValidLengths(form))
            return Failure("invalid_field_length", "A form field exceeds its supported length.");
        if (page == "login" && (form.FirstName is not null || form.LastName is not null
            || form.Username is not null || form.Email is not null || form.ConfirmPassword is not null)
            || page == "register" && form.Identifier is not null)
            return Failure("invalid_form_fields", "Some supplied fields do not belong to the selected form.");

        var model = login!.ViewModel;
        // Validate the whole request before making any partial change to the form.
        if (page == "login")
        {
            if (form.Identifier is not null) model.EmailAddressOrUsername = form.Identifier;
            if (form.Password is not null) model.Password = form.Password;
        }
        else
        {
            if (form.FirstName is not null) model.RegistrationFirstName = form.FirstName;
            if (form.LastName is not null) model.RegistrationLastName = form.LastName;
            if (form.Username is not null) model.RegistrationUsername = form.Username;
            if (form.Email is not null) model.RegistrationEmailAddress = form.Email;
            if (form.Password is not null) model.RegistrationPassword = form.Password;
            if (form.ConfirmPassword is not null) model.RegistrationConfirmPassword = form.ConfirmPassword;
        }
        return Success("form_updated", "The form was updated without submitting it.");
    }

    private async Task<McpResponse> SubmitAsync(string? page, CancellationToken cancellationToken)
    {
        if (!TryGetForm(page, out var login, out var failure)) return failure!;
        if (_hasSession)
            return Failure("already_signed_in", "An account session already exists. MCP will not replace it.");
        var model = login!.ViewModel;
        if (!model.LoginButtonIsEnabled)
            return Failure("missing_fields", "Complete the required form fields before submitting.");

        await login.SubmitMcpFormAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_hasSession)
            return Success("authenticated", "Authentication succeeded; the normal Welcome flow continues.");
        if (model.HasError)
            return Failure(model.ErrorCode, model.ErrorMessage);
        if (!ReferenceEquals(_navigation.CurrentMainPage, login))
            return Failure("page_changed", "The authentication page changed before submission completed.");
        return Failure("not_submitted", "The form was not submitted.");
    }

    private McpResponse ClearForm(string? page)
    {
        if (!TryGetForm(page, out var login, out var failure)) return failure!;
        var model = login!.ViewModel;
        if (page == "login") model.EmailAddressOrUsername = string.Empty;
        else
        {
            model.RegistrationFirstName = string.Empty;
            model.RegistrationLastName = string.Empty;
            model.RegistrationUsername = string.Empty;
            model.RegistrationEmailAddress = string.Empty;
        }
        model.ClearSensitiveInputs();
        return Success("form_cleared", "The form and sensitive inputs were cleared.");
    }

    private async Task<McpResponse> RetryWelcomeAsync(CancellationToken cancellationToken)
    {
        if (!_hasSession) return Failure("session_required", "No account session is available to validate.");
        if (_navigation.CurrentMainPage is not WelcomePage welcome || !welcome.IsLoaded)
            return Failure("page_mismatch", "Welcome must be the current page.");
        if (welcome.IsMcpInitializing) return Failure("busy", "Welcome is already checking the account.");
        if (!await welcome.RetryMcpWelcomeAsync(cancellationToken))
            return Failure("page_changed", "Welcome is no longer available for retry.");
        cancellationToken.ThrowIfCancellationRequested();
        if (welcome.ViewModel.StageCode == "connection_error")
            return Failure("connection_error", welcome.ViewModel.StatusDetail);
        if (!_hasSession) return Failure("session_rejected", "The existing session was rejected; Login is now active.");
        return Success("welcome_checked", "The normal Welcome check completed.");
    }

    private McpResponse SetLanguage(string? language)
    {
        if (_navigation.CurrentMainPage is not LoginPage and not WelcomePage)
            return Failure("page_mismatch", "Open Login, Register or Welcome before changing its language.");
        var normalized = string.Equals(language, "fa-IR", StringComparison.OrdinalIgnoreCase) ? "fa-IR"
            : string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : null;
        if (normalized is null) return Failure("invalid_language", "Supported languages are fa-IR and en-US.");
        LocalizationService.Current.SetLanguage(normalized);
        return Success("language_updated", "The app display language was updated.");
    }

    private bool TryGetForm(string? page, out LoginPage? login, out McpResponse? failure)
    {
        login = _navigation.CurrentMainPage as LoginPage;
        failure = page is not ("login" or "register")
            ? Failure("invalid_page", "Choose login or register for a form operation.")
            : login is null || !login.IsLoaded || login.ViewModel.IsRegistrationMode != (page == "register")
                ? Failure("page_mismatch", "The requested authentication form must be the current page.")
                : !login.ViewModel.ControlsIsEnabled
                    ? Failure("busy", "An authentication request is already in progress.") : null;
        return failure is null;
    }

    private static bool HasFields(McpAuthForm form) => form.Identifier is not null || form.Password is not null
        || form.FirstName is not null || form.LastName is not null || form.Username is not null
        || form.Email is not null || form.ConfirmPassword is not null;

    private static bool ValidLengths(McpAuthForm form) => form.Identifier?.Length is not > 128
        && form.Password?.Length is not > 128 && form.FirstName?.Length is not > 60
        && form.LastName?.Length is not > 60 && form.Username?.Length is not > 32
        && form.Email?.Length is not > 128 && form.ConfirmPassword?.Length is not > 128;

    private McpResponse Success(string code, string message) => new()
    {
        Success = true, Code = code, Message = message, State = ReadState()
    };

    private McpResponse Failure(string code, string message) => new()
    {
        Code = code, Message = message, State = ReadState()
    };

    private McpAuthState ReadState()
    {
        var state = new McpAuthState { Language = LocalizationService.Current.LanguageTag, HasSession = _hasSession };
        if (_navigation.CurrentMainPage is LoginPage login)
        {
            var model = login.ViewModel;
            return state with
            {
                Page = model.IsRegistrationMode ? "register" : "login",
                IsBusy = model.ShowProgressBar,
                CanSubmit = login.IsLoaded && !_hasSession && model.LoginButtonIsEnabled,
                ControlsEnabled = login.IsLoaded && model.ControlsIsEnabled,
                HasError = model.HasError,
                ErrorCode = model.HasError ? model.ErrorCode : null,
                ErrorMessage = model.HasError ? model.ErrorMessage : null,
                Identifier = model.IsLoginMode ? model.EmailAddressOrUsername : null,
                FirstName = model.IsRegistrationMode ? model.RegistrationFirstName : null,
                LastName = model.IsRegistrationMode ? model.RegistrationLastName : null,
                Username = model.IsRegistrationMode ? model.RegistrationUsername : null,
                Email = model.IsRegistrationMode ? model.RegistrationEmailAddress : null,
                HasPassword = !string.IsNullOrEmpty(model.IsRegistrationMode ? model.RegistrationPassword : model.Password),
                HasConfirmPassword = model.IsRegistrationMode && !string.IsNullOrEmpty(model.RegistrationConfirmPassword)
            };
        }
        if (_navigation.CurrentMainPage is WelcomePage welcome)
        {
            var model = welcome.ViewModel;
            return state with
            {
                Page = "welcome",
                IsBusy = welcome.IsMcpInitializing,
                ControlsEnabled = welcome.IsLoaded && !welcome.IsMcpInitializing,
                HasError = model.StageCode == "connection_error",
                ErrorCode = model.StageCode == "connection_error" ? "connection_error" : null,
                ErrorMessage = model.StageCode == "connection_error" ? model.StatusDetail : null,
                WelcomeStage = model.StageCode,
                StatusTitle = model.StatusTitle,
                StatusDetail = model.StatusDetail,
                DisplayName = model.DisplayName,
                HasProfileImage = model.ProfileImage is not null
            };
        }
        return state with
        {
            Page = _navigation.CurrentMainPage switch { HomePage => "home", null => "unavailable", _ => "other" }
        };
    }

    internal static async Task<bool> WaitForLoadedAsync(FrameworkElement page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (page.IsLoaded) return true;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded(object sender, RoutedEventArgs args) => completion.TrySetResult(true);
        void Unloaded(object sender, RoutedEventArgs args) => completion.TrySetResult(false);
        page.Loaded += Loaded;
        page.Unloaded += Unloaded;
        try
        {
            if (page.IsLoaded) return true;
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            page.Loaded -= Loaded;
            page.Unloaded -= Unloaded;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localStorage.AuthenticationChanged -= AuthenticationChanged;
    }
}
