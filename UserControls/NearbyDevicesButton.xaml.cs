using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Mizon.Windows.Services;
using Mizon.Windows.Services.Discovery;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Symbol = FluentIcons.Common.Symbol;

namespace Mizon.Windows.UserControls;

public sealed partial class NearbyDevicesButton : UserControl
{
    private const int OrbitDeviceLimit = 6;
    private readonly ObservableCollection<NearbyDeviceRow> _rows = [];
    private readonly Dictionary<(string Platform, bool Light), SolidColorBrush> _platformBrushes = [];
    private readonly UISettings _settings = new();
    private readonly LocalizationService _localization = LocalizationService.Current;
    private XamlRoot? _flyoutRoot;
    private Window? _flyoutWindow;
    private NearbyDiscoveryState? _renderedState;
    private Storyboard? _motion;
    private int _updateQueued;
    private bool _open;
    private bool _windowActive;
    private bool _scanShown;
    private bool _synchronizingSwitch;
    private bool _restoreTriggerFocus;

    public NearbyDevicesButton()
    {
        InitializeComponent();
        Devices.ItemsSource = _rows;
    }

    internal HomePageViewModel? ViewModel { get; set; }

    private string Text(string key) => _localization.GetString("NearbyDevices/" + key);

    private void Control_Loaded(object sender, RoutedEventArgs args)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.NearbyDevices.Changed += QueueUpdate;
            viewModel.PropertyChanged += ViewModelChanged;
        }
        ActualThemeChanged += ControlThemeChanged;
        _localization.PropertyChanged += LocalizationChanged;
        ApplyLocalization();
        UpdateState();
    }

    private void Control_Unloaded(object sender, RoutedEventArgs args)
    {
        CloseFlyout();
        if (ViewModel is { } viewModel)
        {
            viewModel.NearbyDevices.Changed -= QueueUpdate;
            viewModel.PropertyChanged -= ViewModelChanged;
        }
        ActualThemeChanged -= ControlThemeChanged;
        _localization.PropertyChanged -= LocalizationChanged;
    }

    private void Flyout_Opening(object sender, object args)
    {
        _open = true;
        _windowActive = true;
        _avatarLifetime = new CancellationTokenSource();
        _flyoutRoot = XamlRoot;
        if (_flyoutRoot is not null) _flyoutRoot.Changed += FlyoutRootChanged;
        _flyoutWindow = App.MainMizonWindow;
        if (_flyoutWindow is not null) _flyoutWindow.Activated += FlyoutWindowActivated;
        _settings.AnimationsEnabledChanged += AnimationsChanged;
        DevicesFlyout.AreOpenCloseAnimationsEnabled = _settings.AnimationsEnabled;
        UpdateState();
    }

    private void Flyout_Opened(object sender, object args)
    {
        if (_open && !DiscoverySwitch.Focus(FocusState.Programmatic))
            CloseButton.Focus(FocusState.Programmatic);
        UpdateMotion(_renderedState?.Status);
    }

    private void Flyout_Closed(object sender, object args)
    {
        ReleaseFlyout();
        if (_restoreTriggerFocus && IsLoaded) Trigger.Focus(FocusState.Programmatic);
        _restoreTriggerFocus = false;
    }

    internal void CloseFlyout()
    {
        if (_open) DevicesFlyout.Hide();
        ReleaseFlyout();
    }

    private void ReleaseFlyout()
    {
        if (!_open) return;
        _open = false;
        _windowActive = false;
        if (_flyoutRoot is not null) _flyoutRoot.Changed -= FlyoutRootChanged;
        if (_flyoutWindow is not null) _flyoutWindow.Activated -= FlyoutWindowActivated;
        _flyoutRoot = null;
        _flyoutWindow = null;
        _settings.AnimationsEnabledChanged -= AnimationsChanged;
        _scanShown = false;
        StopMotion();
        ReleaseAvatars();
        _rows.Clear();
        OrbitPeers.Children.Clear();
        _renderedState = null;
    }

    private void FlyoutRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) =>
        ResizePanel(_renderedState?.Devices.Count ?? 0);

    private void ResizePanel(int deviceCount)
    {
        if (_flyoutRoot is not { } root) return;
        var width = Math.Clamp(root.Size.Width - 28, 1, 356);
        var desiredHeight = DiscoverySwitch.IsOn ? 412 + Math.Min(deviceCount, 2) * 52 : 372;
        Panel.Width = width;
        Panel.Height = Math.Clamp(root.Size.Height - 70, 1, desiredHeight);
        Orbit.Height = width < 300 ? 127 : 153;
    }

    private void FlyoutWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        UpdateMotion(_renderedState?.Status);
    }

    private void Surface_GettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (args.InputDevice == FocusInputDeviceKind.GameController) ControllerHint.Visibility = Visibility.Visible;
        else if (args.InputDevice is FocusInputDeviceKind.Keyboard or FocusInputDeviceKind.Mouse or FocusInputDeviceKind.Touch)
            ControllerHint.Visibility = Visibility.Collapsed;
    }

    private void Panel_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.OriginalKey is VirtualKey.GamepadB or VirtualKey.Escape)
        {
            args.Handled = true;
            DismissAndRestoreFocus();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs args) => DismissAndRestoreFocus();

    private void DismissAndRestoreFocus()
    {
        _restoreTriggerFocus = true;
        DevicesFlyout.Hide();
    }

    private async void DiscoverySwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_open || _synchronizingSwitch || ViewModel is not { } viewModel) return;
        var enabled = DiscoverySwitch.IsOn;
        if (viewModel.CanStartNearbyDiscovery && enabled != viewModel.NearbyDiscoveryEnabled)
            await viewModel.SetNearbyDiscoveryEnabledAsync(enabled);
        if (_open) UpdateState();
    }

    private async void RetryDiscovery_Click(object sender, RoutedEventArgs args)
    {
        if (!_open || ViewModel is not { CanStartNearbyDiscovery: true, NearbyDiscoveryEnabled: true } viewModel)
            return;
        // The retry disappears as soon as the scan restarts. Keep focus within the flyout.
        DiscoverySwitch.Focus(FocusState.Programmatic);
        await viewModel.SetNearbyDiscoveryEnabledAsync(true);
        if (_open) UpdateState();
    }

    private void QueueUpdate()
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _updateQueued, 0);
                if (IsLoaded) UpdateState();
            }))
            Interlocked.Exchange(ref _updateQueued, 0);
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(HomePageViewModel.CanStartNearbyDiscovery) or nameof(HomePageViewModel.IsSigningOut))
            UpdateState();
    }

    private void ControlThemeChanged(FrameworkElement sender, object args)
    {
        _renderedState = null;
        if (_open) UpdateState();
    }

    private void LocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision)) return;
        _renderedState = null;
        ApplyLocalization();
        UpdateState();
    }

    private void ApplyLocalization()
    {
        Panel.FlowDirection = _localization.FlowDirection;
        Panel.Language = _localization.LanguageTag;
        TitleText.FontFamily = SubtitleText.FontFamily = VisibilityText.FontFamily = PrivacyText.FontFamily
            = StatusText.FontFamily = StatusDetail.FontFamily = DevicesHeading.FontFamily
            = ControllerHint.FontFamily = RetryDiscovery.FontFamily = _localization.TextFontFamily;
        TitleText.Text = Text("Title");
        SubtitleText.Text = Text("Subtitle");
        VisibilityText.Text = Text("Discoverability");
        PrivacyText.Text = Text("Privacy");
        RetryDiscovery.Content = Text("Retry");
        ControllerHint.Text = Text("ControllerFooter");
        AutomationProperties.SetName(Trigger, Text("Title"));
        AutomationProperties.SetName(DiscoverySwitch, VisibilityText.Text);
        AutomationProperties.SetHelpText(DiscoverySwitch, PrivacyText.Text);
        AutomationProperties.SetName(CloseButton, Text("Close"));
        AutomationProperties.SetName(Devices, Text("List"));
        AutomationProperties.SetHelpText(Trigger, Text("Controller"));
        ToolTipService.SetToolTip(Trigger, Text("Title"));
        ToolTipService.SetToolTip(CloseButton, Text("Close"));
    }

    private void UpdateState()
    {
        if (ViewModel is not { } viewModel) return;
        var state = viewModel.NearbyDevices.ReadState();
        var active = state.Status is NearbyDiscoveryStatus.Starting or NearbyDiscoveryStatus.Searching;
        ActiveSignal.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        InactiveSignal.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        var hasDevices = state.Devices.Count != 0;
        var statusKey = state.Status switch
        {
            NearbyDiscoveryStatus.Searching => "Empty",
            NearbyDiscoveryStatus.Starting => "Starting",
            NearbyDiscoveryStatus.Unavailable => "Unavailable",
            _ => "Off"
        };
        var summary = hasDevices ? _localization.Format("NearbyDevices/Count", state.Devices.Count) : Text(statusKey);
        AutomationProperties.SetItemStatus(Trigger, summary);
        if (!_open) return;

        _synchronizingSwitch = true;
        try
        {
            DiscoverySwitch.IsEnabled = viewModel.CanStartNearbyDiscovery;
            DiscoverySwitch.IsOn = viewModel.NearbyDiscoveryEnabled;
        }
        finally { _synchronizingSwitch = false; }

        EmptyState.Visibility = hasDevices ? Visibility.Collapsed : Visibility.Visible;
        DevicesHeading.Visibility = hasDevices ? Visibility.Visible : Visibility.Collapsed;
        RetryDiscovery.Visibility = state.Status == NearbyDiscoveryStatus.Unavailable && viewModel.NearbyDiscoveryEnabled
            && viewModel.CanStartNearbyDiscovery ? Visibility.Visible : Visibility.Collapsed;
        StatusIcon.Symbol = state.Status switch
        {
            NearbyDiscoveryStatus.Off => Symbol.CircleOff,
            NearbyDiscoveryStatus.Unavailable => Symbol.WiFiWarning,
            _ => Symbol.Scan
        };
        SetLiveText(DevicesHeading, hasDevices ? summary : string.Empty);
        SetLiveText(StatusText, hasDevices ? string.Empty : summary);
        StatusDetail.Text = Text(statusKey + "Detail");

        if (!ReferenceEquals(state, _renderedState))
        {
            _renderedState = state;
            UpdateRows(state);
            UpdateOrbit();
        }
        ResizePanel(state.Devices.Count);
        UpdateMotion(state.Status);
    }

    private static void SetLiveText(TextBlock block, string value)
    {
        if (string.Equals(block.Text, value, StringComparison.Ordinal)) return;
        block.Text = value;
        if (block.Visibility == Visibility.Visible && value.Length != 0)
            FrameworkElementAutomationPeer.FromElement(block)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void UpdateRows(NearbyDiscoveryState state)
    {
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo(_localization.LanguageTag),
            CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);
        var wanted = state.Devices.OrderBy(device => device.Name, comparer).ThenBy(device => device.Id)
            .Select(CreateRow).ToArray();
        for (var index = 0; index < wanted.Length; index++)
        {
            var row = wanted[index];
            var existing = index;
            while (existing < _rows.Count && _rows[existing].Id != row.Id) existing++;
            if (existing == _rows.Count) _rows.Insert(index, row);
            else
            {
                if (existing != index) _rows.Move(existing, index);
                if (!_rows[index].SamePresentation(row)) _rows[index] = row;
            }
        }
        while (_rows.Count > wanted.Length) _rows.RemoveAt(_rows.Count - 1);
    }

    private NearbyDeviceRow CreateRow(NearbyDevice device)
    {
        var platform = Text(device.Platform);
        var version = _localization.Format("NearbyDevices/Version", platform, device.AppVersion);
        var identity = device.Account is { } account
            ? device.DisplayName is { } name && !string.Equals(name, account, StringComparison.OrdinalIgnoreCase)
                ? $"{name} · @{account}" : $"@{account}"
            : string.Empty;
        var symbol = device.Platform switch
        {
            "windows" => Symbol.Desktop,
            "macos" => Symbol.Laptop,
            "ios" or "android" => Symbol.Phone,
            "watchos" or "wearos" => Symbol.Smartwatch,
            _ => Symbol.DeviceMeetingRoom
        };
        var row = new NearbyDeviceRow(device.Id, device.Account, device.Name, symbol, identity, version,
            GetPlatformBrush(device.Platform), _localization.TextFontFamily);
        if (device.Account is { } username && _avatars.TryGetValue(username, out var avatar))
            row.Avatar = avatar;
        return row;
    }

    private SolidColorBrush GetPlatformBrush(string platform)
    {
        var light = ActualTheme == ElementTheme.Light;
        var key = (platform, light);
        if (_platformBrushes.TryGetValue(key, out var brush)) return brush;

        var rgb = platform switch
        {
            "windows" => light ? 0x00748C : 0x7DD4E8,
            "macos" => light ? 0x4E47A6 : 0xACA7FF,
            "ios" => light ? 0x087762 : 0x7DDFBE,
            "android" => light ? 0x27783D : 0x83D890,
            "watchos" => light ? 0x9E4D10 : 0xFFB77C,
            "wearos" => light ? 0x245CAC : 0x8BBCFF,
            _ => light ? 0x46536A : 0xB8C3D8
        };
        brush = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        _platformBrushes.Add(key, brush);
        return brush;
    }

    private void UpdateOrbit()
    {
        var count = Math.Min(OrbitDeviceLimit, _rows.Count);
        var markers = OrbitPeers.Children;
        while (markers.Count > count) markers.RemoveAt(markers.Count - 1);
        for (var index = 0; index < count; index++)
        {
            if (index == markers.Count)
                markers.Add(new ContentPresenter { ContentTemplate = (DataTemplate)Resources["OrbitMarkerTemplate"] });
            var marker = (ContentPresenter)markers[index];
            marker.Content = _rows[index];
            // Match the Mac radar: six slots, alternating the outer and middle bands.
            var angle = index * Math.Tau / OrbitDeviceLimit - Math.PI / 3;
            var distance = index % 2 == 0 ? 60 : 46.8;
            Canvas.SetLeft(marker, 62.5 + Math.Cos(angle) * distance);
            Canvas.SetTop(marker, 62.5 + Math.Sin(angle) * distance);
        }
    }

    private void AnimationsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_open) return;
            DevicesFlyout.AreOpenCloseAnimationsEnabled = _settings.AnimationsEnabled;
            UpdateMotion(_renderedState?.Status);
        });

    private void UpdateMotion(NearbyDiscoveryStatus? status)
    {
        if (!_open || !_windowActive || !_settings.AnimationsEnabled
            || status is not (NearbyDiscoveryStatus.Starting or NearbyDiscoveryStatus.Searching))
        {
            _scanShown = false;
            StopMotion();
            return;
        }
        if (_scanShown || !Orbit.IsLoaded) return;
        _scanShown = true;

        var rotation = new DoubleAnimation
        {
            From = 0, To = 360, Duration = new Duration(TimeSpan.FromSeconds(1.25)),
            RepeatBehavior = new RepeatBehavior(2)
        };
        Storyboard.SetTarget(rotation, OrbitRotation);
        Storyboard.SetTargetProperty(rotation, "Angle");
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), Value = 1 });
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.32)), Value = 1 });
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5)), Value = 0 });
        Storyboard.SetTarget(fade, Sweep);
        Storyboard.SetTargetProperty(fade, "Opacity");
        _motion = new Storyboard();
        _motion.Children.Add(rotation);
        _motion.Children.Add(fade);
        _motion.Completed += SweepCompleted;
        _motion.Begin();
    }

    private void SweepCompleted(object? sender, object args) => StopMotion();

    private void StopMotion()
    {
        if (_motion is not { } motion) return;
        _motion = null;
        motion.Completed -= SweepCompleted;
        motion.Stop();
    }
}
