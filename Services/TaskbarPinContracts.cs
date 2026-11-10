using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

internal sealed record TaskbarPinState(bool IsSupported, bool IsPinned, bool IsPinningAllowed, bool IsBusy, string Code)
{
    public bool CanRequest => IsSupported && !IsPinned && IsPinningAllowed && !IsBusy;
}

internal sealed record TaskbarPinResult(bool Success, string Code, string Message, TaskbarPinState State);

internal interface ITaskbarPinService
{
    TaskbarPinState State { get; }
    event EventHandler? StateChanged;
    Task<TaskbarPinState> RefreshAsync(CancellationToken token = default);
    Task<TaskbarPinResult> RequestPinAsync(CancellationToken token = default);
}

// The only platform boundary; the menu and MCP share the same state and gate.
internal interface ITaskbarPinBackend : IDisposable
{
    void VerifyAccess();
    bool IsForeground { get; }
    Task<TaskbarPinState> ReadAsync(CancellationToken token);
    Task<bool> RequestPinAsync(CancellationToken token);
}
