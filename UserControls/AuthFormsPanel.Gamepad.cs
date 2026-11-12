using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mizon.Windows.Services.Gamepad;
using System.Threading.Tasks;

namespace Mizon.Windows.UserControls;

public sealed partial class AuthFormsPanel
{
    internal Control[] GamepadFields => ViewModel.IsRegistrationMode
        ? [FirstNameInput, LastNameInput, UsernameInput, EmailInput, RegisterPasswordInput, ConfirmPasswordInput]
        : [LoginIdentifierInput, LoginPasswordInput];

    internal string? GetGamepadFieldId(Control? control) => control?.Name switch
    {
        nameof(LoginIdentifierInput) => "identifier",
        nameof(LoginPasswordInput) => "password",
        nameof(FirstNameInput) => "firstName",
        nameof(LastNameInput) => "lastName",
        nameof(UsernameInput) => "username",
        nameof(EmailInput) => "emailAddress",
        nameof(RegisterPasswordInput) => "password",
        nameof(ConfirmPasswordInput) => "confirmPassword",
        _ => null
    };

    internal void HideGamepadPasswords()
    {
        HideLoginPassword();
        if (RegisterPasswordInput is not null) RegisterPasswordInput.PasswordRevealMode = PasswordRevealMode.Hidden;
        if (ConfirmPasswordInput is not null) ConfirmPasswordInput.PasswordRevealMode = PasswordRevealMode.Hidden;
    }

    internal async Task HandleGamepadActionAsync(AuthGamepadAction action)
    {
        if (!_isLoaded || !ViewModel.ControlsIsEnabled) return;
        var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        switch (action)
        {
            case AuthGamepadAction.PreviousSection:
            case AuthGamepadAction.NextSection:
            case AuthGamepadAction.Back when ViewModel.IsRegistrationMode:
                HideGamepadPasswords();
                var desiredMode = action != AuthGamepadAction.Back && !ViewModel.IsRegistrationMode;
                var change = AuthFormsChangeMode(action);
                // FlipSide permits reversing an in-flight spring. Focus the current
                // face immediately so controller interaction never waits for its tail.
                FocusInitialGamepadField();
                await change;
                if (_isLoaded && XamlRoot is not null && ViewModel.IsRegistrationMode == desiredMode
                    && (FocusManager.GetFocusedElement(XamlRoot) is null || ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), focused)))
                    FocusInitialGamepadField();
                break;
            case AuthGamepadAction.Clear:
                if (focused is TextBox text) text.Text = string.Empty;
                else if (focused is PasswordBox password) password.Password = string.Empty;
                break;
            case AuthGamepadAction.RevealPassword when focused is PasswordBox password:
                if (ReferenceEquals(password, LoginPasswordInput)) RevealLoginPassword_Click(this, new RoutedEventArgs());
                else password.PasswordRevealMode = password.PasswordRevealMode == PasswordRevealMode.Visible
                    ? PasswordRevealMode.Hidden : PasswordRevealMode.Visible;
                break;
            case AuthGamepadAction.Submit:
                HideGamepadPasswords();
                RequestSubmit();
                break;
            case AuthGamepadAction.FocusPrimary:
                (ViewModel.IsRegistrationMode ? RegisterSubmitButton : LoginSubmitButton).Focus(FocusState.Keyboard);
                break;
            case AuthGamepadAction.ResetFocus:
                FocusInitialGamepadField();
                break;
        }
    }

    private Task AuthFormsChangeMode(AuthGamepadAction action) => SelectModeAsync(
        action != AuthGamepadAction.Back && !ViewModel.IsRegistrationMode);

    internal void FocusInitialGamepadField()
    {
        if (_isLoaded && ViewModel.ControlsIsEnabled) GamepadFields[0].Focus(FocusState.Keyboard);
    }
}
