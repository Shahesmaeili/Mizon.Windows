using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Mizon.Windows.Services;
using Mizon.Windows.Pages;
using System.IO;
using System.Runtime.InteropServices;
using WinRT.Interop;
using Mizon.Windows.ViewModels;
using System.Diagnostics;
using System.Linq;
using System.ComponentModel;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Windows.UI.WindowManagement;

namespace Mizon.Windows;

public sealed partial class MainWindow : Window
{
    private readonly IDisposable _appearanceAttachment;
    private readonly ProfilePresenceService _profilePresence;
    private readonly LocalStorageService _presenceStorage;

    internal void ShowWindowOverlay(UIElement overlay)
    {
        WindowOverlayHost.Children.Clear();
        WindowOverlayHost.Children.Add(overlay);
        WindowOverlayHost.Visibility = Visibility.Visible;
        MainFrame.IsEnabled = false;
    }

    internal void HideWindowOverlay(UIElement overlay)
    {
        if (!WindowOverlayHost.Children.Contains(overlay))
            return;

        WindowOverlayHost.Children.Clear();
        WindowOverlayHost.Visibility = Visibility.Collapsed;
        MainFrame.IsEnabled = true;
    }

    public MainWindow()
    {
        InitializeComponent();
        _profilePresence = ((App)Application.Current).Services.GetRequiredService<ProfilePresenceService>();
        _presenceStorage = ((App)Application.Current).Services.GetRequiredService<LocalStorageService>();
        Activated += PresenceWindowActivated;
        _presenceStorage.AuthenticationChanged += PresenceAuthenticationChanged;
        UpdatePresenceLanguage();
        InitializeMcpAccess();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon", "Mizon.ico"));

        switch (AppWindow.Presenter)
        {
            case Microsoft.UI.Windowing.OverlappedPresenter overlappedPresenter:
                //overlappedPresenter.SetBorderAndTitleBar(false, false);
                //overlappedPresenter.IsMaximizable = false;
                //overlappedPresenter.IsResizable = true;
                //overlappedPresenter.IsMinimizable = false;

                overlappedPresenter.PreferredMinimumWidth = 900;
                overlappedPresenter.PreferredMinimumHeight = 600;

                //overlappedPresenter.Maximize();
                break;
        }


        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        // Let WinUI own and refresh the native caption across resize/maximize/DPI changes.
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.ShowIconAndSystemMenu;
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
        _appearanceAttachment = AppearanceSettingsService.Current.Attach(this);
        var navigationService = ((App)App.Current).Services.GetRequiredService<NavigationService>();
        navigationService.MainInitialize(MainFrame);
        navigationService.MainNavigationChanged += NavigationChanged;
        navigationService.InternalNavigationChanged += NavigationChanged;
        LocalizationService.Current.PropertyChanged += LocalizationChanged;
        Closed += MainWindow_Closed;

        var mainViewModel = ((App)App.Current).Services.GetRequiredService<MainViewModel>();
        //DataContext = mainViewModel;

        mainViewModel.InitializeMainWindow(this);

        var teachingTipService = ((App)App.Current).Services.GetRequiredService<TeachingTipService>();
        teachingTipService.Initialize(MainTeachingTip);

        var taskbarService = ((App)App.Current).Services.GetRequiredService<ITaskbarService>();
        taskbarService.PlaybackToggleRequested += TaskbarService_PlaybackToggleRequested;
        taskbarService.FullScreenRequested += TaskbarService_FullScreenRequested;

        // DragMoveAndResizeHelper.SetDragMove(App.MainMizonWindow, DragAreaOverlayGrid);

        CenterWindow();
    }

