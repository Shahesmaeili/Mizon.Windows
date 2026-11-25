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
using Mizon.Windows.Services.Discovery;
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
        ((App)Current).CancelResultActivation("cancelled", "Navigation was cancelled by the app.");
        if (((App)Current).Services.GetRequiredService<NavigationService>().CurrentMainPage is HomePage home)
            home.CancelDestination();
    }
    internal static void ResumePendingActivation() => ((App)Current).OpenPendingActivation();
    private bool _initialized;
    private AppActionsService? _appActions;
    private JumpListService? _jumpList;
    private NearbyDeviceDiscoveryService? _discoveryService;
    private AppDeepLinkResultDispatcher? _deepLinkResults;
    private AppActivationEnvelope? _pendingResultActivation;
    private AppDeepLinkResponseSession? _deferredForeground;
    private bool _closing;
    internal static string? PendingLinkUri => Current is App app
        ? app._pendingResultActivation is { Response.IsCompleted: false } pending
            ? pending.Request.ToUri() : app._deepLinkResults?.PendingUri
        : null;

    private void ReceiveActivation(AppActivationEnvelope envelope)
    {
        if (_closing) { envelope.Close("unavailable", "The app is closing."); return; }
        if (envelope.Response is not { } response)
        {
            ReceiveActivation(envelope.Request);
            return;
        }
        if (response.IsCompleted) { response.Dispose(); return; }
        if (!envelope.Request.HasDestination)
        {
            envelope.Close("invalid_uri", "The URI is not a supported Mizon destination.");
            return;
        }
        CancelResultActivation("superseded", "A newer deep-link request replaced this request.");
        ClearPendingActivation();
        if (_deepLinkResults is null) _pendingResultActivation = envelope;
        else StartActivation(envelope.Request, response, envelope.PresentAfterResponse);
        if (!envelope.PresentAfterResponse && MainMizonWindow is MainWindow window) window.ActivateExistingWindow();
    }

    private void StartActivation(AppActivationRequest request, AppDeepLinkResponseSession? response = null,
        bool presentAfterResponse = false)
    {
        var dispatcher = _deepLinkResults!;
        response ??= new AppDeepLinkResponseSession(Guid.NewGuid(), timeout: AppDeepLinkResponseSession.TimeoutFor(request));
        if (presentAfterResponse)
        {
            _deferredForeground = response;
            _ = PresentWhenReadyAsync(response);
        }
        _ = dispatcher.DispatchAsync(request, response);
    }

    private async Task PresentWhenReadyAsync(AppDeepLinkResponseSession response)
    {
        var result = await response.Completion;
        if (_closing || !ReferenceEquals(_deferredForeground, response)) return;
        _deferredForeground = null;
        if (result.Success && MainMizonWindow is MainWindow window) window.ActivateExistingWindow();
    }

    private void CancelResultActivation(string code, string message)
    {
        _deferredForeground = null;
        _pendingResultActivation?.Close(code, message);
        _pendingResultActivation = null;
        _deepLinkResults?.CancelActive(code, message);
    }

    private void ReceiveActivation(AppActivationRequest request)
    {
        if (_closing) return;
        if (request.HasDestination)
        {
            CancelResultActivation("superseded", "Another app activation replaced this request.");
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
        if (OpenPendingAccountDestination() || TryOpenPendingContent()) return;
        if (navigation.CurrentMainPage is HomePage) return; // Home will resume from Loaded.
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

    private bool OpenPendingAccountDestination()
    {
        if (PendingDestination is not ("login" or "register")) return false;
        StartActivation(TakePendingActivation());
        return true;
    }

    internal static bool TryOpenPendingContent()
    {
        var app = (App)Current;
        if (!PendingActivation.RequiresAuthentication
            || app.Services.GetRequiredService<NavigationService>().CurrentMainPage is not HomePage { CanOpenDestination: true })
            return false;
        app.StartActivation(TakePendingActivation());
        return true;
    }

    private void AppInitialize()
    {
        AppLog.Information("Application initialization started.");
        try { TaskbarProfileBridge.SetPackageFamilyName(global::Windows.ApplicationModel.Package.Current.Id.FamilyName); }
        catch (InvalidOperationException) { TaskbarProfileBridge.SetPackageFamilyName(null); }
        var localStorageService = Services.GetRequiredService<LocalStorageService>();
        var navigationService = Services.GetRequiredService<NavigationService>();
        // Subscribe before the first navigation; login/logout replace the shell tasks as one list.
        _jumpList = Services.GetRequiredService<JumpListService>();
        var mizonApi = Services.GetRequiredService<MizonApi>();

        mizonApi.SetDeviceInformation(DeviceInformations.GetOsVersion(),
            DeviceInformations.GetDeviceIdentifier(),
            DeviceInformations.GetDeviceModel(),
            DeviceInformations.GetDeviceName(),
            DeviceInformations.GetDeviceUsername(),
            MizonVersions.CurrentVersion);

        var userToken = localStorageService.GetJwtToken();
        _ = _jumpList.ConfigureAsync(!string.IsNullOrWhiteSpace(userToken));
        if (!string.IsNullOrWhiteSpace(userToken))
            mizonApi.SetToken(userToken);

        // The first activation is queued before initialization. Restore the session
        // before honoring its page, and do not briefly load a different page first.
        var authentication = new McpAuthenticationService(navigationService, localStorageService);
        var deepLinkNavigation = new AppDeepLinkNavigation(navigationService, localStorageService, authentication);
        _deepLinkResults = new AppDeepLinkResultDispatcher(deepLinkNavigation);
        _initialized = true;
        if (!OpenPendingAccountDestination())
            navigationService.MainNavigate(string.IsNullOrWhiteSpace(userToken) ? typeof(LoginPage) : typeof(WelcomePage));
        if (AppActionsService.IsSupported)
        {
            _appActions = new AppActionsService(localStorageService, !string.IsNullOrWhiteSpace(userToken), ReceiveAppAction);
            Program.ConnectAppActionHandler(_appActions.Invoke);
        }
        McpAccessService.Current.Start(MainMizonWindow!.DispatcherQueue,
            authentication, new McpAppearanceService(AppearanceSettingsService.Current),
            new McpNavigationService(deepLinkNavigation),
            new McpTaskbarPinService(TaskbarPinService.Current),
            new McpMusicHomeService(
                () => navigationService.CurrentMainPage is Pages.HomePage { ViewModel.IsSigningOut: false }
                    ? navigationService.CurrentInternalPage as IMcpMusicHomePage : null,
                () => !string.IsNullOrWhiteSpace(localStorageService.GetJwtToken())),
            presence: new McpPresenceService(Services.GetRequiredService<ProfilePresenceService>(),
                () => (MainMizonWindow as MainWindow)?.ReadTaskbarOnlineStatus()),
            windowPresentation: new McpWindowPresentationService(() => MainMizonWindow as IWindowPresentationHost),
            vpn: new McpVpnService(
                () => navigationService.CurrentMainPage is Pages.HomePage { ViewModel.IsSigningOut: false }
                    ? navigationService.CurrentInternalPage as IMcpVpnPage : null,
                () => !string.IsNullOrWhiteSpace(localStorageService.GetJwtToken())),
            personProfile: new McpPersonProfileService(
                () => navigationService.CurrentMainPage is Pages.HomePage { ViewModel.IsSigningOut: false }
                    ? navigationService.CurrentInternalPage as IMcpPersonProfilePage : null),
            musicDetails: new McpMusicDetailsService(
                () => navigationService.CurrentMainPage is Pages.HomePage { ViewModel.IsSigningOut: false }
                    && navigationService.CurrentInternalPage is Pages.MusicsPage { IsMusicDetailsActive: true } details
                        ? details : null),
            nearbyDevices: new McpNearbyDevicesService(Services.GetRequiredService<NearbyDeviceDiscoveryService>().ReadState,
                () =>
                {
                    var (available, received, uploaded) = Services.GetRequiredService<WindowsPeerMediaExchange>().ReadTransferState();
                    return new(available, received, uploaded);
                }),
            readDeviceSessions: ReadMcpDeviceSessionsAsync);
        MainMizonWindow.Closed += MainWindow_Closed;
        if (_pendingResultActivation is { } pendingResult)
        {
            _pendingResultActivation = null;
            StartActivation(pendingResult.Request, pendingResult.Response!, pendingResult.PresentAfterResponse);
        }
        // The authenticated shell starts the socket after account validation. Login and
        // Welcome do not need SignalR's connection, transport buffers or reconnect loop.

    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _closing = true;
        CancelResultActivation("unavailable", "The app has closed.");
        _deepLinkResults?.Dispose();
        _deepLinkResults = null;
        MainMizonWindow!.Closed -= MainWindow_Closed;
        _appActions?.Dispose();
        _appActions = null;
        _jumpList?.Dispose();
        _jumpList = null;
        _discoveryService?.Dispose();
        _discoveryService = null;
        ClearPendingActivation();
        McpAccessService.Current.Dispose();
        TaskbarPinService.DisposeCurrent();
    }

    private async Task<Mizon.Windows.Mcp.Contracts.McpResponse> ReadMcpDeviceSessionsAsync(CancellationToken token)
    {
        var api = Services.GetRequiredService<Mizon.API.MizonApi>();
        if (string.IsNullOrWhiteSpace(api.GetToken()))
            return new() { Code = "unavailable", Message = "Sign in to inspect device sessions." };

        var response = await api.SendRequestAsync(
            new Mizon.API.Client.Operations.Clients.ClientSessionsOperation(new()), token);
        token.ThrowIfCancellationRequested();
        if (!response.IsSuccess || response.Content is not { } content)
            return new() { Code = "unavailable", Message = "Device session status is unavailable." };

        var online = 0;
        foreach (var session in content.Sessions)
            if (session.IsOnline) online++;
        return new()
        {
            Success = true,
            Code = "device_sessions",
            Message = "Current signed-in session counts.",
            DeviceSessions = new(content.Sessions.Count, online, content.InactivityDays),
        };
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
        services.AddMizonApi(static () => LocalizationService.Current.LanguageTag);
        services.AddSingleton(sp => new Lazy<MizonSocket>(sp.GetRequiredService<MizonSocket>));
        services.AddSingleton(sp => new ProfilePresenceService(
            () => new MizonProfilePresenceTransport(sp.GetRequiredService<Lazy<MizonSocket>>().Value),
            static (session, state) => TaskbarProfileBridge.PublishPresence(session, state.IsOnline),
            static error => AppLog.Information($"Profile presence update failed ({error.GetType().Name}).")));
        services.AddSingleton<IProfilePresenceService>(sp => sp.GetRequiredService<ProfilePresenceService>());
        services.AddSingleton<WindowsPeerMediaExchange>();
        services.AddSingleton<NearbyDeviceDiscoveryService>(sp => _discoveryService = new(
            new WindowsNearbyDeviceTransport(sp.GetRequiredService<WindowsPeerMediaExchange>().Open),
            static error => AppLog.Information($"Nearby discovery stopped ({error.GetType().Name}, 0x{error.HResult:X8}).")));

        // Register a shared MediaDownloader
        services.AddSingleton(sp =>
        {
            var client = new HttpClient();
            var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MizonCache");
            var peers = sp.GetRequiredService<WindowsPeerMediaExchange>();
            var downloader = new Mizon.API.Client.Operations.Media.MediaDownloader(client, cacheRoot, peers);
            peers.Attach(downloader);
            return downloader;
        });

        // Register ViewModels:
        services.AddTransient<LoginPageViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<WelcomePageViewModel>();
        services.AddTransient<PersonProfileViewModel>();
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
