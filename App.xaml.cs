using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Mizon.API;
using Mizon.API.Client;
using Mizon.Windows.Common;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using RGB.NET.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using WinRT.Interop;


namespace Mizon.Windows;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        Services = ConfigureServices();
        this.InitializeComponent();
        this.UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled WinUI exception.", e.Exception);
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            AppLog.Error("Unhandled AppDomain exception.", exception);
        else
            AppLog.Information("Unhandled AppDomain exception without Exception details.");
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }


    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Information("Application launch started.");
        MainMizonWindow = new MainWindow();
        var taskbarService = Services.GetRequiredService<ITaskbarService>();
        var hwnd = WindowNative.GetWindowHandle(MainMizonWindow);
        taskbarService.Initialize(hwnd);

        MainMizonWindow.Activate();
        taskbarService.NotifyWindowActivated();

        Program.ConnectActivationHandler(MainMizonWindow.DispatcherQueue, ReceiveActivation);
        AppInitialize();

        WindowNativeInterop.RemoveNativeWindowMenu(MainMizonWindow);
        RainbowFrameInitialize();



    }

    private void RainbowFrameInitialize()
    {
        //var rainbowFrame = new RainbowFrame(MainMizonWindow);
        //rainbowFrame.ChangeFrameColor(Colors.Blue);
    }

    public static Window? MainMizonWindow;
    // A single immutable request prevents IDs/actions from different activations
    // being combined while Welcome validates the account. The latest link wins.
    internal static AppActivationRequest PendingActivation { get; private set; } = new();
    internal static string? PendingDestination
    {
        get => PendingActivation.Destination;
        set => PendingActivation = value is null ? new() : new(Destination: value);
    }
    internal static AppActivationRequest TakePendingActivation()
    {
        var request = PendingActivation;
        PendingActivation = new();
        return request;
    }
    internal static void ClearPendingActivation() => PendingActivation = new();
    internal static void CancelPendingNavigation()
    {
        ClearPendingActivation();
        if (((App)Current).Services.GetRequiredService<NavigationService>().CurrentMainPage is HomePage home)
            home.CancelDestination();
    }
    internal static void ResumePendingActivation() => ((App)Current).OpenPendingActivation();
    private bool _initialized;
    private AppActionsService? _appActions;
    private MulticastDiscoveryServiceV2? _discoveryService;

    private void ReceiveActivation(AppActivationRequest request)
    {
        if (request.HasDestination)
        {
            PendingActivation = request;
            if (_initialized) OpenPendingActivation();
        }
        if (MainMizonWindow is MainWindow window) window.ActivateExistingWindow();
    }

    private void OpenPendingActivation()
    {
        if (!PendingActivation.HasDestination) return;
        var navigation = Services.GetRequiredService<NavigationService>();
        // Never tear down a form while it is submitting. Its existing completion
        // path resumes the latest request, including content destinations.
        if (navigation.CurrentMainPage is LoginPage { ViewModel.ControlsIsEnabled: false }) return;
        // The outstanding logout owns the session until its cleanup completes;
        // opening an account page here would let that cleanup overwrite a new login.
        if (navigation.CurrentMainPage is HomePage { ViewModel.IsSigningOut: true })
        {
            ClearPendingActivation();
            return;
        }
        if (OpenPendingAccountDestination(navigation)) return;
        if (navigation.CurrentMainPage is HomePage home)
        {
            home.OpenPendingDestination();
            return;
        }
        var hasSession = !string.IsNullOrWhiteSpace(Services.GetRequiredService<LocalStorageService>().GetJwtToken());
        if (hasSession)
        {
            // A form may be open while an existing session is still present.
            // Validate it through Welcome before exposing authenticated pages.
            if (navigation.CurrentMainPage is not WelcomePage) navigation.MainNavigate(typeof(WelcomePage));
        }
        else if (navigation.CurrentMainPage is not LoginPage)
            navigation.MainNavigate(typeof(LoginPage));
    }

    internal static bool OpenPendingAccountDestination(NavigationService navigation)
    {
        // Page-only actions never submit credentials or sign the current account out.
        // Login consumes the requested form after its controls have loaded.
        if (PendingDestination is "login" or "register")
        {
            AppLog.Information($"Opening account destination: {PendingDestination}.");
            if (navigation.CurrentMainPage is LoginPage login) login.OpenPendingAccountAction();
            else navigation.MainNavigate(typeof(LoginPage));
            return true;
        }
        return false;
    }

    private void AppInitialize()
    {
        AppLog.Information("Application initialization started.");
        var localStorageService = Services.GetRequiredService<LocalStorageService>();
        var navigationService = Services.GetRequiredService<NavigationService>();
        // Subscribe before the first navigation; login/logout replace the shell tasks as one list.
        _ = Services.GetRequiredService<JumpListService>();
        var mizonApi = Services.GetRequiredService<MizonApi>();

        mizonApi.SetDeviceInformation(DeviceInformations.GetOsVersion(),
            DeviceInformations.GetDeviceIdentifier(),
            DeviceInformations.GetDeviceModel(),
            DeviceInformations.GetDeviceName(),
            DeviceInformations.GetDeviceUsername(),
            MizonVersions.CurrentVersion);

        var userToken = localStorageService.GetJwtToken();
        if (!string.IsNullOrWhiteSpace(userToken))
            mizonApi.SetToken(userToken);

        // The first activation is queued before initialization. Restore the session
        // before honoring its page, and do not briefly load a different page first.
        _initialized = true;
        if (!OpenPendingAccountDestination(navigationService))
            navigationService.MainNavigate(string.IsNullOrWhiteSpace(userToken) ? typeof(LoginPage) : typeof(WelcomePage));
        if (AppActionsService.IsSupported)
        {
            _appActions = new AppActionsService(localStorageService, !string.IsNullOrWhiteSpace(userToken), ReceiveAppAction);
            Program.ConnectAppActionHandler(_appActions.Invoke);
        }
        var authentication = new McpAuthenticationService(navigationService, localStorageService);
        McpAccessService.Current.Start(MainMizonWindow!.DispatcherQueue,
            authentication, new McpAppearanceService(AppearanceSettingsService.Current),
            new McpNavigationService(new AppDeepLinkNavigation(navigationService, localStorageService, authentication)),
            new McpTaskbarPinService(TaskbarPinService.Current),
            new McpMusicHomeService(
                () => navigationService.CurrentMainPage is Pages.HomePage { ViewModel.IsSigningOut: false }
                    ? navigationService.CurrentInternalPage as IMcpMusicHomePage : null,
                () => !string.IsNullOrWhiteSpace(localStorageService.GetJwtToken())));
        MainMizonWindow.Closed += MainWindow_Closed;
        // The authenticated shell starts the socket after account validation. Login and
        // Welcome do not need SignalR's connection, transport buffers or reconnect loop.

    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        MainMizonWindow!.Closed -= MainWindow_Closed;
        _appActions?.Dispose();
        _appActions = null;
        _discoveryService?.Dispose();
        _discoveryService = null;
        ClearPendingActivation();
        McpAccessService.Current.Dispose();
        TaskbarPinService.DisposeCurrent();
    }

    private void ReceiveAppAction(AppActivationRequest request)
    {
        ReceiveActivation(request);
    }


    private void Instance_DevicesChanged(object? sender, DevicesChangedEventArgs e)
    {

    }

    private void Instance_Exception(object? sender, ExceptionEventArgs e)
    {

    }


    /// <summary>
    /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Configures the services for the application.
    /// </summary>
    private IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        // services.AddTransient<IMizonApiResponseMiddleware, MizonMiddleware>();
        services.AddSingleton<DispatcherQueue>(
    DispatcherQueue.GetForCurrentThread()
);
        services.AddMizonApi();
        services.AddSingleton(sp => new Lazy<MizonSocket>(sp.GetRequiredService<MizonSocket>));
        services.AddSingleton<MulticastDiscoveryServiceV2>(_ => _discoveryService = new());

        // Register a shared MediaDownloader
        services.AddSingleton(sp =>
        {
            var client = new HttpClient();
            var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MizonCache");
            return new Mizon.API.Client.Operations.Media.MediaDownloader(client, cacheRoot);
        });

        // Register ViewModels:
        services.AddTransient<LoginPageViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<WelcomePageViewModel>();
        services.AddSingleton<HomePageViewModel>();
        services.AddSingleton<MusicTopUserControlViewModel>();
        services.AddSingleton<MusicPageViewModel>();
        services.AddSingleton<MusicPlaylistPlaybackService>();
        services.AddSingleton<MusicPlaybackActivityService>();
        services.AddSingleton<PictureInPictureService>();
        services.AddSingleton<MusicsPageViewModel>();

        services.AddSingleton<IShortcutService, ShortcutService>();

        services.AddSingleton<ITaskbarService, TaskbarService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<JumpListService>();
        services.AddSingleton<TeachingTipService>();
        services.AddSingleton<LocalStorageService>();
        services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
        services.AddSingleton<RgbMusicLightingService>();

        
        return services.BuildServiceProvider();
    }
}
