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

        PendingMusicPublicId = TryGetMusicPublicId(args.Arguments);

        MainMizonWindow = new MainWindow();
        var taskbarService = Services.GetRequiredService<ITaskbarService>();
        var hwnd = WindowNative.GetWindowHandle(MainMizonWindow);
        taskbarService.Initialize(hwnd);

        MainMizonWindow.Activate();
        taskbarService.NotifyWindowActivated();

        AppInitialize();

        WindowNativeInterop.RemoveNativeWindowMenu(MainMizonWindow);
        RainbowFrameInitialize();


        var infoWindow = new InfoWindow();
        infoWindow.Activate();
    }

    private void RainbowFrameInitialize()
    {
        //var rainbowFrame = new RainbowFrame(MainMizonWindow);
        //rainbowFrame.ChangeFrameColor(Colors.Blue);
    }

    public static Window? MainMizonWindow;
    internal static Guid? PendingMusicPublicId { get; set; }

    private static Guid? TryGetMusicPublicId(string arguments)
    {
        const string musicArgument = "--music";
        var argumentIndex = arguments.IndexOf(musicArgument, StringComparison.OrdinalIgnoreCase);
        if (argumentIndex < 0)
            return null;

        var remainingArguments = arguments[(argumentIndex + musicArgument.Length)..].Trim();
        var musicPublicIdText = remainingArguments.Trim().Trim('\"').Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Guid.TryParse(musicPublicIdText, out var musicPublicId) ? musicPublicId : null;
    }


    private async void AppInitialize()
    {
        AppLog.Information("Application initialization started.");
        var localStorageService = Services.GetRequiredService<LocalStorageService>();
        var navigationService = Services.GetRequiredService<NavigationService>();
        var mizonApi = Services.GetRequiredService<MizonApi>();

        mizonApi.SetDeviceInformation(DeviceInformations.GetOsVersion(),
            DeviceInformations.GetDeviceIdentifier(),
            DeviceInformations.GetDeviceModel(),
            DeviceInformations.GetDeviceName(),
            DeviceInformations.GetDeviceUsername(),
            MizonVersions.CurrentVersion);

        var userToken = localStorageService.GetJwtToken();
        //navigationService.MainNavigate(typeof(WelcomePage));
        if (string.IsNullOrWhiteSpace(userToken))
        {
            navigationService.MainNavigate(typeof(LoginPage));
        }
        else
        {
            mizonApi.SetToken(userToken);

            navigationService.MainNavigate(typeof(WelcomePage));
        }
        // start socket connection in background (best-effort)
        try
        {
            var socket = Services.GetRequiredService<MizonSocket>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await socket.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(userToken))
                        await socket.RegisterDeviceAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Socket start failed.", ex);
                }
            });
        }
        catch (Exception exception)
        {
            AppLog.Error("Socket initialization could not be scheduled.", exception);
        }

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
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        // services.AddTransient<IMizonApiResponseMiddleware, MizonMiddleware>();
        services.AddSingleton<DispatcherQueue>(
    DispatcherQueue.GetForCurrentThread()
);
        services.AddMizonApi();

        // Register a shared MediaDownloader
        services.AddSingleton(sp =>
        {
            var client = new HttpClient();
            var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MizonCache");
            return new Mizon.API.Client.Operations.Media.MediaDownloader(client, cacheRoot);
        });

        // Register ViewModels:
        services.AddSingleton<LoginPageViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<WelcomePageViewModel>();
        services.AddSingleton<HomePageViewModel>();
        services.AddSingleton<MusicTopUserControlViewModel>();
        services.AddSingleton<MusicPageViewModel>();
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
