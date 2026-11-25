using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

public interface IProfilePresenceService
{
    ProfilePresenceState ReadState();
}

public sealed record ProfilePresenceState(
    bool HasSession,
    bool IsWindowActive,
    bool IsConnected,
    bool IsOnline,
    bool HasConfirmedState);

// Local focus acknowledgements and the account snapshot are independent.
internal readonly record struct ProfilePresenceTransportState(
    bool IsConnected, bool? ConfirmedWindowOnline, bool? AccountOnline = null);

internal interface IProfilePresenceTransport : IDisposable
{
    event EventHandler? StateChanged;
    ProfilePresenceTransportState ReadState();
    Task SetWindowPresenceAsync(bool isActive, CancellationToken cancellationToken);
    Task WatchAccountPresenceAsync(CancellationToken cancellationToken);
}
