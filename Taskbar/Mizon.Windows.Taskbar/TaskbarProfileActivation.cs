using Mizon.Windows.Services;

namespace Mizon.Windows.Taskbar;

internal readonly record struct TaskbarDeepLinkRequest(Guid RequestId, Uri Uri, string PackageFamilyName);
internal enum TaskbarLaunchStatus { Success, AppUnavailable, ProtocolUnavailable, Unknown }
internal readonly record struct TaskbarDeepLinkResponse(TaskbarLaunchStatus Status, string? Json);
internal sealed record TaskbarActivationOutcome(string Code, AppDeepLinkResult? Result = null)
{
    internal bool Success => Result?.Success == true;
}

internal interface ITaskbarDeepLinkTransport
{
    Task<TaskbarDeepLinkResponse> LaunchAsync(TaskbarDeepLinkRequest request, CancellationToken token);
}

// UI-thread owned. The native transport is the only Windows-dependent part.
// Cancellation invalidates a generation immediately; a late native reply cannot
// clear a newer request's ring or overwrite its accessible outcome.
internal sealed class TaskbarProfileActivation(ITaskbarDeepLinkTransport transport, TimeProvider? clock = null) : IDisposable
{
    internal static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private CancellationTokenSource? _pending;
    private (bool Authenticated, Guid? PublicId, string? PackageFamilyName) _account;
    private bool _disposed;
    internal event EventHandler? Changed;
    internal bool IsBusy => _pending is not null;
    internal TaskbarActivationOutcome? Outcome { get; private set; }

    internal void ApplyProfile(ProfileSnapshot snapshot)
    {
        if (_disposed) return;
        var account = (snapshot.IsAuthenticated, snapshot.ClientPublicId, snapshot.PackageFamilyName);
        if (account != _account) Cancel();
        _account = account;
    }

    internal async Task<TaskbarActivationOutcome> OpenAsync()
    {
        if (_disposed) return new("disposed");
        if (!_account.Authenticated) return new("canceled");
        if (IsBusy) return new("busy");
        var (_, publicId, packageFamilyName) = _account;
        if (!IsPackageFamilyName(packageFamilyName))
        {
            var unavailable = new TaskbarActivationOutcome("app_unavailable");
            Outcome = unavailable;
            NotifyChanged();
            return unavailable;
        }

        using var cancellation = new CancellationTokenSource();
        var timedOut = 0;
        using var deadline = _clock.CreateTimer(_ =>
        {
            Interlocked.Exchange(ref timedOut, 1);
            // Native cancellation callbacks must not fault a thread-pool timer.
            CancelRequest(cancellation);
        }, null, ResponseTimeout, Timeout.InfiniteTimeSpan);
        _pending = cancellation;
        Outcome = null;
        TaskbarActivationOutcome outcome = new("launch_failed");
        try
        {
            NotifyChanged();
            var request = new TaskbarDeepLinkRequest(Guid.NewGuid(), new Uri(publicId is { } id && id != Guid.Empty
                ? $"mizon://client/{id:D}" : "mizon://music"), packageFamilyName!);
            cancellation.Token.ThrowIfCancellationRequested();
            var operation = transport.LaunchAsync(request, cancellation.Token);
            // Windows must not leave the ring active forever if its native
            // operation ignores cancellation or the receiver exits mid-request.
            _ = ObserveCompletionAsync(operation);
            var response = await operation.WaitAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            outcome = response.Status switch
            {
                TaskbarLaunchStatus.AppUnavailable => new("app_unavailable"),
                TaskbarLaunchStatus.ProtocolUnavailable => new("protocol_unavailable"),
                TaskbarLaunchStatus.Success when AppDeepLinkResultProtocol.TryParse(response.Json, request.RequestId, out var result)
                    => new(result!.Code, result),
                TaskbarLaunchStatus.Success => new("invalid_response"),
                _ => new("launch_failed")
            };
        }
        catch (OperationCanceledException)
        {
            outcome = new(Volatile.Read(ref timedOut) != 0 && ReferenceEquals(_pending, cancellation) ? "timeout" : "canceled");
        }
        catch (Exception)
        {
            // Never display native exception messages or arbitrary returned text.
            outcome = new("launch_failed");
        }
        finally
        {
            // Also cancel any still-running native operation after WaitAsync ends.
            CancelRequest(cancellation);
            if (!_disposed && ReferenceEquals(_pending, cancellation))
            {
                // Clear ownership before notifying UI or disposing the CTS.
                _pending = null;
                Outcome = outcome;
                NotifyChanged();
            }
        }
        return outcome;
    }

    internal void Cancel()
    {
        var pending = _pending;
        _pending = null;
        var changed = pending is not null || Outcome is not null;
        Outcome = null;
        CancelRequest(pending); // The request's finally owns disposal.
        if (!_disposed && changed) NotifyChanged();
    }

    private static void CancelRequest(CancellationTokenSource? source)
    {
        if (source is null) return;
        try { source.Cancel(); }
        catch (AggregateException) { /* Cancellation is set even when a native callback fails. */ }
        catch (ObjectDisposedException) { /* A queued deadline raced completed ownership. */ }
    }

    private void NotifyChanged()
    {
        if (_disposed || Changed is not { } observers) return;
        foreach (EventHandler observer in observers.GetInvocationList())
        {
            if (_disposed) break;
            try { observer(this, EventArgs.Empty); }
            catch (Exception) { /* A failed presentation observer must not own request lifetime. */ }
        }
    }

    internal static bool IsPackageFamilyName(string? value)
    {
        if (value is null || value.Length is < 3 or > 255 || !value.Contains('_')) return false;
        foreach (var character in value)
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')) return false;
        return true;
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch (Exception) { /* A canceled/dead receiver is already reported by the owner. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
        Changed = null;
    }
}
