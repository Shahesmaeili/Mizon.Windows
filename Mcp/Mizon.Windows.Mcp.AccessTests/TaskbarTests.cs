using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static async Task TaskbarScopesAndPinState()
    {
        await using var host = await Host.StartAsync(Path.Combine(StorageRoot, "taskbar-scopes", "grants.dat"));
        await using var peer = await host.ConnectAsync(Proof());
        var id = peer.Hello.Connection!.Id;
        Assert((await peer.Call("get_taskbar_pin_state")).Code == "approval_required"
            && !(await peer.Call("request_taskbar_pin")).Success && host.TaskbarBackend.Requests == 0,
            "Pending taskbar access never reads status or reaches the pin backend");
        await host.Approve(id, true, true, true, true, true, true, true);
        var denied = await peer.Call("get_taskbar_pin_state");
        Assert(!denied.Success && denied.TaskbarPin is null && denied.Connection is { AllowTaskbarRead: false, AllowTaskbarControl: false }
            && !(await peer.Call("request_taskbar_pin")).Success, "Auth, appearance and navigation grants do not authorize taskbar pinning");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, taskbarRead: true);
        var read = await peer.Call("get_taskbar_pin_state");
        Assert(read is { Success: true, State: null, Appearance: null, Navigation: null,
            TaskbarPin: { IsPinned: false, IsSupported: true, CanRequest: true } } && host.TaskbarBackend.Reads == 1,
            "Taskbar read uses production adapter and typed source-generated state through actual pipe");
        Assert(!(await peer.Call("request_taskbar_pin")).Success && host.TaskbarBackend.Requests == 0,
            "Read-only taskbar consent cannot request Windows confirmation");
        host.TaskbarBackend.State = new(true, true, true, false, "pinned");
        Assert((await peer.Call("get_taskbar_pin_state")).TaskbarPin is { IsPinned: true, CanRequest: false }, "External pin change is refreshed rather than inferred from a saved flag");
        host.TaskbarBackend.State = new(true, false, true, false, "not_pinned");
        Assert((await peer.Call("get_taskbar_pin_state")).TaskbarPin is { IsPinned: false, CanRequest: true }, "External unpin is visible on next status read");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, taskbarControl: true);
        var pin = await peer.Call("request_taskbar_pin");
        Assert(pin is { Success: true, TaskbarPin: null, Connection: { AllowTaskbarControl: true, AllowTaskbarRead: false } }
            && host.TaskbarBackend.Requests == 1, "Control-only request executes once without leaking read state");
        Assert(!(await peer.Call("get_taskbar_pin_state")).Success && !(await peer.Call("get_state")).Success
            && !(await peer.Call("get_navigation")).Success, "Taskbar control cannot read other scopes");
        host.TaskbarBackend.RequestHandler = _ => Task.FromResult(new TaskbarPinResult(false, "not_foreground", "Bring Mizon to the foreground.", host.TaskbarBackend.State));
        Assert((await peer.Call("request_taskbar_pin")).Code == "not_foreground", "Foreground rejection is preserved without activating or retrying automatically");
        host.TaskbarBackend.RequestHandler = _ => Task.FromResult(new TaskbarPinResult(false, "not_pinned", "Pin request was not approved.", host.TaskbarBackend.State));
        Assert(!(await peer.Call("request_taskbar_pin")).Success, "Declined Windows confirmation is not reported as success");
        host.TaskbarBackend.RequestHandler = _ => throw new IOException("synthetic-sensitive-path");
        var failure = await peer.Call("request_taskbar_pin");
        Assert(failure is { Success: false, Code: "operation_failed", TaskbarPin: null }
            && !failure.Message.Contains("synthetic-sensitive"), "Backend failure is sanitized and not retried");
    }

    private static async Task TaskbarCancellationAndRestart()
    {
        var path = Path.Combine(StorageRoot, "taskbar-lifecycle", "grants.dat");
        var proof = Proof();
        McpCredentialIdentity.TryGetId(proof, out var id);
        McpTaskbarPinService released;
        await using (var host = await Host.StartAsync(path))
        {
            released = host.Taskbar;
            await using var peer = await host.ConnectAsync(proof);
            await host.Approve(id, false, false, false, taskbarRead: true, taskbarControl: true);
            var started = Signal();
            var cancelled = Signal();
            host.TaskbarBackend.RequestHandler = async token =>
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
                throw new InvalidOperationException();
            };
            var request = peer.Call("request_taskbar_pin");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            var result = await request;
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert(result is { Code: "cancelled", TaskbarPin: null }, "Revoking taskbar consent cancels the backend operation and suppresses stale state");
            Assert((await peer.Call("request_taskbar_pin")) is { Code: "permission_denied", Connection.Status: "revoked" }, "Revoked taskbar control cannot request another prompt");
            await host.Approve(id, false, false, false, taskbarRead: true);
            await host.Ui(() => host.Service.IsEnabled = false);
            Assert((await peer.Call("get_taskbar_pin_state")).Code == "disabled", "Global disable suspends taskbar access");
            await host.Ui(() => host.Service.IsEnabled = true);
            Assert((await peer.Call("get_taskbar_pin_state")).Success, "Re-enabling restores only approved taskbar access");
        }
        Assert((await released.ExecuteAsync(new() { Operation = "get_taskbar_pin_state" }, default)).Code == "unavailable",
            "Permission service disposes taskbar adapter and makes it inert");
        await using (var restarted = await Host.StartAsync(path))
        {
            await using var peer = await restarted.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { Status: "approved", AllowTaskbarRead: true, AllowTaskbarControl: false,
                AllowRead: false, AllowControl: false, AllowNavigationRead: false, AllowAppearanceRead: false },
                "Restart restores the exact independent taskbar grant without approval escalation");
            Assert((await peer.Call("get_taskbar_pin_state")).Success && !(await peer.Call("request_taskbar_pin")).Success,
                "Remembered taskbar read remains read-only");
            await peer.DisposeAsync();
            await restarted.WaitUntil(() => !restarted.Item(id).IsConnected);
            await restarted.Ui(() => restarted.Service.RevokeAsync(restarted.Item(id)));
        }
        await using var revoked = await Host.StartAsync(path);
        await using var reconnect = await revoked.ConnectAsync(proof);
        Assert((await reconnect.Call("get_taskbar_pin_state")).Code == "approval_required", "Offline taskbar revocation survives restart");
        var backend = new SyntheticTaskbar();
        using var adapter = new McpTaskbarPinService(backend);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { await adapter.ExecuteAsync(new() { Operation = "request_taskbar_pin" }, cts.Token); Assert(false, "Pre-cancelled request must throw"); }
        catch (OperationCanceledException) { Assert(backend.Requests == 0, "Pre-cancelled request never reaches Windows backend"); }
        adapter.Dispose(); adapter.Dispose();
        Assert(backend.Requests == 0, "Adapter disposal is idempotent and does not request pinning");
    }

    private sealed class SyntheticTaskbar : ITaskbarPinService
    {
        public TaskbarPinState State { get; set; } = new(true, false, true, false, "not_pinned");
        public event EventHandler? StateChanged { add { } remove { } }
        public int Reads { get; private set; }
        public int Requests { get; private set; }
        public Func<CancellationToken, Task<TaskbarPinResult>>? RequestHandler { get; set; }
        public Task<TaskbarPinState> RefreshAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Reads++; return Task.FromResult(State);
        }
        public Task<TaskbarPinResult> RequestPinAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Requests++;
            if (RequestHandler is not null) return RequestHandler(token);
            State = new(true, true, true, false, "pinned");
            return Task.FromResult(new TaskbarPinResult(true, "pinned", "Synthetic Windows confirmation accepted.", State));
        }
    }
}
