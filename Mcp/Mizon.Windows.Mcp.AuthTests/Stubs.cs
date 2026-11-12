using Mizon.Windows.Mcp.Contracts;

// Synthetic UI/session fixtures. Production authentication adapter is source-linked.
// No WinUI, real controller, live consent, account or backend is accessed.
namespace Microsoft.UI.Xaml
{
    public sealed class RoutedEventArgs : EventArgs;
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public class FrameworkElement
    {
        public bool IsLoaded { get; set; } = true;
        public event RoutedEventHandler? Loaded;
        public event RoutedEventHandler? Unloaded;
        public int Subscribers => (Loaded?.GetInvocationList().Length ?? 0) + (Unloaded?.GetInvocationList().Length ?? 0);
        public void Load() { IsLoaded = true; Loaded?.Invoke(this, new()); }
        public void Unload() { IsLoaded = false; Unloaded?.Invoke(this, new()); }
    }
}

namespace Mizon.Windows
{
    internal static class App
    {
        internal static SyntheticWindow? MainMizonWindow { get; set; } = new();
        internal static object? PendingDestination { get; set; }
    }
    internal sealed class SyntheticWindow { internal SyntheticDispatcher DispatcherQueue { get; } = new(); }
    internal sealed class SyntheticDispatcher { internal bool HasThreadAccess { get; set; } = true; }
}

namespace Mizon.Windows.ViewModels
{
    internal sealed class LoginPageViewModel
    {
        public bool ControlsIsEnabled { get; set; } = true;
        public bool IsRegistrationMode { get; set; }
        public bool IsLoginMode => !IsRegistrationMode;
        public bool LoginButtonIsEnabled { get; set; }
        public bool ShowProgressBar { get; set; }
        public bool HasError { get; set; }
        public string ErrorCode { get; set; } = "validation_error";
        public string ErrorMessage { get; set; } = "Synthetic validation error.";
        public string EmailAddressOrUsername { get; set; } = "synthetic-login";
        public string Password { get; set; } = "synthetic-secret-never-returned";
        public string RegistrationFirstName { get; set; } = "Synthetic";
        public string RegistrationLastName { get; set; } = "Fixture";
        public string RegistrationUsername { get; set; } = "synthetic-fixture";
        public string RegistrationEmailAddress { get; set; } = "synthetic@example.invalid";
        public string RegistrationPassword { get; set; } = "synthetic-secret-never-returned";
        public string RegistrationConfirmPassword { get; set; } = "synthetic-secret-never-returned";
        public void ClearSensitiveInputs() => Password = RegistrationPassword = RegistrationConfirmPassword = string.Empty;
    }
    internal sealed class WelcomePageViewModel
    {
        public string StageCode { get; set; } = "ready";
        public string StatusTitle { get; set; } = "Synthetic welcome";
        public string StatusDetail { get; set; } = "Synthetic state";
        public string DisplayName { get; set; } = "Fixture";
        public object? ProfileImage { get; set; }
    }
    internal sealed class HomePageViewModel { public bool IsSigningOut { get; set; } }
}

namespace Mizon.Windows.Pages
{
    internal class GamepadPage : Microsoft.UI.Xaml.FrameworkElement
    {
        internal McpAuthGamepadState Gamepad { get; set; } = new() { IsControllerConnected = true, IsNavigationActive = true, FocusedFieldId = "login-identifier" };
        internal int ActionCalls { get; private set; }
        internal int StateReads { get; private set; }
        internal McpAuthGamepadAction? LastAction { get; private set; }
        internal Func<McpAuthGamepadAction, CancellationToken, Task<bool>>? Action { get; set; }
        internal McpAuthGamepadState GetGamepadState() { StateReads++; return Gamepad; }
        internal Task<bool> ApplyGamepadActionAsync(McpAuthGamepadAction action, CancellationToken token)
        {
            ActionCalls++;
            LastAction = action;
            return Action?.Invoke(action, token) ?? Task.FromResult(true);
        }
    }
    internal sealed class LoginPage : GamepadPage
    {
        internal ViewModels.LoginPageViewModel ViewModel { get; } = new();
        internal int Submissions { get; private set; }
        internal Task<bool> SelectMcpModeAsync(bool register, CancellationToken token) { ViewModel.IsRegistrationMode = register; return Task.FromResult(true); }
        internal Task SubmitMcpFormAsync(CancellationToken token) { Submissions++; return Task.CompletedTask; }
    }
    internal sealed class WelcomePage : GamepadPage
    {
        internal ViewModels.WelcomePageViewModel ViewModel { get; } = new();
        internal bool IsMcpInitializing { get; set; }
        internal int Retries { get; private set; }
        internal Task<bool> RetryMcpWelcomeAsync(CancellationToken token) { Retries++; return Task.FromResult(true); }
    }
    internal sealed class HomePage : Microsoft.UI.Xaml.FrameworkElement { internal ViewModels.HomePageViewModel ViewModel { get; } = new(); }
}

namespace Mizon.Windows.Services
{
    internal sealed class NavigationService
    {
        internal Microsoft.UI.Xaml.FrameworkElement? CurrentMainPage { get; set; }
        internal void MainNavigate(Type page) => throw new InvalidOperationException("No real navigation is authorized by this fixture.");
    }
    internal sealed class LocalStorageService
    {
        internal event EventHandler<bool>? AuthenticationChanged;
        internal int Subscribers => AuthenticationChanged?.GetInvocationList().Length ?? 0;
        internal string? GetJwtToken() => null;
        internal void ChangeSession(bool present) => AuthenticationChanged?.Invoke(this, present);
    }
    internal sealed class LocalizationService
    {
        internal static LocalizationService Current { get; } = new();
        internal string LanguageTag { get; private set; } = "en-US";
        internal void SetLanguage(string language) => LanguageTag = language;
    }
}
