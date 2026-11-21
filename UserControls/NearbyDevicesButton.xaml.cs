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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class NearbyDevicesButton : UserControl
{
    private readonly ObservableCollection<NearbyDeviceRow> _rows = [];
    private readonly UISettings _settings = new();
    private readonly LocalizationService _localization = LocalizationService.Current;
    private XamlRoot? _flyoutRoot;
    private NearbyDiscoveryState? _renderedState;
    private Storyboard? _motion;
    private int _updateQueued;
    private bool _open;

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
        _localization.PropertyChanged -= LocalizationChanged;
    }

    private void Flyout_Opening(object sender, object args)
    {
        _open = true;
        _avatarLifetime = new CancellationTokenSource();
        _flyoutRoot = XamlRoot;
        if (_flyoutRoot is not null) _flyoutRoot.Changed += FlyoutRootChanged;
        _settings.AnimationsEnabledChanged += AnimationsChanged;
        DevicesFlyout.AreOpenCloseAnimationsEnabled = _settings.AnimationsEnabled;
        ResizePanel();
        UpdateState();
    }

    private void Flyout_Opened(object sender, object args)
    {
        if (_open && !DiscoveryAction.Focus(FocusState.Programmatic))
            Panel.Focus(FocusState.Programmatic);
    }

    private void Flyout_Closed(object sender, object args) => ReleaseFlyout();

    internal void CloseFlyout()
    {
        if (_open) DevicesFlyout.Hide();
        ReleaseFlyout();
    }

    private void ReleaseFlyout()
    {
        if (!_open) return;
        _open = false;
        if (_flyoutRoot is not null) _flyoutRoot.Changed -= FlyoutRootChanged;
        _flyoutRoot = null;
        _settings.AnimationsEnabledChanged -= AnimationsChanged;
        StopMotion();
        ReleaseAvatars();
        _rows.Clear();
        _renderedState = null;
    }

    private void FlyoutRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizePanel();

    private void ResizePanel()
    {
        if (_flyoutRoot is not { } root) return;
        Panel.Width = Math.Clamp(root.Size.Width - 40, 180, 372);
        Orbit.Visibility = root.Size.Height >= 420 ? Visibility.Visible : Visibility.Collapsed;
        Devices.MaxHeight = Math.Clamp(root.Size.Height - (root.Size.Height >= 420 ? 320 : 212), 78, 262);
        UpdateMotion(ViewModel?.NearbyDevices.ReadState().Status);
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
            DevicesFlyout.Hide();
            Trigger.Focus(FocusState.Programmatic);
        }
    }

    private async void DiscoveryAction_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel is not { } viewModel) return;
        var enabled = viewModel.NearbyDevices.ReadState().Status is NearbyDiscoveryStatus.Starting or NearbyDiscoveryStatus.Searching;
        if (enabled || viewModel.CanStartNearbyDiscovery)
            await viewModel.SetNearbyDiscoveryEnabledAsync(!enabled);
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
        TitleText.FontFamily = StatusText.FontFamily = PrivacyText.FontFamily = ControllerHint.FontFamily = DiscoveryAction.FontFamily = _localization.TextFontFamily;
        TitleText.Text = Text("Title");
        PrivacyText.Text = Text("Privacy");
        ControllerHint.Text = Text("ControllerFooter");
        AutomationProperties.SetName(Trigger, Text("Title"));
        AutomationProperties.SetName(Devices, Text("List"));
        AutomationProperties.SetHelpText(Trigger, Text("Controller"));
        ToolTipService.SetToolTip(Trigger, Text("Title"));
    }

    private void UpdateState()
    {
        if (ViewModel is not { } viewModel) return;
        var state = viewModel.NearbyDevices.ReadState();
        var enabled = state.Status is NearbyDiscoveryStatus.Starting or NearbyDiscoveryStatus.Searching;
        ActiveIndicator.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        var summary = state.Status == NearbyDiscoveryStatus.Searching && state.Devices.Count != 0
            ? _localization.Format("NearbyDevices/Count", state.Devices.Count)
            : Text(state.Status == NearbyDiscoveryStatus.Searching ? "Empty" : state.Status.ToString());
        AutomationProperties.SetItemStatus(Trigger, summary);
        if (!_open) return;

        DiscoveryAction.IsEnabled = enabled || viewModel.CanStartNearbyDiscovery;
        DiscoveryAction.Content = Text(enabled ? "Stop" : state.Status == NearbyDiscoveryStatus.Unavailable ? "Retry" : "Start");
        if (!string.Equals(summary, StatusText.Text, StringComparison.Ordinal))
        {
            StatusText.Text = summary;
            FrameworkElementAutomationPeer.FromElement(StatusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        if (!ReferenceEquals(state, _renderedState))
        {
            _renderedState = state;
            var wanted = state.Devices.Select(CreateRow).ToArray();
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
        Devices.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateMotion(state.Status);
    }

    private NearbyDeviceRow CreateRow(NearbyDevice device)
    {
        var platform = Text(device.Platform);
        var version = _localization.Format("NearbyDevices/Version", platform, device.AppVersion);
        var identity = device.Account is { } account
            ? device.DisplayName is { } display ? $"{display} · @{account}" : $"@{account}"
            : Text("NoAccount");
        var glyph = device.Platform switch
        {
            "windows" or "macos" => "\uE7F4",
            "ios" or "android" => "\uE8EA",
            "watchos" or "wearos" => "\uE823",
            _ => "\uE770"
        };
        return new(device.Id, device.Account, device.Name, glyph, identity, version, _localization.TextFontFamily);
    }

    private void AnimationsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) =>
        DispatcherQueue.TryEnqueue(() => UpdateMotion(ViewModel?.NearbyDevices.ReadState().Status));

    private void UpdateMotion(NearbyDiscoveryStatus? status)
    {
        if (!_open || !_settings.AnimationsEnabled || Orbit.Visibility != Visibility.Visible
            || status is not (NearbyDiscoveryStatus.Starting or NearbyDiscoveryStatus.Searching))
        {
            StopMotion();
            return;
        }
        if (_motion is not null) return;
        var rotation = new DoubleAnimation
        {
            From = 0, To = 360, Duration = new Duration(TimeSpan.FromSeconds(13)),
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(rotation, OrbitRotation);
        Storyboard.SetTargetProperty(rotation, "Angle");
        _motion = new Storyboard();
        _motion.Children.Add(rotation);
        _motion.Begin();
    }

    private void StopMotion()
    {
        _motion?.Stop();
        _motion = null;
    }
}
