using System;
using System.Threading;

namespace Mizon.Windows.Services;

// One caller owns one response. The native receiver supplies the report callback;
// this lifetime boundary stays independent of COM, WinUI and the transport.
internal sealed class AppDeepLinkResponseSession : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _token;
    private Action<AppDeepLinkResult>? _report;
    private Timer? _deadline;
    private bool _completed;
    private bool _disposeRequested;
    private bool _resourcesDisposed;
    private int _cancellationInProgress;

    internal AppDeepLinkResponseSession(Guid requestId, Action<AppDeepLinkResult> report, TimeSpan? timeout = null)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("A request ID is required.", nameof(requestId));
        ArgumentNullException.ThrowIfNull(report);
        var duration = timeout ?? TimeSpan.FromSeconds(50);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        RequestId = requestId;
        _report = report;
        _token = _cancellation.Token;
        // Assign before enabling: a very short test deadline must not race the
        // field assignment and leave its timer alive after completion.
        _deadline = new Timer(static state => ((AppDeepLinkResponseSession)state!).Cancel(
            "timed_out", "The destination did not finish before the response deadline."), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _deadline.Change(duration, Timeout.InfiniteTimeSpan);
    }

    internal Guid RequestId { get; }
    internal CancellationToken Token => _token;
    internal bool IsCompleted { get { lock (_gate) return _completed; } }

    internal void Complete(AppNavigationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Finish(new AppDeepLinkResult
        {
            RequestId = RequestId,
            Success = outcome.Success,
            Code = outcome.Code,
            Message = outcome.Message,
            Data = outcome.Data
        }, cancel: false);
    }

    internal void Fail(string code, string message) => Finish(Failure(code, message), cancel: false);

    internal void Cancel(string code, string message) => Finish(Failure(code, message), cancel: true);

    private AppDeepLinkResult Failure(string code, string message) => new()
    {
        RequestId = RequestId, Success = false, Code = code, Message = message
    };

    private void Finish(AppDeepLinkResult result, bool cancel)
    {
        // Never let malformed metadata or an exception string cross the native
        // boundary. Callers provide fixed, sanitized messages rather than errors.
        if (!AppDeepLinkResultProtocol.IsValid(result))
            result = Failure("invalid_result", "The destination returned an invalid response.");
        Action<AppDeepLinkResult>? report;
        Timer? deadline;
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            report = _report;
            _report = null;
            deadline = _deadline;
            _deadline = null;
            if (cancel) _cancellationInProgress++;
        }
        deadline?.Dispose();
        if (cancel)
        {
            try { _cancellation.Cancel(); }
            catch (AggregateException) { /* A consumer's cancellation callback cannot suppress the response. */ }
            finally
            {
                lock (_gate)
                {
                    _cancellationInProgress--;
                    ReleaseCancellationIfReady();
                }
            }
        }
        // The native caller may already have gone away. Reporting is best-effort
        // but is never retried: Windows receives at most one completion attempt.
        try { report?.Invoke(result); }
        catch (Exception) { /* The transport owns diagnostics; never fault a timer or shutdown callback. */ }
    }

    public void Dispose()
    {
        Cancel("app_closed", "The app closed before the destination finished.");
        lock (_gate)
        {
            _disposeRequested = true;
            ReleaseCancellationIfReady();
        }
    }

    private void ReleaseCancellationIfReady()
    {
        // Dispose can be re-entered by a cancellation callback. Defer source
        // disposal until Cancel has unwound, and serialize it with timer races.
        if (!_disposeRequested || _cancellationInProgress != 0 || _resourcesDisposed) return;
        _resourcesDisposed = true;
        _cancellation.Dispose();
    }
}
