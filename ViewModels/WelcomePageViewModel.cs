using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Common;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public sealed partial class WelcomePageViewModel : MainViewModel
{
    private readonly LocalStorageService _localStorageService;
    private readonly MediaDownloader _mediaDownloader;
    private WelcomeStage _stage;

    private enum WelcomeStage { Initial, Checking, Profile, Ready, ConnectionError }

    public WelcomePageViewModel(MizonApi mizonApi, Lazy<MizonSocket> mizonSocket,
        NavigationService navigationService, IAudioPlayerService audioPlayerService,
        LocalStorageService localStorageService, MediaDownloader mediaDownloader)
        : base(mizonApi, mizonSocket, navigationService, audioPlayerService)
    {
        _localStorageService = localStorageService;
        _mediaDownloader = mediaDownloader;
        RefreshLocalization();
    }

    public MeResponse? Account { get; private set; }
    internal long TaskbarProfileSession { get; private set; }
    public string? ProfileImagePath { get; private set; }
    public ImageSource? ProfileImage { get => field; private set => SetField(ref field, value); }
    public Visibility ProfileVisibility => ProfileImage is null ? Visibility.Collapsed : Visibility.Visible;
    public string DisplayName { get => field; private set => SetField(ref field, value); } = string.Empty;
    public string WelcomeTitle { get => field; private set => SetField(ref field, value); } = string.Empty;
    public string StatusTitle { get => field; private set => SetField(ref field, value); } = string.Empty;
    public string StatusDetail { get => field; private set => SetField(ref field, value); } = string.Empty;
    public bool IsBusy { get => field; private set => SetField(ref field, value); }
    internal string StageCode => _stage switch
    {
        WelcomeStage.Checking => "checking",
        WelcomeStage.Profile => "profile",
        WelcomeStage.Ready => "ready",
        WelcomeStage.ConnectionError => "connection_error",
        _ => "initial"
    };
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility { get => field; private set => SetField(ref field, value); } = Visibility.Collapsed;

    public void RefreshLocalization()
    {
        // Language changes only re-render the current state. Account requests,
        // profile downloads and navigation remain owned by InitializeAsync.
        var localization = LocalizationService.Current;
        WelcomeTitle = Account is null ? localization.GetString("WelcomeTitle")
            : string.IsNullOrWhiteSpace(DisplayName) ? localization.GetString("WelcomeBack")
            : localization.Format("WelcomeBackNamed", DisplayName);
        var (titleKey, detailKey) = _stage switch
        {
            WelcomeStage.Checking => ("WelcomeCheckingTitle", "WelcomeCheckingDetail"),
            WelcomeStage.Profile => ("WelcomeProfileTitle", "WelcomeProfileDetail"),
            WelcomeStage.Ready => ("WelcomeReadyTitle", "WelcomeReadyDetail"),
            WelcomeStage.ConnectionError => ("WelcomeConnectionErrorTitle", "WelcomeConnectionErrorDetail"),
            _ => ("WelcomeCheckingTitle", "WelcomeInitialDetail")
        };
        StatusTitle = localization.GetString(titleKey);
        StatusDetail = localization.GetString(detailKey);
    }

    private void SetStage(WelcomeStage stage)
    {
        _stage = stage;
        RefreshLocalization();
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        TaskbarProfileSession = TaskbarProfileBridge.CurrentSession;
        IsBusy = true;
        OnPropertyChanged(nameof(BusyVisibility));
        ErrorVisibility = Visibility.Collapsed;
        Account = null;
        ProfileImage = null;
        ProfileImagePath = null;
        OnPropertyChanged(nameof(ProfileVisibility));
        DisplayName = string.Empty;
        SetStage(WelcomeStage.Checking);

        try
        {
            var response = await MizonApi.SendRequestAsync(new MeOperation(new()), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!response.IsSuccess || response.Content is null)
            {
                if (response.Error?.Code is 401 or 20000)
                {
                    GoToLogin();
                    return false;
                }

                ShowConnectionError();
                return false;
            }

            Account = response.Content;
            var fullName = $"{Account.FirstName} {Account.LastName}".Trim();
            DisplayName = !string.IsNullOrWhiteSpace(Account.NickName) ? Account.NickName.Trim()
                : !string.IsNullOrWhiteSpace(fullName) ? fullName : Account.Username ?? string.Empty;
            SetStage(WelcomeStage.Profile);

            if (Account.MeProfile is { } profile)
                await LoadProfileAsync(profile.MediaPublicId, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            SetStage(WelcomeStage.Ready);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            AppLog.Error("Welcome account initialization failed.", exception);
            ShowConnectionError();
            return false;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(BusyVisibility));
        }
    }

    public void GoToLogin()
    {
        _localStorageService.SetJwtToken(null);
        MizonApi.ClearToken();
        NavigationService.MainNavigate(typeof(LoginPage));
    }

    public void ReleaseProfileImage()
    {
        ProfileImage = null;
        ProfileImagePath = null;
        OnPropertyChanged(nameof(ProfileVisibility));
    }

    private void ShowConnectionError()
    {
        SetStage(WelcomeStage.ConnectionError);
        ErrorVisibility = Visibility.Visible;
    }

    private async Task LoadProfileAsync(Guid mediaPublicId, CancellationToken cancellationToken)
    {
        // A missing or slow avatar must not prevent a valid account from entering the app.
        using var avatarCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        avatarCancellation.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            var profile = await ProfileImageLoader.LoadAsync(MizonApi, _mediaDownloader, mediaPublicId, avatarCancellation.Token);
            avatarCancellation.Token.ThrowIfCancellationRequested();
            ProfileImagePath = profile.Path;
            ProfileImage = profile.Image;
            OnPropertyChanged(nameof(ProfileVisibility));
        }
        catch (OperationCanceledException) when (avatarCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error($"Welcome profile image could not be loaded. MediaPublicId='{mediaPublicId}'.", exception);
        }
    }
}
