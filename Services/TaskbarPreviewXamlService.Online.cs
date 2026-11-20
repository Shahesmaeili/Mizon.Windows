using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.UI.Dispatching;
using Mizon.API;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;

namespace Mizon.Windows.Services;

internal sealed partial class TaskbarPreviewXamlService
{
    private readonly global::Windows.UI.ViewManagement.UISettings _onlineAnimations = new();
    private readonly object _onlineSnapshotGate = new();
    private MizonSocket? _onlineSocket;
    private DispatcherQueueTimer? _onlineTimer;
    private CancellationTokenSource? _onlineSession;
    private HubConnectionState _onlineTransportState;
    private TaskbarOnlineSnapshot? _publishedOnlineSnapshot;
    private int? _onlineFollowerCount;
    private long _nextFollowerRead;
    private long _observedOnlineRevision = -1;
    private int _onlineConnectionVersion;
    private bool _readingOnlineFollowers;
    private bool _onlineFollowersDirty;
    private bool? _networkHasInternet;

    internal TaskbarOnlineSnapshot? OnlineSnapshot
    {
        get
        {
            lock (_onlineSnapshotGate) return _publishedOnlineSnapshot;
        }
    }

    private string OnlineSnapshotPath => Path.Combine(_directory, "online.txt");

    private void StartOnlineStatus()
    {
        if (_onlineSocket is not null) return;

        _onlineSession = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _onlineSocket = _getSocket();
        _onlineTransportState = _onlineSocket.ConnectionState;
        _onlineSocket.HubConnectionStateChanged += PreviewSocketStateChanged;
        _onlineSocket.OnlineClientsChanged += PreviewOnlineCountChanged;
        NetworkInformation.NetworkStatusChanged += PreviewNetworkChanged;
        _onlineAnimations.AnimationsEnabledChanged += PreviewAnimationsChanged;
        _onlineTimer = _dispatcher.CreateTimer();
        _onlineTimer.Interval = TimeSpan.FromSeconds(1);
        _onlineTimer.Tick += PreviewOnlineTimerTick;
        _onlineTimer.Start();
        ReadOnlineNetwork();
        RefreshOnlineStatus();
    }

    private void StopOnlineStatus()
    {
        if (_onlineSocket is not { } socket) return;
        socket.HubConnectionStateChanged -= PreviewSocketStateChanged;
        socket.OnlineClientsChanged -= PreviewOnlineCountChanged;
        NetworkInformation.NetworkStatusChanged -= PreviewNetworkChanged;
        _onlineAnimations.AnimationsEnabledChanged -= PreviewAnimationsChanged;
        _onlineSocket = null;
        _onlineSession?.Cancel();
        _onlineSession?.Dispose();
        _onlineSession = null;
        if (_onlineTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= PreviewOnlineTimerTick;
            _onlineTimer = null;
        }
        lock (_onlineSnapshotGate) _publishedOnlineSnapshot = null;
        _onlineFollowerCount = null;
        _observedOnlineRevision = -1;
        _onlineFollowersDirty = false;
        _readingOnlineFollowers = false;
        _nextFollowerRead = 0;
        _onlineConnectionVersion++;
        try { File.Delete(OnlineSnapshotPath); }
        catch (IOException error) { AppLog.Error("Taskbar online status cleanup failed.", error); }
    }

    private void PreviewSocketStateChanged(object? sender, HubConnectionState state) =>
        _dispatcher.TryEnqueue(() =>
        {
            if (_onlineSocket is null || !ReferenceEquals(sender, _onlineSocket)) return;
            _onlineTransportState = state;
            RefreshOnlineStatus();
        });

    private void PreviewOnlineCountChanged(object? sender, EventArgs args) =>
        _dispatcher.TryEnqueue(RefreshOnlineStatus);

    private void PreviewNetworkChanged(object sender) => _dispatcher.TryEnqueue(() =>
    {
        if (_onlineSocket is null) return;
        ReadOnlineNetwork();
        RefreshOnlineStatus();
    });
    private void PreviewAnimationsChanged(global::Windows.UI.ViewManagement.UISettings sender, object args) =>
        _dispatcher.TryEnqueue(RefreshOnlineStatus);
    private void PreviewOnlineTimerTick(DispatcherQueueTimer sender, object args) => RefreshOnlineStatus();

