using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Mizon.API;

namespace Mizon.Windows.Services;

internal sealed class MizonProfilePresenceTransport : IProfilePresenceTransport
{
    private readonly MizonSocket _socket;
    private bool _disposed;

    internal MizonProfilePresenceTransport(MizonSocket socket)
    {
        _socket = socket;
        socket.HubConnectionStateChanged += ConnectionChanged;
        socket.WindowPresenceChanged += PresenceChanged;
    }

    public event EventHandler? StateChanged;
    public ProfilePresenceTransportState ReadState() => _disposed ? default
        : new(_socket.IsConnected, _socket.WindowPresence?.IsOnline);

    public Task SetWindowPresenceAsync(bool isActive, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _socket.SetWindowPresenceAsync(isActive, cancellationToken);
    }

    private void ConnectionChanged(object? sender, HubConnectionState state) => StateChanged?.Invoke(this, EventArgs.Empty);
    private void PresenceChanged(object? sender, ClientWindowPresenceResponse state) => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.HubConnectionStateChanged -= ConnectionChanged;
        _socket.WindowPresenceChanged -= PresenceChanged;
        StateChanged = null;
        // The authenticated shell owns the shared music/notification connection.
    }
}
