using Microsoft.UI;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.Windows.Common;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public class LoginPageViewModel : MainViewModel
{
    private readonly LocalStorageService _localStorageService;
    private readonly TeachingTipService _teachingTipService;


    public string EmailAddressOrUsername
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
                UpdateLoginButtonIsEnabled();
        }
    } = string.Empty;

    public string Password
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
                UpdateLoginButtonIsEnabled();
        }
    } = string.Empty;

    public bool LoginButtonIsEnabled
    {
        get => field;
        set => SetField(ref field, value);
    } = false;


    public bool ControlsIsEnabled
    {
        get => field;
        set => SetField(ref field, value);
    } = true;

    public bool ShowProgressBar
    {
        get => field;
        set => SetField(ref field, value);
    } = false;

    private void UpdateLoginButtonIsEnabled()
    {
        LoginButtonIsEnabled = !string.IsNullOrEmpty(EmailAddressOrUsername) && !string.IsNullOrEmpty(Password);
        _ = WindowNativeInterop.AnimateWindowBorderColor(App.MainMizonWindow, Colors.Blue);
    }

    public RelayCommand LoginCommand { get; }

    public LoginPageViewModel(MizonApi mizonApi,
        MizonSocket mizonSocket,
        NavigationService navigationService,
        LocalStorageService localStorageService,
        TeachingTipService teachingTipService,
        IAudioPlayerService audioPlayerService)
        : base(mizonApi, mizonSocket, navigationService, audioPlayerService)
    {
        LoginCommand = new RelayCommand(LoginAsync);
        _localStorageService = localStorageService;
        _teachingTipService = teachingTipService;
    }

    private bool IsEmail(string value)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            value,
            @"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant
        );
    }


    private async Task LoginAsync(object? parameter, CancellationToken cancellationToken)
    {
        ControlsIsEnabled = LoginButtonIsEnabled = false;
        ShowProgressBar = true;
        await WindowNativeInterop.AnimateWindowBorderColor(App.MainMizonWindow, Colors.Blue);

        string? username = null;
        string? emailAddress = null;

        var emailAddressOrUsernameTrimed = EmailAddressOrUsername.Trim();

        if (IsEmail(emailAddressOrUsernameTrimed))
            emailAddress = emailAddressOrUsernameTrimed;
        else username = emailAddressOrUsernameTrimed;


        var baseApiResponse = await MizonApi.SendRequestAsync(new LoginOperation(new()
        {
            Username = username,
            EmailAddress = emailAddress,
            Password = Password,
        }), cancellationToken);


        ShowProgressBar = false;
        ControlsIsEnabled = LoginButtonIsEnabled = true;

        if (baseApiResponse.IsSuccess)
        {
            _localStorageService.SetJwtToken(baseApiResponse.Content!.AccessToken);
            NavigationService.MainNavigate(typeof(HomePage));
            await WindowNativeInterop.AnimateWindowBorderColor(App.MainMizonWindow, Colors.Blue);
        }
        else
        {
            _teachingTipService.ShowMessage(baseApiResponse.Error!.Title, baseApiResponse.Error.Details, baseApiResponse.Error.CloseButtonText);
            await WindowNativeInterop.AnimateWindowBorderColor(App.MainMizonWindow, Colors.Red);
        }
    }
}
