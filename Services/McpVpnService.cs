using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

/// <summary>Uses the active VPN page's existing operations. Never creates a page, starts a tunnel or changes OS settings.</summary>
internal sealed class McpVpnService(Func<IMcpVpnPage?> getActivePage, Func<bool> hasSession) : IMcpVpnService, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return Unavailable();
        if (request.Operation is not ("get_vpn_state" or "set_vpn_particles" or "open_vpn_settings"))
            return new() { Code = "unsupported_operation", Message = "This VPN operation is not supported." };
        if (request.Form is not null || request.Page is not null || request.Language is not null
            || request.Appearance is not null || request.Navigation is not null || request.MusicHome is not null
            || request.FullScreen is not null
            || (request.Operation == "set_vpn_particles" ? request.Vpn?.Enabled is null : request.Vpn is not null))
            return new() { Code = "invalid_arguments", Message = "Use only the arguments declared by this VPN tool." };
        try
        {
            if (!hasSession()) return new() { Code = "requires_sign_in", Message = "Sign in to Mizon before using its VPN page." };
            if (getActivePage() is not { IsActive: true } page || page.Lifetime.IsCancellationRequested)
                return Unavailable();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, page.Lifetime, _lifetime.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (request.Operation != "get_vpn_state" && page.GetState().IsBusy)
                return new() { Code = "busy", Message = "The VPN page is already opening Windows settings." };
            linked.Token.ThrowIfCancellationRequested();
            var opened = true;
            if (request.Operation == "set_vpn_particles") page.SetParticlesEnabled(request.Vpn!.Enabled!.Value);
            else if (request.Operation == "open_vpn_settings")
                opened = await page.OpenWindowsSettingsAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            // A completed launch cannot make data from an unloaded/replaced page current again.
            if (_disposed || !hasSession() || !page.IsActive || !ReferenceEquals(page, getActivePage())) return Unavailable();
            var state = page.GetState();
            linked.Token.ThrowIfCancellationRequested();
            if (_disposed || !hasSession() || !page.IsActive || !ReferenceEquals(page, getActivePage())) return Unavailable();
            return new()
            {
                Success = opened, Code = opened ? "ok" : "launch_failed",
                Message = opened ? "The VPN page operation completed." : "Windows VPN settings could not be opened.",
                Vpn = state
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (ObjectDisposedException) { return Unavailable(); }
        catch (Exception) { return new() { Code = "operation_failed", Message = "The VPN page operation could not be completed." }; }
    }

    private static McpResponse Unavailable() => new() { Code = "page_unavailable", Message = "Open the VPN page in Mizon first." };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
