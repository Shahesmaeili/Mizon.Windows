using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;
using ModelContextProtocol.Protocol;

namespace Mizon.Windows.Mcp;

internal sealed class MizonAuthPipeClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeLock = new();
    private readonly string _pipeName;
    private readonly McpClientCredentialStore _credentials;
    private readonly string? _clientProfile;
    private string? _clientCredential;
    private NamedPipeClientStream? _pipe;
    private bool _disposed;
    private Task? _disposeTask;

    public MizonAuthPipeClient(string? credentialDirectory = null, string? clientProfile = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Windows user identity is unavailable.");
        _pipeName = McpPipeProtocol.PipePrefix + sid;
        _credentials = new(credentialDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mizon", "Mcp", "Identities"));
        if (clientProfile is not null)
        {
            if ((!Guid.TryParseExact(clientProfile, "N", out var profile) && !Guid.TryParseExact(clientProfile, "D", out profile))
                || profile == Guid.Empty) throw new ArgumentException("Invalid configured MCP client profile.", nameof(clientProfile));
            _clientProfile = profile.ToString("N");
        }
    }

    public async ValueTask<McpResponse> SendAsync(
        McpRequest request, Implementation? clientInfo, bool canOpenApplication,
        CancellationToken cancellationToken)
    {
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = requestLifetime.Token;
        await _requests.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!await ConnectForCallAsync(clientInfo, canOpenApplication ? request.Page : null, cancellationToken))
                return Error("app_not_running", "Mizon is not running. Open the app, or explicitly use mizon_open_auth_page to launch it.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Sign-in can include the existing API timeout. No automatic retry is
            // allowed for a mutation: the remote action may have already happened.
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try { return await ExchangeAsync(request, timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                CloseConnection();
                return Error("request_timeout", "Mizon did not finish in time. The connection was closed; reconnect and check state before repeating the operation.");
            }
        }
        catch (OperationCanceledException)
        {
            CloseConnection();
            throw;
        }
        catch (IdentityUnavailableException)
        {
            CloseConnection();
            return Error("identity_unavailable", "The saved MCP client identity could not be accessed. Existing permissions were not changed.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            CloseConnection();
            return Error("connection_lost", "The Mizon connection was interrupted. Reconnect and inspect its status; saved permissions are restored unless the user revoked them.");
        }
        catch (TimeoutException)
        {
            CloseConnection();
            return Error("connection_timeout", "Mizon is running but did not complete the connection handshake in time. Try again when the app is responsive.");
        }
        finally { _requests.Release(); }
    }

    private async Task<bool> ConnectForCallAsync(Implementation? clientInfo, string? launchPage, CancellationToken cancellationToken)
    {
        if (await EnsureConnectedAsync(clientInfo, TimeSpan.FromMilliseconds(600), cancellationToken)) return true;

        if (launchPage is not ("login" or "register" or "welcome")) return false;
        try
        {
            // Welcome has no public deep link. Launch the ordinary app entry and
            // request its internal validation page only through the consent gate.
            // These are allowlisted URIs, never user-supplied shell text.
            var launchUri = launchPage == "welcome" ? "mizon://home" : "mizon://" + launchPage;
            using var process = Process.Start(new ProcessStartInfo(launchUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }

        return await EnsureConnectedAsync(clientInfo, TimeSpan.FromSeconds(15), cancellationToken);
    }

    private async Task<bool> EnsureConnectedAsync(Implementation? clientInfo, TimeSpan availabilityTimeout, CancellationToken cancellationToken)
    {
        if (_pipe?.IsConnected == true) return true;
        CloseConnection();
        if (_clientCredential is null && _clientProfile is not null)
        {
            try { _clientCredential = await _credentials.GetOrCreateAsync(_clientProfile, cancellationToken); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException or ArgumentException)
            {
                // Never rotate a corrupt identity or forget an existing revoke.
                throw new IdentityUnavailableException();
            }
        }
        // Resolve the local proof before opening a pipe: credential-file lock
        // contention must not consume the app's deadline for the initial hello.
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _pipe = pipe;
        try
        {
            // The short budget only probes whether an OS pipe exists. Once it
            // connects, the app may need its busy UI dispatcher to create consent
            // state and reply; that is not evidence that the app isn't running.
            using (var availability = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                availability.CancelAfter(availabilityTimeout);
                try { await pipe.ConnectAsync(availability.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    CloseConnection();
                    return false;
                }
            }
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(TimeSpan.FromSeconds(15));
            McpResponse connected;
            try
            {
                connected = await ExchangeAsync(new McpRequest
                {
                    Operation = "connect",
                    // Labels come from MCP client metadata, not a verified publisher.
                    ClientName = ClientLabel(clientInfo?.Name, "MCP client"),
                    ClientTitle = ClientLabel(clientInfo?.Title, string.Empty),
                    ClientVersion = ClientLabel(clientInfo?.Version, string.Empty),
                    ClientCredential = _clientCredential
                }, handshake.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Mizon connection handshake timed out.");
            }
            if (connected.Connection is null)
                throw new IOException("Mizon did not return a connection status.");
            return true;
        }
        catch
        {
            CloseConnection();
            throw;
        }
    }

    private async Task<McpResponse> ExchangeAsync(McpRequest request, CancellationToken cancellationToken)
    {
        var pipe = _pipe ?? throw new IOException("Mizon is not connected.");
        await McpPipeProtocol.WriteAsync(pipe, request, McpJsonContext.Default.McpRequest, cancellationToken);
        return await McpPipeProtocol.ReadAsync(pipe, McpJsonContext.Default.McpResponse, cancellationToken)
            ?? throw new EndOfStreamException("Mizon closed the connection before replying.");
    }

    private static string ClientLabel(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        // Preserve useful labels while excluding multiline/control-character UI.
        var clean = string.Concat(value.Where(character => !char.IsControl(character))).Trim();
        return clean.Length == 0 ? fallback : clean[..Math.Min(clean.Length, 128)];
    }

    private static McpResponse Error(string code, string message) => new()
    {
        Success = false,
        Code = code,
        Message = message
    };

    private void CloseConnection()
    {
        var pipe = _pipe;
        _pipe = null;
        pipe?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        // Cancel both active I/O and queued requests before waiting for the gate.
        // Otherwise shutdown could wait for the entire operation timeout.
        _lifetime.Cancel();
        await _requests.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            CloseConnection();
            _clientCredential = null;
        }
        finally { _requests.Release(); }
        _requests.Dispose();
        _lifetime.Dispose();
    }

    private sealed class IdentityUnavailableException : Exception;
}
