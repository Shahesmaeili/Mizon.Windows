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

// A nullable confirmation distinguishes a server response from a locally desired state.
internal readonly record struct ProfilePresenceTransportState(bool IsConnected, bool? ConfirmedOnline);

internal interface IProfilePresenceTransport : IDisposable
{
    event EventHandler? StateChanged;
    ProfilePresenceTransportState ReadState();
    Task SetWindowPresenceAsync(bool isActive, CancellationToken cancellationToken);
}
