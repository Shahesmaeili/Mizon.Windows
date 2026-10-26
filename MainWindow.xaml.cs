using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System.Diagnostics;
using System.Linq;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Windows.UI.WindowManagement;

namespace Mizon.Windows;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new AcrylicSystemBackdrop(DesktopAcrylicKind.Default)
        {
            //Kind = MicaKind.Base

        };

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
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.ShowIconAndSystemMenu;
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;



        var navigationService = ((App)App.Current).Services.GetRequiredService<NavigationService>();
        navigationService.MainInitialize(MainFrame);

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

    private async void TaskbarService_PlaybackToggleRequested(object? sender, EventArgs eventArgs)
    {
        var audioPlayerService = ((App)App.Current).Services.GetRequiredService<IAudioPlayerService>();
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
