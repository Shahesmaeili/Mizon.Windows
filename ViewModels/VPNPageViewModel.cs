using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mizon.Windows.Services;

namespace Mizon.Windows.ViewModels;

/// <summary>UI-dispatcher-owned state shared by the page and its scoped MCP adapter.</summary>
public sealed partial class VPNPageViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan SettingsTimeout = TimeSpan.FromSeconds(15);
    private readonly IVpnPageEnvironment _environment;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _settingsRequest;
    private bool _disposed;
    private bool _isActive;
    private bool _isBusy;
    private bool _isLoading;
    private string _networkStatus = "unknown";
    private bool _particlesEnabled = true;
    private bool _motionAllowed;
    private bool _isPreferenceSaved = true;
    private string? _lastErrorCode;

    public VPNPageViewModel(IVpnPageEnvironment environment, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
        _clock = clock ?? TimeProvider.System;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsActive => _isActive;
    public bool IsBusy => _isBusy;
    public bool IsLoading => _isLoading;
    // Mizon does not have a VPN transport or a configured connection yet.
    public bool IsConfigured => false;
    public bool IsConnected => false;
    public string NetworkStatus => _networkStatus;
    public bool ParticlesEnabled => _particlesEnabled;
    public bool MotionAllowed => _motionAllowed;
    public bool CanOpenSystemSettings => IsActive && !IsBusy;
    public bool IsParticleAnimationEnabled => IsActive && ParticlesEnabled && MotionAllowed;
    public bool IsPreferenceSaved => _isPreferenceSaved;
    public string? LastErrorCode => _lastErrorCode;

    public void Activate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActive) return;

        _lifetime = new CancellationTokenSource();
        Set(ref _isActive, true, nameof(IsActive));
        Set(ref _isLoading, true, nameof(IsLoading));
        Set(ref _lastErrorCode, null, nameof(LastErrorCode));
        _environment.StateChanged += OnEnvironmentStateChanged;
        try
        {
            // Keep an unsaved live choice if the platform store was temporarily unavailable.
            if (IsPreferenceSaved)
                Set(ref _particlesEnabled, _environment.ReadParticlesEnabled(), nameof(ParticlesEnabled));
            _environment.Start();
            ReadEnvironmentState();
        }
        catch (Exception)
        {
            Set(ref _networkStatus, "unknown", nameof(NetworkStatus));
            Set(ref _motionAllowed, false, nameof(MotionAllowed));
            Set(ref _isLoading, false, nameof(IsLoading));
            Set(ref _lastErrorCode, "environment_unavailable", nameof(LastErrorCode));
        }
        NotifyAvailability();
    }

    public void Deactivate()
    {
        if (!IsActive) return;

        Set(ref _isActive, false, nameof(IsActive));
        _environment.StateChanged -= OnEnvironmentStateChanged;
        var lifetime = _lifetime;
        _lifetime = null;
        _settingsRequest = null;
        CancelSafely(lifetime);
        lifetime?.Dispose();
        try { _environment.Stop(); }
        catch (Exception) { /* A failing OS observer must not retain active page state. */ }
        Set(ref _isBusy, false, nameof(IsBusy));
        Set(ref _isLoading, false, nameof(IsLoading));
        NotifyAvailability();
    }

    public void SetParticlesEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive || (ParticlesEnabled == enabled && IsPreferenceSaved)) return;

        Set(ref _particlesEnabled, enabled, nameof(ParticlesEnabled));
        var saved = false;
        try { saved = _environment.TrySaveParticlesEnabled(enabled); }
        catch (Exception) { /* Preserve the live preference even if storage is unavailable. */ }
        Set(ref _isPreferenceSaved, saved, nameof(IsPreferenceSaved));
        if (!saved)
            Set(ref _lastErrorCode, "preference_save_failed", nameof(LastErrorCode));
        else if (LastErrorCode == "preference_save_failed")
            Set(ref _lastErrorCode, null, nameof(LastErrorCode));
        OnPropertyChanged(nameof(IsParticleAnimationEnabled));
    }

    public async Task<bool> OpenWindowsSettingsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanOpenSystemSettings || _lifetime is null) return false;

        var lifetime = _lifetime;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        _settingsRequest = request;
        Set(ref _isBusy, true, nameof(IsBusy));
        Set(ref _lastErrorCode, null, nameof(LastErrorCode));
        OnPropertyChanged(nameof(CanOpenSystemSettings));
        try
        {
            // WaitAsync also bounds a misbehaving platform implementation that ignores cancellation.
            var launch = _environment.OpenSystemSettingsAsync(request.Token);
            _ = launch.ContinueWith(static task => { _ = task.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            var opened = await launch.WaitAsync(SettingsTimeout, _clock, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!OwnsRequest(request, lifetime)) return false;
            if (!opened) Set(ref _lastErrorCode, "settings_unavailable", nameof(LastErrorCode));
            return opened;
        }
        catch (TimeoutException)
        {
            CancelSafely(request);
            if (OwnsRequest(request, lifetime))
                Set(ref _lastErrorCode, "settings_timeout", nameof(LastErrorCode));
            return false;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Caller cancellation remains observable to the MCP permission/lifetime boundary.
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
        catch (Exception)
        {
            if (OwnsRequest(request, lifetime))
                Set(ref _lastErrorCode, "settings_failed", nameof(LastErrorCode));
            return false;
        }
        finally
        {
            if (ReferenceEquals(_settingsRequest, request))
            {
                _settingsRequest = null;
                Set(ref _isBusy, false, nameof(IsBusy));
                OnPropertyChanged(nameof(CanOpenSystemSettings));
            }
        }
    }

    private bool OwnsRequest(CancellationTokenSource request, CancellationTokenSource lifetime) =>
        !_disposed && IsActive && ReferenceEquals(_lifetime, lifetime) && ReferenceEquals(_settingsRequest, request);

    private void OnEnvironmentStateChanged(object? sender, EventArgs args)
    {
        if (!_disposed && IsActive) ReadEnvironmentState();
    }

    private void ReadEnvironmentState()
    {
        VpnPageEnvironmentState state;
        try { state = _environment.ReadState(); }
        catch (Exception)
        {
            state = new(true, "unknown", false);
            Set(ref _lastErrorCode, "environment_unavailable", nameof(LastErrorCode));
        }
        var status = state.NetworkStatus is "internet" or "local" or "offline" ? state.NetworkStatus : "unknown";
        Set(ref _networkStatus, status, nameof(NetworkStatus));
        Set(ref _motionAllowed, state.MotionAllowed, nameof(MotionAllowed));
        Set(ref _isLoading, !state.IsReady, nameof(IsLoading));
        OnPropertyChanged(nameof(IsParticleAnimationEnabled));
    }

    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanOpenSystemSettings));
        OnPropertyChanged(nameof(IsParticleAnimationEnabled));
    }

    private bool Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static void CancelSafely(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (AggregateException) { /* Cancellation observers cannot prevent page cleanup. */ }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Deactivate();
        _disposed = true;
        _environment.Dispose();
        PropertyChanged = null;
    }
}
