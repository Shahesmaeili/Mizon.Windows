using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using System;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public sealed partial class LoginPageViewModel : MainViewModel
{
    private readonly LocalStorageService _localStorageService;
    private bool _isSubmitting;
    private readonly LocalizationService _localization = LocalizationService.Current;
    private string _errorKey = string.Empty;

    public string EmailAddressOrUsername
    {
        get => field;
        set
        {
            if (SetField(ref field, value ?? string.Empty))
                CredentialsChanged();
        }
    } = string.Empty;

    public string Password
    {
        get => field;
        set
        {
            if (SetField(ref field, value ?? string.Empty))
                CredentialsChanged();
        }
    } = string.Empty;

    public string RegistrationFirstName
    {
        get => field;
        set { if (SetField(ref field, value ?? string.Empty)) CredentialsChanged(); }
    } = string.Empty;

    public string RegistrationLastName
    {
        get => field;
        set { if (SetField(ref field, value ?? string.Empty)) CredentialsChanged(); }
    } = string.Empty;

    public string RegistrationUsername
    {
        get => field;
        set { if (SetField(ref field, value ?? string.Empty)) CredentialsChanged(); }
    } = string.Empty;

    public string RegistrationEmailAddress
    {
        get => field;
        set { if (SetField(ref field, value ?? string.Empty)) CredentialsChanged(); }
    } = string.Empty;

    public string RegistrationPassword
    {
        get => field;
        set { if (SetField(ref field, value ?? string.Empty)) CredentialsChanged(); }
    } = string.Empty;

    public string RegistrationConfirmPassword
    {
        get => field;
        set { if (SetField(ref field, value ?? string.Empty)) CredentialsChanged(); }
    } = string.Empty;

    public bool IsRegistrationMode { get; private set; }
    public bool IsLoginMode => !IsRegistrationMode;
    public string HeadingText => IsRegistrationMode ? _localization.GetString("AuthRegisterHeading") : _localization.GetString("AuthLoginHeading");
    public string DescriptionText => IsRegistrationMode
        ? _localization.GetString("AuthRegisterDescription")
        : _localization.GetString("AuthLoginDescription");
    public string ModePromptText => IsRegistrationMode ? _localization.GetString("AuthHasAccountPrompt") : _localization.GetString("AuthNoAccountPrompt");
    public string ModeActionText => IsRegistrationMode ? _localization.GetString("AuthSignInAccount") : _localization.GetString("AuthCreateAccount");
    public string FooterText => IsRegistrationMode ? _localization.GetString("AuthRegisterFooter") : _localization.GetString("AuthLoginFooter");

    public bool LoginButtonIsEnabled => !_isSubmitting && (IsRegistrationMode
        ? !string.IsNullOrWhiteSpace(RegistrationFirstName)
            && !string.IsNullOrWhiteSpace(RegistrationLastName)
            && !string.IsNullOrWhiteSpace(RegistrationUsername)
            && !string.IsNullOrWhiteSpace(RegistrationEmailAddress)
            && !string.IsNullOrEmpty(RegistrationPassword)
            && !string.IsNullOrEmpty(RegistrationConfirmPassword)
        : !string.IsNullOrWhiteSpace(EmailAddressOrUsername) && !string.IsNullOrEmpty(Password));
    public bool ControlsIsEnabled => !_isSubmitting;
    public bool ShowProgressBar => _isSubmitting;
    public string SubmitText => IsRegistrationMode
        ? (_isSubmitting ? _localization.GetString("AuthRegisterProgress") : _localization.GetString("AuthRegisterSubmit"))
        : (_isSubmitting ? _localization.GetString("AuthLoginProgress") : _localization.GetString("AuthLoginSubmit"));
    public bool HasError => !string.IsNullOrEmpty(_errorKey);
    internal string ErrorCode => _errorKey;
    // Preserve the error's identity so a language change can translate an error already on screen.
    public string ErrorMessage => HasError ? _localization.GetString(_errorKey) : string.Empty;

    private void SetError(string key)
    {
        if (_errorKey == key) return;
        _errorKey = key;
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(ModePromptText));
        OnPropertyChanged(nameof(ModeActionText));
        OnPropertyChanged(nameof(FooterText));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    public RelayCommand LoginCommand { get; }

    public LoginPageViewModel(MizonApi mizonApi, Lazy<MizonSocket> mizonSocket,
        NavigationService navigationService, LocalStorageService localStorageService,
        IAudioPlayerService audioPlayerService)
        : base(mizonApi, mizonSocket, navigationService, audioPlayerService)
    {
        _localStorageService = localStorageService;
        // The page uses SubmitAsync with its own lifetime token; this preserves existing command consumers.
        LoginCommand = new RelayCommand(LoginAsync, (_, _) => LoginButtonIsEnabled);
    }

    private void CredentialsChanged()
    {
        SetError(string.Empty);
        OnPropertyChanged(nameof(LoginButtonIsEnabled));
        LoginCommand?.RaiseCanExecuteChanged();
    }

    public void SetRegistrationMode(bool value)
    {
        if (_isSubmitting || IsRegistrationMode == value) return;

        IsRegistrationMode = value;
        ClearSensitiveInputs();
        SetError(string.Empty);
        OnPropertyChanged(nameof(IsRegistrationMode));
        OnPropertyChanged(nameof(IsLoginMode));
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(ModePromptText));
        OnPropertyChanged(nameof(ModeActionText));
        OnPropertyChanged(nameof(FooterText));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(LoginButtonIsEnabled));
        LoginCommand.RaiseCanExecuteChanged();
    }

    public void ClearSensitiveInputs()
    {
        Password = string.Empty;
        RegistrationPassword = string.Empty;
        RegistrationConfirmPassword = string.Empty;
    }

    private void SetSubmitting(bool value)
    {
        _isSubmitting = value;
        OnPropertyChanged(nameof(LoginButtonIsEnabled));
        OnPropertyChanged(nameof(ControlsIsEnabled));
        OnPropertyChanged(nameof(ShowProgressBar));
        OnPropertyChanged(nameof(SubmitText));
        LoginCommand.RaiseCanExecuteChanged();
    }

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._]{3,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    private Task LoginAsync(object? parameter, CancellationToken cancellationToken) => SubmitAsync(cancellationToken);

    public async Task SubmitAsync(CancellationToken cancellationToken)
    {
        if (!LoginButtonIsEnabled || cancellationToken.IsCancellationRequested) return;

        SetError(string.Empty);
        if (IsRegistrationMode && !ValidateRegistration()) return;

        var isRegistration = IsRegistrationMode;
        SetSubmitting(true);

        try
        {
            string? accessToken;
            int? errorCode;
            bool isSuccess;
            if (isRegistration)
            {
                var response = await MizonApi.SendRequestAsync(new RegisterOperation(new()
                {
                    FirstName = RegistrationFirstName.Trim(),
                    LastName = RegistrationLastName.Trim(),
                    Username = RegistrationUsername.Trim(),
                    EmailAddress = RegistrationEmailAddress.Trim(),
                    Password = RegistrationPassword
                }, updateToken: false), cancellationToken);
                accessToken = response?.Content?.AccessToken;
                errorCode = response?.Error?.Code;
                isSuccess = response?.IsSuccess == true;
            }
            else
            {
                var identifier = EmailAddressOrUsername.Trim();
                var isEmail = EmailAddressPattern().IsMatch(identifier);
                var response = await MizonApi.SendRequestAsync(new LoginOperation(new()
                {
                    Username = isEmail ? null : identifier,
                    EmailAddress = isEmail ? identifier : null,
                    Password = Password
                }, updateToken: false), cancellationToken);
                accessToken = response?.Content?.AccessToken;
                errorCode = response?.Error?.Code;
                isSuccess = response?.IsSuccess == true;
            }

            // A completed response from an abandoned page must not sign in or change the shared API token.
            cancellationToken.ThrowIfCancellationRequested();
            if (!isSuccess || string.IsNullOrWhiteSpace(accessToken))
            {
                SetError(GetErrorKey(errorCode, isRegistration));
                return;
            }

            _localStorageService.SetJwtToken(accessToken);
            MizonApi.SetToken(accessToken);
            ClearSensitiveInputs();
            NavigationService.MainNavigate(typeof(WelcomePage));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leaving the page is not an authentication error.
        }
        catch (Exception exception)
        {
            // Authentication diagnostics intentionally omit payloads and exception messages.
            AppLog.Information($"Authentication submission failed ({exception.GetType().Name}).");
            SetError(GetErrorKey(null, isRegistration));
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested) ClearSensitiveInputs();
            SetSubmitting(false);
        }
    }

    private bool ValidateRegistration()
    {
        var firstName = RegistrationFirstName.Trim();
        var lastName = RegistrationLastName.Trim();
        string username;
        string email;
        try
        {
            username = RegistrationUsername.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            email = RegistrationEmailAddress.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            SetError("AuthInvalidIdentifierError");
            return false;
        }

        if (firstName.Length is < 1 or > 60 || lastName.Length is < 1 or > 60
            || ContainsControlCharacter(firstName) || ContainsControlCharacter(lastName))
            SetError("AuthInvalidNameError");
        else if (!UsernamePattern().IsMatch(username))
            SetError("AuthInvalidUsernameError");
        else if (email.Length > 128 || !IsAsciiEmail(email) || !EmailAddressPattern().IsMatch(email) || !MailAddress.TryCreate(email, out var address)
            || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            SetError("AuthInvalidEmailError");
        else if (RegistrationPassword.Length is < 8 or > 128)
            SetError("AuthInvalidPasswordError");
        else if (!string.Equals(RegistrationPassword, RegistrationConfirmPassword, StringComparison.Ordinal))
            SetError("AuthPasswordMismatchError");

        return !HasError;
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var character in value)
            if (char.IsControl(character)) return true;
        return false;
    }

    private static bool IsAsciiEmail(string value)
    {
        foreach (var character in value)
            if (character > 127 || char.IsWhiteSpace(character) || char.IsControl(character)) return false;
        return true;
    }

    private static string GetErrorKey(int? code, bool isRegistration) => code switch
    {
        21000 => "AuthInvalidRegistrationError",
        21001 => "AuthUsernameTakenError",
        21002 => "AuthEmailTakenError",
        21003 => "AuthRegistrationUnavailableError",
        401 or 1001 => "AuthInvalidCredentialsError",
        1000 => "AuthUpdateRequiredError",
        101 or 102 or 104 or 105 or 106 or 107 =>
            "AuthConnectionError",
        429 => "AuthTooManyAttemptsError",
        _ => isRegistration ? "AuthRegistrationFailedError" : "AuthLoginFailedError"
    };
}
