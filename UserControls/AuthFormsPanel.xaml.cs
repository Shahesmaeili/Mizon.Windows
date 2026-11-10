using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.ComponentModel;
using System.Threading;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class AuthFormsPanel : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;

    private readonly UISettings _uiSettings = new();
    private CancellationTokenSource? _transitionCancellation;
    private bool _isLoaded;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(LoginPageViewModel), typeof(AuthFormsPanel), new PropertyMetadata(null));
    public static readonly DependencyProperty CardPaddingProperty = DependencyProperty.Register(
        nameof(CardPadding), typeof(Thickness), typeof(AuthFormsPanel), new PropertyMetadata(new Thickness(22, 24, 22, 24)));

    public LoginPageViewModel ViewModel
    {
        get => (LoginPageViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
    public Thickness CardPadding
    {
        get => (Thickness)GetValue(CardPaddingProperty);
        set => SetValue(CardPaddingProperty, value);
    }

    public event EventHandler? SubmitRequested;
    public event EventHandler? ModeChanged;
    public event EventHandler<bool>? CredentialFocusChanged;

    public AuthFormsPanel()
    {
        InitializeComponent();
        Loaded += Panel_Loaded;
        Unloaded += Panel_Unloaded;
    }

    public Visibility VisibilityFor(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Thickness PasswordInputPadding(bool isPersian) => isPersian
        ? new Thickness(48, 10, 12, 10)
        : new Thickness(12, 10, 48, 10);

    private void Panel_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        Bindings.Update();
        Localization.PropertyChanged -= Localization_PropertyChanged;
        Localization.PropertyChanged += Localization_PropertyChanged;
        UpdateLoginPasswordAccessibility();
        if (ViewModel?.IsRegistrationMode == true) EnsureRegistrationForm();
        UpdateNativePasswordAccessibility(RegisterPasswordInput);
        UpdateNativePasswordAccessibility(ConfirmPasswordInput);
        FormFlip.IsFlipped = ViewModel?.IsRegistrationMode == true;
        FormFlip.CancelAnimation();
        if (!_uiSettings.AnimationsEnabled)
        {
            LoginFocusRing.OpacityTransition = null;
            if (RegisterFocusRing is not null) RegisterFocusRing.OpacityTransition = null;
        }
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        Localization.PropertyChanged -= Localization_PropertyChanged;
        Bindings.StopTracking();
        CancelTransitions();
        HideLoginPassword();
    }

    public void FocusInitialField()
    {
        if (!_isLoaded || ViewModel is null || !ViewModel.ControlsIsEnabled) return;
        if (ViewModel.IsRegistrationMode) FirstNameInput.Focus(FocusState.Programmatic);
        else LoginIdentifierInput.Focus(FocusState.Programmatic);
    }

    public void CancelTransitions()
    {
        _transitionCancellation?.Cancel();
        FormFlip.CancelAnimation();
        FormInteractionHost.IsHitTestVisible = true;
        SetCredentialFocus(false);
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e) => RequestSubmit();

    private void RequestSubmit()
    {
        if (ViewModel?.LoginButtonIsEnabled == true)
            SubmitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Form_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !IsCredentialField(e.OriginalSource as DependencyObject)) return;
        e.Handled = true;
        RequestSubmit();
    }

    private async void ModeButton_Click(object sender, RoutedEventArgs e) =>
        await SelectModeAsync(!ViewModel.IsRegistrationMode);

    public async System.Threading.Tasks.Task SelectModeAsync(bool registration)
    {
        if (!_isLoaded || ViewModel?.ControlsIsEnabled != true || ViewModel.IsRegistrationMode == registration) return;
        if (registration) EnsureRegistrationForm();
        SetCredentialFocus(false);
        var cancellation = new CancellationTokenSource();
        _transitionCancellation = cancellation;
        try
        {
            ViewModel.SetRegistrationMode(registration);
            HideLoginPassword();
            var flip = FormFlip.FlipAsync(ViewModel.IsRegistrationMode, cancellation.Token);
            ModeChanged?.Invoke(this, EventArgs.Empty);
            await flip;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Information($"Authentication flip unavailable ({exception.GetType().Name}).");
            if (ReferenceEquals(_transitionCancellation, cancellation)) FormFlip.CancelAnimation();
        }
        finally
        {
            if (ReferenceEquals(_transitionCancellation, cancellation)) _transitionCancellation = null;
            cancellation.Dispose();
            FormInteractionHost.IsHitTestVisible = true;
            // Don't scroll or recolor the card by auto-focusing a textbox at the end of the spring.
        }
    }

    private void EnsureRegistrationForm()
    {
        // Realize the whole group once, before the first spring. Keep it thereafter so
        // reversing the flip never reconstructs the controls or interrupts typing.
        if (RegisterFace is null) FindName(nameof(RegisterFace));
        if (!_uiSettings.AnimationsEnabled && RegisterFocusRing is not null)
            RegisterFocusRing.OpacityTransition = null;
    }

    private void RevealLoginPassword_Click(object sender, RoutedEventArgs e)
    {
        var reveal = LoginPasswordInput.PasswordRevealMode != PasswordRevealMode.Visible;
        LoginPasswordInput.PasswordRevealMode = reveal ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        LoginPasswordRevealIcon.Symbol = reveal ? FluentIcons.Common.Symbol.EyeOff : FluentIcons.Common.Symbol.Eye;
        UpdateLoginPasswordAccessibility();
    }

    private void HideLoginPassword()
    {
        LoginPasswordInput.PasswordRevealMode = PasswordRevealMode.Hidden;
        LoginPasswordRevealIcon.Symbol = FluentIcons.Common.Symbol.Eye;
        UpdateLoginPasswordAccessibility();
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_isLoaded || args.PropertyName != nameof(LocalizationService.Revision)) return;
        // Translating the button must preserve the reveal state and every entered credential.
        UpdateLoginPasswordAccessibility();
        UpdateNativePasswordAccessibility(RegisterPasswordInput);
        UpdateNativePasswordAccessibility(ConfirmPasswordInput);
    }

    private void UpdateLoginPasswordAccessibility()
    {
        var reveal = LoginPasswordInput.PasswordRevealMode == PasswordRevealMode.Visible;
        var name = Localization.GetString(reveal ? "AuthHidePassword" : "AuthShowPassword");
        AutomationProperties.SetName(LoginPasswordRevealButton, name);
        AutomationProperties.SetItemStatus(LoginPasswordRevealButton,
            Localization.GetString(reveal ? "AuthPasswordVisible" : "AuthPasswordHidden"));
        ToolTipService.SetToolTip(LoginPasswordRevealButton, name);
    }

    private void NativePassword_Loaded(object sender, RoutedEventArgs e)
    {
        var passwordBox = (PasswordBox)sender;
        passwordBox.ApplyTemplate();
        if (FindTemplateButton(passwordBox, "RevealButton") is ToggleButton button)
        {
            button.Checked -= NativePasswordReveal_Changed;
            button.Unchecked -= NativePasswordReveal_Changed;
            button.Checked += NativePasswordReveal_Changed;
            button.Unchecked += NativePasswordReveal_Changed;
        }
        UpdateNativePasswordAccessibility(passwordBox);
    }

    private void NativePassword_Unloaded(object sender, RoutedEventArgs e)
    {
        if (FindTemplateButton((PasswordBox)sender, "RevealButton") is not ToggleButton button) return;
        button.Checked -= NativePasswordReveal_Changed;
        button.Unchecked -= NativePasswordReveal_Changed;
    }

    private void NativePasswordReveal_Changed(object sender, RoutedEventArgs e) =>
        UpdateNativeRevealButtonAccessibility((ButtonBase)sender);

    private void UpdateNativePasswordAccessibility(PasswordBox? passwordBox)
    {
        if (passwordBox is null) return;
        passwordBox.ApplyTemplate();
        if (FindTemplateButton(passwordBox, "RevealButton") is not { } button) return;
        UpdateNativeRevealButtonAccessibility(button);
    }

    private void UpdateNativeRevealButtonAccessibility(ButtonBase button)
    {
        var reveal = button is ToggleButton { IsChecked: true };
        var name = Localization.GetString(reveal ? "AuthHidePassword" : "AuthShowPassword");
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetHelpText(button, Localization.GetString("AuthPasswordPeekHelp"));
        AutomationProperties.SetItemStatus(button,
            Localization.GetString(reveal ? "AuthPasswordVisible" : "AuthPasswordHidden"));
        ToolTipService.SetToolTip(button, name);
    }

    private static ButtonBase? FindTemplateButton(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ButtonBase button && button.Name == name) return button;
            if (FindTemplateButton(child, name) is { } nested) return nested;
        }
        return null;
    }

    private void SetCredentialFocus(bool hasFocus)
    {
        LoginFocusRing.Opacity = hasFocus ? 1 : 0;
        if (RegisterFocusRing is not null) RegisterFocusRing.Opacity = hasFocus ? 1 : 0;
        CredentialFocusChanged?.Invoke(this, hasFocus);
    }

    private void Form_GotFocus(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is not null)
            SetCredentialFocus(IsCredentialField(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject));
    }

    private void Form_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isLoaded && XamlRoot is not null)
                SetCredentialFocus(IsCredentialField(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject));
        });
    }

    private bool IsCredentialField(DependencyObject? element)
    {
        var isInput = false;
        while (element is not null && !ReferenceEquals(element, FormInteractionHost))
        {
            isInput |= element is TextBox or PasswordBox;
            element = VisualTreeHelper.GetParent(element);
        }
        return element is not null && isInput;
    }
}