    internal void RefreshTitle()
    {
        var navigation = ((App)Application.Current).Services.GetRequiredService<NavigationService>();
        var section = navigation.CurrentMainPage switch
        {
            LoginPage login => LocalizationService.Current.GetString(login.ViewModel.IsRegistrationMode ? "WindowTitleRegister" : "WindowTitleLogin"),
            WelcomePage => LocalizationService.Current.GetString("WindowTitleWelcome"),
            HomePage => navigation.CurrentInternalPage switch
            {
                MusicsPage => "جزئیات آهنگ",
                MusicPlaylistsPage => "پلی‌لیست‌های تو",
                FollowingActivitiesPage => "فعالیت دنبال‌شده‌ها",
                MusicPlaylistPage => "جزئیات پلی‌لیست",
                ClientProfilePage => "پروفایل کاربر",
                VPNPage => "VPN",
                SettingsPage => LocalizationService.Current.IsPersian ? "تنظیمات" : "Settings",
                SectionLandingPage page => page.Destination == "apps" ? "برنامه‌ها" : "فیلم و سریال",
                _ => "موسیقی"
            },
            _ => null
        };
        Title = section is null ? "Mizon" : $"Mizon - {section}";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var accountPage = navigation.CurrentMainPage is LoginPage or WelcomePage;
            presenter.PreferredMinimumWidth = accountPage ? 360 : 900;
            presenter.PreferredMinimumHeight = accountPage ? 400 : 600;
        }
    }

    private void NavigationChanged(object? sender, NavigationChangedEventArgs args)
    {
        if (args.IsEnteringPage) RefreshTitle();
    }

    private void LocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.Revision))
        {
            RefreshTitle();
            UpdatePresenceLanguage();
        }
    }

    private void PresenceWindowActivated(object sender, WindowActivatedEventArgs args) =>
        _profilePresence.SetWindowActive(args.WindowActivationState != WindowActivationState.Deactivated);

    private void PresenceAuthenticationChanged(object? sender, bool hasSession)
    {
        if (!hasSession) _profilePresence.SetSession(false, TaskbarProfileBridge.CurrentSession);
        // Becoming authenticated is owned by the validated HomePage lifecycle.
    }

    private static void UpdatePresenceLanguage()
    {
        var localization = LocalizationService.Current;
        TaskbarProfileBridge.SetPresenceLocalization(localization.LanguageTag,
            localization.GetString("ProfileMenu/PresenceOnline"), localization.GetString("ProfileMenu/PresenceOffline"));
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Activated -= PresenceWindowActivated;
        _presenceStorage.AuthenticationChanged -= PresenceAuthenticationChanged;
        if (((App)Application.Current).Services.GetRequiredService<NavigationService>().CurrentMainPage is HomePage home)
            home.ViewModel.StopProfilePresence();
        _profilePresence.Dispose();
        DisposeMcpAccess();
        _appearanceAttachment.Dispose();
        LocalizationService.Current.PropertyChanged -= LocalizationChanged;
        var services = ((App)Application.Current).Services;
        var navigation = services.GetRequiredService<NavigationService>();
        navigation.MainNavigationChanged -= NavigationChanged;
        navigation.InternalNavigationChanged -= NavigationChanged;
        var taskbar = services.GetRequiredService<ITaskbarService>();
        taskbar.PlaybackToggleRequested -= TaskbarService_PlaybackToggleRequested;
        taskbar.FullScreenRequested -= TaskbarService_FullScreenRequested;
        Closed -= MainWindow_Closed;
    }

    internal void ActivateExistingWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
            presenter.Restore();
        AppWindow.Show();
        Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    private async void TaskbarService_PlaybackToggleRequested(object? sender, EventArgs eventArgs)
    {
        var audioPlayerService = ((App)App.Current).Services.GetRequiredService<IAudioPlayerService>();
        if (audioPlayerService.AudioPlayerState == AudioPlayerState.Idle &&
            ((App)App.Current).Services.GetRequiredService<NavigationService>().CurrentInternalPage is MusicsPage)
            ((App)App.Current).Services.GetRequiredService<MusicPageViewModel>().PlayCommand.Execute(null);
        else
            await audioPlayerService.TogglePlayPauseAsync();
    }

    private void TaskbarService_FullScreenRequested(object? sender, EventArgs eventArgs)
    {
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        else
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }


    async Task BuzzWindowAsync()
    {



        int originalX = AppWindow.Position.X;
        int originalY = AppWindow.Position.Y;

        int shakeDistance = 15; // شدت لرزش
        int shakeCount = 10;    // تعداد تکان‌ها
        int delay = 20;         // سرعت

        for (int i = 0; i < shakeCount; i++)
        {
            int offset = (i % 2 == 0) ? shakeDistance : -shakeDistance;


            AppWindow.Move(new(
           originalX + offset,
           originalY));


            await Task.Delay(delay);
        }

        AppWindow.Move(new(
         originalX,
         originalY));
    }

    public static async Task GetBluetoothBatteryAsync()
    {
        // Battery Service UUID
        var batteryServiceSelector =
            GattDeviceService.GetDeviceSelectorFromUuid(
                GattServiceUuids.Battery);

        var devices = await DeviceInformation.FindAllAsync(batteryServiceSelector);

        if (devices.Count == 0)
        {
            Console.WriteLine("❌ No Bluetooth device with Battery Service found.");
            return;
        }

        foreach (var device in devices)
        {
            Debug.WriteLine($"🔍 Device: {device.Name}");

            var service =
                await GattDeviceService.FromIdAsync(device.Id);

            if (service == null)
                continue;

            var characteristicsResult =
                await service.GetCharacteristicsAsync();

            if (characteristicsResult.Status != GattCommunicationStatus.Success)
                continue;

            var batteryCharacteristic =
                characteristicsResult.Characteristics
                    .FirstOrDefault(c =>
                        c.Uuid == GattCharacteristicUuids.BatteryLevel);

            if (batteryCharacteristic == null)
                continue;

            var readResult =
                await batteryCharacteristic.ReadValueAsync();

            if (readResult.Status != GattCommunicationStatus.Success)
                continue;

            var reader = DataReader.FromBuffer(readResult.Value);
            byte batteryLevel = reader.ReadByte();

            Debug.WriteLine($"🔋 Battery Level: {batteryLevel}%");
        }


    }





    private async void CenterWindow()
    {
        var appWindowPlacementDetails = AppWindow.GetCurrentPlacement();

        var centerX = appWindowPlacementDetails.WorkArea.Width / 2 - appWindowPlacementDetails.NormalRect.Width / 2;
        var centerY = appWindowPlacementDetails.WorkArea.Height / 2 - appWindowPlacementDetails.NormalRect.Height / 2;

        AppWindow.Move(new(
            appWindowPlacementDetails.WorkArea.X + centerX,
            appWindowPlacementDetails.WorkArea.Y + centerY));

        await GetBluetoothBatteryAsync();


    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {


    }

    private async void Button_Click_1(object sender, RoutedEventArgs e)
    {
        await BuzzWindowAsync();
    }
}
