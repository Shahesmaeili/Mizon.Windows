using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static McpRequest VpnParticles(bool enabled) => new() { Operation = "set_vpn_particles", Vpn = new() { Enabled = enabled } };

    private static async Task VpnScopesAndState()
    {
        await using var host = await Host.StartAsync(Path.Combine(StorageRoot, "vpn-scopes", "grants.dat"));
        await using var peer = await host.ConnectAsync(Proof());
        var id = peer.Hello.Connection!.Id;
        foreach (var request in new[] { new McpRequest { Operation = "get_vpn_state" }, VpnParticles(false), new() { Operation = "open_vpn_settings" } })
            Assert((await peer.Call(request)) is { Code: "approval_required", Vpn: null }, "Pending VPN tools are protected");
        await host.Approve(id, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true);
        Assert(!(await peer.Call("get_vpn_state")).Success && !(await peer.Call(VpnParticles(false))).Success
            && !(await peer.Call("open_vpn_settings")).Success && host.VpnPage.Launches == 0,
            "All older domains together authorize no VPN operation");
        host.Auth.Handler = (_, _) => Task.FromResult(new McpResponse { Success = true, Vpn = host.VpnPage.GetState() });
        Assert((await peer.Call("get_state")).Vpn is null, "Unrelated adapter cannot disclose VPN state without VPN read");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, vpnRead: true);
        Assert((await peer.Call("get_vpn_state")) is { Success: true, State: null, Vpn: { IsAvailable: true, IsConfigured: false,
            IsConnected: false, NetworkStatus: "internet", ParticlesEnabled: true } }, "VPN read exposes bounded current page state without inventing a tunnel");
        Assert(!(await peer.Call(VpnParticles(false))).Success && !(await peer.Call("open_vpn_settings")).Success, "Read cannot mutate or launch settings");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, vpnControl: true);
        Assert((await peer.Call(VpnParticles(false))) is { Success: true, Vpn: null } && !host.VpnPage.Particles,
            "Control changes the live page and returns no protected snapshot");
        Assert((await peer.Call("open_vpn_settings")) is { Success: true, Vpn: null } && host.VpnPage.Launches == 1,
            "Control invokes the existing fixed settings operation once");
        host.VpnPage.PreferenceSaved = false;
        host.VpnPage.ErrorCode = "preference_save_failed";
        Assert((await peer.Call(VpnParticles(true))) is { Success: true, Code: "applied_not_saved", Vpn: null },
            "Control-only preference save failure reports the action outcome without leaking read state");
        host.VpnPage.PreferenceSaved = true;
        host.VpnPage.ErrorCode = null;
        Assert(!(await peer.Call("get_vpn_state")).Success && !(await peer.Call("get_state")).Success
            && !(await peer.Call("open_destination")).Success, "VPN control implies no read or unrelated control");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, vpnRead: true, vpnControl: true);
        Assert((await peer.Call(VpnParticles(true))).Vpn is { ParticlesEnabled: true }, "Combined scopes give current shared-page readback");
        host.VpnPage.PreferenceSaved = false;
        host.VpnPage.ErrorCode = "preference_save_failed";
        Assert((await peer.Call(VpnParticles(true))) is { Success: true, Code: "applied_not_saved",
            Vpn: { ParticlesEnabled: true, IsPreferenceSaved: false, LastErrorCode: "preference_save_failed" } },
            "Failed persistence is explicitly session-only in outcome and authorized snapshot");
        host.VpnPage.Loading = true;
        Assert((await peer.Call("get_vpn_state")).Vpn is { IsLoading: true }, "Reader distinguishes initial loading from unknown availability");
        host.VpnPage.Loading = false;
        foreach (var code in new[] { "preference_save_failed", "environment_unavailable", "settings_timeout", "settings_unavailable", "settings_failed", "operation_failed" })
        {
            host.VpnPage.ErrorCode = code;
            Assert((await peer.Call("get_vpn_state")).Vpn?.LastErrorCode == code, "Documented sanitized page error survives authorized reads");
        }
        host.VpnPage.ErrorCode = "private-ssid-path-credential";
        var sanitized = await peer.Call("get_vpn_state");
        Assert(sanitized.Vpn?.LastErrorCode == "operation_failed", "Undeclared page error text never crosses the VPN boundary");
        host.VpnPage.PreferenceSaved = true;
        host.VpnPage.ErrorCode = null;
        Assert((await peer.Call(VpnParticles(true))) is { Code: "ok", Vpn: { IsLoading: false, IsPreferenceSaved: true, LastErrorCode: null } },
            "A successful retry reports durable preference without stale error/loading");
        var launches = host.VpnPage.Launches;
        foreach (var invalid in new McpRequest[]
        {
            new() { Operation = "set_vpn_particles" }, new() { Operation = "set_vpn_particles", Vpn = new() },
            VpnParticles(false) with { FullScreen = true }, VpnParticles(false) with { Page = "login" },
            new() { Operation = "get_vpn_state", Vpn = new() { Enabled = true } },
            new() { Operation = "open_vpn_settings", Navigation = new() { Destination = "vpn" } }
        }) Assert((await peer.Call(invalid)).Code == "invalid_arguments", "VPN adapter rejects missing or unrelated arguments");
        Assert(host.VpnPage.Launches == launches && host.VpnPage.Particles, "Invalid requests do not reach page methods");
        host.VpnPage.Busy = true;
        Assert((await peer.Call("open_vpn_settings")).Code == "busy" && host.VpnPage.Launches == launches, "Busy page rejects duplicate launches");
        host.VpnPage.Busy = false;
        host.VpnPage.LaunchHandler = _ => Task.FromResult(false);
        Assert((await peer.Call("open_vpn_settings")) is { Code: "launch_failed", Vpn.IsConnected: false }, "OS launch failure is not a VPN connection");
        host.VpnPage.LaunchHandler = _ => throw new IOException("private-address-and-token");
        var failure = await peer.Call("open_vpn_settings");
        Assert(failure is { Code: "operation_failed", Vpn: null } && !failure.Message.Contains("private", StringComparison.Ordinal), "Errors are sanitized");
        host.VpnPage.LaunchHandler = null;
        host.VpnPage.IsActive = false;
        Assert((await peer.Call("get_vpn_state")) is { Code: "page_unavailable", Vpn: null }, "Inactive page is not activated or created");
        host.VpnPage.IsActive = true; host.VpnSession = false;
        Assert((await peer.Call(VpnParticles(false))).Code == "requires_sign_in" && host.VpnPage.Particles, "Signed-out requests cannot operate cached pages");
        host.VpnSession = true;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.VpnPage.LaunchHandler = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return true; };
        var pending = peer.Call("open_vpn_settings");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        Assert((await pending) is { Code: "cancelled", Vpn: null }, "Revocation cancels an active settings operation without readback");
        Assert(!(await peer.Call("get_vpn_state")).Success, "Revocation persists after cancellation");
    }

    private static async Task VpnLifetimeAndRestoration()
    {
        var path = Path.Combine(StorageRoot, "vpn-restoration", "grants.dat");
        var proof = Proof(); McpCredentialIdentity.TryGetId(proof, out var id);
        McpVpnService released;
        await using (var host = await Host.StartAsync(path))
        {
            released = host.Vpn;
            await using var peer = await host.ConnectAsync(proof);
            await host.Approve(id, false, false, false, vpnRead: true);
            await host.Ui(() => host.Service.IsEnabled = false);
            Assert(!(await peer.Call("get_vpn_state")).Success, "Global disable suspends VPN read");
            await host.Ui(() => host.Service.IsEnabled = true);
            Assert((await peer.Call("get_vpn_state")).Success, "Global enable restores exactly saved VPN read");
        }
        Assert((await released.ExecuteAsync(new() { Operation = "get_vpn_state" }, default)).Code == "page_unavailable", "Access disposal releases VPN adapter");
        await using (var host = await Host.StartAsync(path))
        {
            await using var peer = await host.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { IsRemembered: true, AllowVpnRead: true, AllowVpnControl: false, AllowRead: false }, "Restart restores independent VPN grants");
            Assert((await peer.Call("get_vpn_state")).Success && !(await peer.Call(VpnParticles(false))).Success, "Restored reader cannot control VPN page");
            await peer.DisposeAsync();
            await host.WaitUntil(() => !host.Item(id).IsConnected);
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        }
        await using (var host = await Host.StartAsync(path))
        {
            await using var peer = await host.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { Status: "pending", AllowVpnRead: false, AllowVpnControl: false }, "Offline VPN revocation survives restart");
        }
        using var page = new SyntheticVpnPage();
        using var adapter = new McpVpnService(() => page, () => true);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await adapter.ExecuteAsync(VpnParticles(false), cancel.Token); Assert(false, "Expected cancellation"); }
        catch (OperationCanceledException) { Assert(page.Particles, "Pre-cancelled operation changes nothing"); }
        using var duringRead = new CancellationTokenSource();
        page.OnRead = duringRead.Cancel;
        try { await adapter.ExecuteAsync(VpnParticles(false), duringRead.Token); Assert(false, "Expected cancellation"); }
        catch (OperationCanceledException) { Assert(page.Particles, "Cancellation during state lookup precedes mutation"); }
        page.OnRead = null;
        using var duringSnapshot = new CancellationTokenSource();
        page.OnRead = duringSnapshot.Cancel;
        try { await adapter.ExecuteAsync(new() { Operation = "get_vpn_state" }, duringSnapshot.Token); Assert(false, "Expected cancellation"); }
        catch (OperationCanceledException) { Assert(true, "Cancellation during read suppresses the snapshot"); }
        page.OnRead = null;
        page.LaunchHandler = _ => { page.IsActive = false; return Task.FromResult(true); };
        Assert((await adapter.ExecuteAsync(new() { Operation = "open_vpn_settings" }, default)) is { Code: "page_unavailable", Vpn: null }, "Page unloading cannot return a stale success or snapshot");
        page.IsActive = true;
        page.LaunchHandler = _ => { page.Source.Cancel(); return Task.FromResult(true); };
        try { await adapter.ExecuteAsync(new() { Operation = "open_vpn_settings" }, default); Assert(false, "Expected cancellation"); }
        catch (OperationCanceledException) { Assert(true, "Page lifetime cancels in-flight completion"); }
        using var disposedPage = new SyntheticVpnPage();
        using var disposedAdapter = new McpVpnService(() => disposedPage, () => true);
        disposedPage.LaunchHandler = _ => { disposedAdapter.Dispose(); return Task.FromResult(true); };
        try { await disposedAdapter.ExecuteAsync(new() { Operation = "open_vpn_settings" }, default); Assert(false, "Expected cancellation"); }
        catch (OperationCanceledException) { Assert(true, "Adapter disposal cancels active work without owning the page"); }
        disposedAdapter.Dispose();
        Assert(disposedPage.IsActive, "Adapter does not dispose the shared UI page");
    }

    private sealed class SyntheticVpnPage : IMcpVpnPage, IDisposable
    {
        public CancellationTokenSource Source { get; } = new();
        public CancellationToken Lifetime => Source.Token;
        public bool IsActive { get; set; } = true;
        public bool Particles { get; private set; } = true;
        public bool Busy { get; set; }
        public bool Loading { get; set; }
        public bool PreferenceSaved { get; set; } = true;
        public string? ErrorCode { get; set; }
        public int Launches { get; private set; }
        public Action? OnRead { get; set; }
        public Func<CancellationToken, Task<bool>>? LaunchHandler { get; set; }
        public McpVpnState GetState()
        {
            OnRead?.Invoke();
            return new() { IsAvailable = IsActive, IsBusy = Busy, IsLoading = Loading, IsPreferenceSaved = PreferenceSaved,
                LastErrorCode = ErrorCode, ParticlesEnabled = Particles, MotionAllowed = true, NetworkStatus = "internet" };
        }
        public void SetParticlesEnabled(bool enabled) => Particles = enabled;
        public Task<bool> OpenWindowsSettingsAsync(CancellationToken token) { Launches++; return LaunchHandler?.Invoke(token) ?? Task.FromResult(true); }
        public void Dispose() { Source.Cancel(); Source.Dispose(); }
    }
}