    private void ReadOnlineNetwork()
    {
        try
        {
            _networkHasInternet = NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel()
                == NetworkConnectivityLevel.InternetAccess;
        }
        catch (Exception error)
        {
            _networkHasInternet = null;
            AppLog.Information($"Taskbar network status unavailable ({error.GetType().Name}).");
        }
    }

    private TaskbarOnlinePhase GetOnlinePhase(MizonSocket socket)
    {
        if (_networkHasInternet == false)
            return TaskbarOnlinePhase.NoInternet;
        return _onlineTransportState switch
        {
            HubConnectionState.Connecting => TaskbarOnlinePhase.Connecting,
            HubConnectionState.Reconnecting => TaskbarOnlinePhase.Reconnecting,
            HubConnectionState.Connected when socket.IsConnected => TaskbarOnlinePhase.Connected,
            _ => TaskbarOnlinePhase.Disconnected
        };
    }

    private void RefreshOnlineStatus()
    {
        if (_disposed || _onlineSocket is not { } socket) return;
        var phase = GetOnlinePhase(socket);
        var count = phase == TaskbarOnlinePhase.Connected ? socket.OnlineClients : null;
        if (phase != TaskbarOnlinePhase.Connected && _publishedOnlineSnapshot?.Phase == TaskbarOnlinePhase.Connected)
        {
            _onlineConnectionVersion++;
            _onlineFollowerCount = null;
            _nextFollowerRead = 0;
        }
        var revision = count?.Revision ?? -1;
        if (_observedOnlineRevision != revision)
        {
            _observedOnlineRevision = revision;
            _onlineFollowersDirty = true;
        }

        PublishOnlineSnapshot(new(phase, count?.Count,
            phase == TaskbarOnlinePhase.Connected ? _onlineFollowerCount : null, _onlineAnimations.AnimationsEnabled));
        var now = Environment.TickCount64;
        if (phase != TaskbarOnlinePhase.Connected || count is null || _readingOnlineFollowers || now < _nextFollowerRead)
            return;
        if (_onlineFollowersDirty || now >= _nextFollowerRead + 13_000)
            _ = RefreshOnlineFollowersAsync(socket);
    }

    private async Task RefreshOnlineFollowersAsync(MizonSocket socket)
    {
        if (_onlineSession is not { } session) return;
        var token = session.Token;
        var version = _onlineConnectionVersion;
        _readingOnlineFollowers = true;
        _onlineFollowersDirty = false;
        _nextFollowerRead = Environment.TickCount64 + 2_000;
        try
        {
            var followers = await socket.GetOnlineFollowerCountAsync(token);
            if (!token.IsCancellationRequested && version == _onlineConnectionVersion && socket.IsConnected)
                _onlineFollowerCount = followers >= 0 ? followers : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested && version == _onlineConnectionVersion)
            {
                _onlineFollowerCount = null;
                AppLog.Information($"Online follower count unavailable ({error.GetType().Name}).");
            }
        }
        finally
        {
            if (!token.IsCancellationRequested && ReferenceEquals(_onlineSession, session))
            {
                _readingOnlineFollowers = false;
                RefreshOnlineStatus();
            }
        }
    }

    private void PublishOnlineSnapshot(TaskbarOnlineSnapshot snapshot)
    {
        if (_publishedOnlineSnapshot == snapshot) return;
        var temporary = Path.Combine(_directory, "online-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, snapshot.Encode(_window, (uint)Environment.ProcessId));
            File.Move(temporary, OnlineSnapshotPath, true);
            lock (_onlineSnapshotGate) _publishedOnlineSnapshot = snapshot;
        }
        catch (Exception error) { AppLog.Error("Taskbar online status could not be published.", error); }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException error) { AppLog.Error("Taskbar online temporary file cleanup failed.", error); }
            catch (UnauthorizedAccessException error) { AppLog.Error("Taskbar online temporary file cleanup failed.", error); }
        }
    }
}
