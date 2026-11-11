using System;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Invoked on the app dispatcher. It keeps the normal navigation service as the
// behavior owner and gives protocol-for-results calls a bounded response lifetime.
internal sealed class AppDeepLinkResultDispatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly IAppDeepLinkNavigation _navigation;
    private AppDeepLinkResponseSession? _active;
    private string? _pendingUri;
    private bool _disposed;

    internal AppDeepLinkResultDispatcher(IAppDeepLinkNavigation navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _navigation = navigation;
    }

    // Reuse the existing permission-gated navigation read state. An operation
    // still unwinding after its deadline is no longer pending for its caller.
    internal string? PendingUri
    {
        get
        {
            lock (_gate)
                return !_disposed && _active is { IsCompleted: false } ? _pendingUri : null;
        }
    }

    internal async Task DispatchAsync(AppActivationRequest request, AppDeepLinkResponseSession response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        AppDeepLinkResponseSession? previous;
        lock (_gate)
        {
            // Accidental delivery of the same envelope twice must not cancel its
            // first operation or dispose its still-active cancellation source.
            if (ReferenceEquals(_active, response)) return;
            previous = _active;
            if (!_disposed)
            {
                _active = response;
                _pendingUri = request.ToUri();
            }
        }
        previous?.Cancel("superseded", "A newer navigation request replaced this request.");
        try
        {
            if (_disposed)
            {
                response.Cancel("app_closed", "The app navigation connection has closed.");
                return;
            }
            if (response.IsCompleted) return;
            if (!request.HasDestination)
            {
                response.Fail("invalid_uri", "The deep link does not identify a supported destination.");
                return;
            }
            response.Token.ThrowIfCancellationRequested();
            var result = await _navigation.OpenAsync(request, response.Token);
            response.Token.ThrowIfCancellationRequested();
            response.Complete(result);
        }
        catch (OperationCanceledException)
        {
            response.Cancel("cancelled", "Navigation was cancelled before it completed.");
        }
        catch (TimeoutException)
        {
            response.Cancel("timed_out", "The destination did not finish before the response deadline.");
        }
        catch (Exception)
        {
            response.Fail("navigation_failed", "The destination could not be opened.");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, response))
                {
                    _active = null;
                    _pendingUri = null;
                }
            }
            response.Dispose();
        }
    }

    internal void CancelActive(string code = "superseded", string message = "A newer navigation request replaced this request.")
    {
        AppDeepLinkResponseSession? active;
        lock (_gate)
        {
            active = _active;
            _active = null;
            _pendingUri = null;
        }
        active?.Cancel(code, message);
        // The owner of DispatchAsync disposes after its navigation call unwinds.
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        CancelActive("app_closed", "The app closed before the destination finished.");
        // Navigation is shared with MCP and is owned by the application.
    }
}
