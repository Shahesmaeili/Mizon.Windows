using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
using Windows.Networking.Connectivity;
using Windows.UI.ViewManagement;
using Launcher = Windows.System.Launcher;

namespace Mizon.Windows.Services;

/// <summary>Event-driven OS observations, owned only while the VPN page is active.</summary>
public sealed class WindowsVpnPageEnvironment(DispatcherQueue dispatcher) : IVpnPageEnvironment
{
    private const string ParticlesSettingKey = "Vpn.ParticlesEnabled";
    private static readonly Uri SettingsUri = new("ms-settings:network-vpn");
    private readonly DispatcherQueue _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    private UISettings? _uiSettings;
    private VpnPageEnvironmentState _state = new(false, "unknown", false);
    private bool _active;
    private bool _disposed;
    private bool _readInFlight;
    private bool _refreshRequested;
    private int _queuedRefresh;
    private int _generation;

    public event EventHandler? StateChanged;

    public VpnPageEnvironmentState ReadState() => _state;

    public bool ReadParticlesEnabled()
    {
        try
        {
            var values = Microsoft.Windows.Storage.ApplicationData.GetDefault().LocalSettings.Values;
            return !values.TryGetValue(ParticlesSettingKey, out var value) || value is not bool enabled || enabled;
        }
        catch (Exception) { return true; }
    }

    public bool TrySaveParticlesEnabled(bool enabled)
    {
        try
        {
            Microsoft.Windows.Storage.ApplicationData.GetDefault().LocalSettings.Values[ParticlesSettingKey] = enabled;
            return true;
        }
        catch (Exception) { return false; }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_active) return;
        _active = true;
        _generation++;
        _state = new(false, "unknown", false);
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        try
        {
            _uiSettings = new UISettings();
            _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
        }
        catch (Exception) { _uiSettings = null; }
        RefreshOnDispatcher();
    }

    public void Stop()
    {
        if (!_active) return;
        _active = false;
        _generation++;
        _readInFlight = false;
        _refreshRequested = false;
        try { NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged; }
        catch (Exception) { }
        if (_uiSettings is { } settings)
        {
            try { settings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged; }
            catch (Exception) { }
            _uiSettings = null;
        }
    }

    private void OnNetworkStatusChanged(object sender) => QueueRefresh();
    private void OnAnimationsEnabledChanged(UISettings sender, object args) => QueueRefresh();

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _queuedRefresh, 1) != 0) return;
        if (!_dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _queuedRefresh, 0);
            // This is a request for a fresh snapshot, not an old snapshot to apply. An event
            // queued before reactivation must not swallow a newer event in the same queue.
            if (_active && !_disposed) RefreshOnDispatcher();
        }))
            Interlocked.Exchange(ref _queuedRefresh, 0);
    }

    private void RefreshOnDispatcher()
    {
        var motionAllowed = false;
        try { motionAllowed = _uiSettings?.AnimationsEnabled == true; }
        catch (Exception) { }
        Publish(_state with { MotionAllowed = motionAllowed });
        _refreshRequested = true;
        if (_readInFlight) return;
        ReadNetworkState();
    }

    private void ReadNetworkState()
    {
        _readInFlight = true;
        _refreshRequested = false;
        var generation = _generation;
        // WinRT profile enumeration can wait on network services. Keep it off the UI thread.
        _ = Task.Run(ReadNetworkStatus).ContinueWith(task =>
        {
            var status = task.Status == TaskStatus.RanToCompletion ? task.Result : "unknown";
            _ = task.Exception; // Observe an unexpected worker failure without surfacing native details.
            _dispatcher.TryEnqueue(() =>
            {
                if (!_active || _disposed || generation != _generation) return;
                _readInFlight = false;
                Publish(new(true, status, _state.MotionAllowed));
                if (_refreshRequested) ReadNetworkState();
            });
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static string ReadNetworkStatus()
    {
        try
        {
            var status = MapConnectivity(NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel());
            if (status == "internet") return status;
            // A machine may have local connectivity without an Internet connection profile.
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                var profileStatus = MapConnectivity(profile.GetNetworkConnectivityLevel());
                if (profileStatus == "internet") return profileStatus;
                if (profileStatus == "local") status = profileStatus;
            }
            return status;
        }
        catch (Exception) { return "unknown"; }
    }

    private static string MapConnectivity(NetworkConnectivityLevel? level) => level switch
    {
        NetworkConnectivityLevel.InternetAccess => "internet",
        NetworkConnectivityLevel.LocalAccess or NetworkConnectivityLevel.ConstrainedInternetAccess => "local",
        _ => "offline"
    };

    private void Publish(VpnPageEnvironmentState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> OpenSystemSettingsAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var operation = Launcher.LaunchUriAsync(SettingsUri);
        try { return await operation.AsTask(cancellationToken); }
        finally
        {
            try
            {
                if (operation.Status != AsyncStatus.Started) operation.Close();
            }
            catch (Exception) { /* Native-operation cleanup must not replace the launch outcome. */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        StateChanged = null;
    }
}
