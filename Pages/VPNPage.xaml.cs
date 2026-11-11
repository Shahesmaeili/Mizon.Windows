using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Mizon.Windows.Common;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class VPNPage : Page, IMcpVpnPage
{
    private CancellationTokenSource? _pageLifetime;
    private Window? _window;
    private bool _active;
    private bool _released;
    private bool? _wide;
    private bool? _compact;
    private bool _networkAnnouncementQueued;

    public VPNPageViewModel ViewModel { get; private set; }
    public LocalizationService Localization => LocalizationService.Current;

    public VPNPage()
    {
        ViewModel = CreateViewModel();
        InitializeComponent();
    }

    private VPNPageViewModel CreateViewModel() => new(new WindowsVpnPageEnvironment(DispatcherQueue));

    public string Text(string key, int revision) => Localization.GetString("Vpn/" + key, revision);
    public Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public bool HasError(string? code) => !string.IsNullOrEmpty(code);
    public string NetworkText(string status, int revision) => Text(status switch
    {
        "internet" => "InternetAvailable",
        "local" => "LocalOnly",
        "offline" => "Offline",
        _ => "NetworkUnknown"
    }, revision);
    public string MotionText(bool allowed, int revision) => Text(allowed ? "MotionDescription" : "MotionReduced", revision);
    public string ErrorText(string? code, int revision) => code is null ? string.Empty : Text(code switch
    {
        "preference_save_failed" => "PreferenceSaveError",
        "environment_unavailable" => "EnvironmentError",
        "settings_timeout" => "SettingsTimeout",
        _ => "SettingsError"
    }, revision);

    private void Page_Loaded(object sender, RoutedEventArgs args)
    {
        if (_active) return;
        if (_released)
        {
            ViewModel = CreateViewModel();
            _released = false;
        }
        _active = true;
        _pageLifetime = new CancellationTokenSource();
        _window = App.MainMizonWindow;
        if (_window is not null) _window.Closed += Window_Closed;
        Bindings.Update();
        UpdateLayoutMode(ActualWidth);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Activate();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        // Close access before the outgoing visual has finished its transition.
        ReleasePage();
        base.OnNavigatedFrom(args);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded) ReleasePage();
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        ReleasePage();
        ParticleBackground.Dispose();
    }

    private void ReleasePage()
    {
        if (_released) return;
        _active = false;
        _released = true;
        var lifetime = _pageLifetime;
        _pageLifetime = null;
        try { lifetime?.Cancel(); }
        catch (AggregateException) { /* A canceled observer cannot prevent page cleanup. */ }
        lifetime?.Dispose();
        if (_window is not null) _window.Closed -= Window_Closed;
        _window = null;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Dispose();
        // StopTracking releases the live localization source as well as the old view model.
        Bindings.StopTracking();
        ParticleBackground.IsAnimationEnabled = false;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_active || ViewModel.IsLoading || args.PropertyName != nameof(VPNPageViewModel.NetworkStatus)
            || _networkAnnouncementQueued || _pageLifetime is null) return;
        var lifetime = _pageLifetime.Token;
        _networkAnnouncementQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _networkAnnouncementQueued = false;
            if (lifetime.IsCancellationRequested || !_active || XamlRoot?.IsHostVisible != true) return;
            AccessibilityAnnouncements.Announce(NetworkHost,
                NetworkText(ViewModel.NetworkStatus, Localization.Revision), "VpnNetwork");
        });
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutMode(args.NewSize.Width);

    private void UpdateLayoutMode(double width)
    {
        if (LayoutRoot is null || !double.IsFinite(width) || width <= 0) return;
        var wide = width >= 940;
        var compact = width < 600;
        // Only crossing a breakpoint changes layout. No per-pixel rebuild or new visual tree.
        if (_wide != wide && VisualStateManager.GoToState(this, wide ? "Wide" : "Stacked", false))
            _wide = wide;
        if (_compact != compact && VisualStateManager.GoToState(this, compact ? "Compact" : "Comfortable", false))
            _compact = compact;
    }

    private void ParticlesSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_active && sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.ParticlesEnabled)
            ViewModel.SetParticlesEnabled(toggle.IsOn);
    }

    private async void OpenWindowsSettings_Click(object sender, RoutedEventArgs args)
    {
        if (!_active || _pageLifetime is null) return;
        try { await ViewModel.OpenWindowsSettingsAsync(_pageLifetime.Token); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    bool IMcpVpnPage.IsActive => _active && !_released && ViewModel.IsActive;
    CancellationToken IMcpVpnPage.Lifetime => _pageLifetime?.Token ?? new CancellationToken(true);
    McpVpnState IMcpVpnPage.GetState() => new()
    {
        IsAvailable = _active && !_released && ViewModel.IsActive,
        IsConfigured = ViewModel.IsConfigured,
        IsConnected = ViewModel.IsConnected,
        IsBusy = ViewModel.IsBusy,
        IsLoading = ViewModel.IsLoading,
        IsPreferenceSaved = ViewModel.IsPreferenceSaved,
        LastErrorCode = ViewModel.LastErrorCode,
        NetworkStatus = ViewModel.NetworkStatus,
        ParticlesEnabled = ViewModel.ParticlesEnabled,
        MotionAllowed = ViewModel.MotionAllowed
    };
    void IMcpVpnPage.SetParticlesEnabled(bool enabled) => ViewModel.SetParticlesEnabled(enabled);
    Task<bool> IMcpVpnPage.OpenWindowsSettingsAsync(CancellationToken cancellationToken) =>
        ViewModel.OpenWindowsSettingsAsync(cancellationToken);
}
