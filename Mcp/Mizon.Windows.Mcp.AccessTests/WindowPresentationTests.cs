using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static McpRequest Fullscreen(bool enabled) => new() { Operation = "set_fullscreen", FullScreen = enabled };

    private static async Task WindowScopesAndState()
    {
        await using var host = await Host.StartAsync(Path.Combine(StorageRoot, "window-scopes", "grants.dat"));
        await using var peer = await host.ConnectAsync(Proof());
        var id = peer.Hello.Connection!.Id;
        Assert((await peer.Call("get_window_state")) is { Code: "approval_required", Window: null }
            && !(await peer.Call(Fullscreen(true))).Success && host.WindowBackend.Writes == 0,
            "Pending window tools cannot read or change the window");
        await host.Approve(id, true, true, true, true, true, true, true, true, true, true, true, true, true);
        Assert(!(await peer.Call("get_window_state")).Success && !(await peer.Call(Fullscreen(true))).Success,
            "Every older scope including appearance and music grants no window access");
        host.Auth.Handler = (_, _) => Task.FromResult(new McpResponse { Success = true, Window = new() { IsAvailable = true } });
        Assert((await peer.Call("get_state")).Window is null, "Window state is redacted even if another adapter accidentally supplies it");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, windowRead: true);
        Assert((await peer.Call("get_window_state")) is { Success: true, State: null, Appearance: null,
            Window: { IsAvailable: true, FullScreen: false, ControlsVisible: false } }, "Window-only reader receives a bounded current snapshot");
        Assert(!(await peer.Call(Fullscreen(true))).Success && host.WindowBackend.Writes == 0, "Window read does not authorize fullscreen changes");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, windowControl: true);
        Assert((await peer.Call(Fullscreen(true))) is { Success: true, Window: null, State: null }
            && host.WindowBackend.IsFullScreen && host.WindowBackend.Writes == 1, "Control-only fullscreen uses shared host and redacts state");
        Assert(!(await peer.Call("get_window_state")).Success && !(await peer.Call("get_appearance")).Success
            && !(await peer.Call("get_presence_state")).Success, "Window control authorizes no read scope");
        await peer.Call(Fullscreen(true));
        Assert(host.WindowBackend.Writes == 1, "Explicit fullscreen state is idempotent through the real host operation");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, windowRead: true, windowControl: true);
        host.WindowBackend.ControlsVisible = true;
        Assert((await peer.Call("get_window_state")).Window is { FullScreen: true, ControlsVisible: true },
            "Control visibility is read from the current host, not guessed from fullscreen alone");
        Assert((await peer.Call(Fullscreen(false))) is { Success: true, Window.FullScreen: false }, "Exit fullscreen returns live readback with both scopes");
        var writes = host.WindowBackend.Writes;
        foreach (var invalid in new McpRequest[]
        {
            new() { Operation = "set_fullscreen" },
            new() { Operation = "set_fullscreen", FullScreen = true, Page = "login" },
            new() { Operation = "get_window_state", FullScreen = true },
        }) Assert((await peer.Call(invalid)).Code == "invalid_arguments", "Window adapter rejects missing or unrelated payload fields");
        Assert(host.WindowBackend.Writes == writes, "Rejected payloads do not reach the window operation");
        host.WindowBackend.OnSet = () => throw new IOException("private-handle-location-token");
        var failed = await peer.Call(Fullscreen(true));
        Assert(failed is { Code: "operation_failed", Window: null } && !failed.Message.Contains("private", StringComparison.Ordinal),
            "Window failures are sanitized without leaking cached snapshots");
        host.WindowBackend.OnSet = null;
        host.WindowBackend.Available = false;
        Assert((await peer.Call("get_window_state")) is { Code: "unavailable", Window.IsAvailable: false }
            && (await peer.Call(Fullscreen(true))).Code == "unavailable", "Closed window cannot be read as live or mutated");
        host.WindowBackend.Available = true;
        Task? revocation = null;
        host.WindowBackend.OnAvailable = () =>
        {
            host.WindowBackend.OnAvailable = null;
            revocation = host.Service.RevokeAsync(host.Item(id));
        };
        Assert((await peer.Call(Fullscreen(true))) is { Code: "cancelled", Window: null }
            && host.WindowBackend.Writes == writes, "Revocation during host lookup cancels before window mutation");
        if (revocation is not null) await revocation;
        Assert(!(await peer.Call("get_window_state")).Success, "Revocation remains enforced after the interrupted request");
    }

    private static async Task WindowLifetimeAndRestoration()
    {
        var path = Path.Combine(StorageRoot, "window-restoration", "grants.dat");
        var proof = Proof(); McpCredentialIdentity.TryGetId(proof, out var id);
        McpWindowPresentationService released;
        await using (var host = await Host.StartAsync(path))
        {
            released = host.Window;
            await using var peer = await host.ConnectAsync(proof);
            await host.Approve(id, false, false, false, windowRead: true);
            await host.Ui(() => host.Service.IsEnabled = false);
            Assert(!(await peer.Call("get_window_state")).Success, "Global disable suspends the new window domain");
            await host.Ui(() => host.Service.IsEnabled = true);
            Assert((await peer.Call("get_window_state")).Success, "Global re-enable restores only the exact window scope");
        }
        Assert((await released.ExecuteAsync(new() { Operation = "get_window_state" }, default)).Code == "unavailable",
            "Access-service disposal releases the adapter and cannot read a closed host");
        await using (var restarted = await Host.StartAsync(path))
        {
            await using var peer = await restarted.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { IsRemembered: true, AllowWindowRead: true, AllowWindowControl: false,
                AllowAppearanceRead: false, AllowRead: false }, "Restart restores the exact independent window read grant");
            Assert((await peer.Call("get_window_state")).Success && !(await peer.Call(Fullscreen(true))).Success,
                "Restored window reader remains read-only");
            await peer.DisposeAsync();
            await restarted.WaitUntil(() => !restarted.Item(id).IsConnected);
            await restarted.Ui(() => restarted.Service.RevokeAsync(restarted.Item(id)));
        }
        await using (var revoked = await Host.StartAsync(path))
        {
            await using var peer = await revoked.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { Status: "pending", AllowWindowRead: false, AllowWindowControl: false },
                "Offline window revocation survives restart");
        }
        var window = new SyntheticWindow();
        using var adapter = new McpWindowPresentationService(() => window);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await adapter.ExecuteAsync(Fullscreen(true), canceled.Token); Assert(false, "Expected cancellation"); }
        catch (OperationCanceledException) { Assert(window.Writes == 0, "Pre-canceled fullscreen does not mutate the host"); }
        using var duringRead = new CancellationTokenSource();
        window.OnAvailable = duringRead.Cancel;
        try { await adapter.ExecuteAsync(Fullscreen(true), duringRead.Token); Assert(false, "Expected cancellation"); }
        catch (OperationCanceledException) { Assert(window.Writes == 0, "Cancellation during availability check prevents mutation"); }
        window.OnAvailable = adapter.Dispose;
        Assert((await adapter.ExecuteAsync(Fullscreen(true), default)).Code == "unavailable" && window.Writes == 0,
            "Disposed adapter during host lookup cannot mutate the window");
        adapter.Dispose(); adapter.Dispose();
        using var absent = new McpWindowPresentationService(() => null);
        Assert((await absent.ExecuteAsync(new() { Operation = "get_window_state" }, default)).Window is { IsAvailable: false },
            "Missing main window has an explicit unavailable snapshot");
    }

    private sealed class SyntheticWindow : IWindowPresentationHost
    {
        public bool Available { get; set; } = true;
        public bool IsAvailable { get { OnAvailable?.Invoke(); return Available; } }
        public bool IsFullScreen { get; private set; }
        public bool ControlsVisible { get; set; }
        public bool IsFullScreenControlsVisible => ControlsVisible && IsFullScreen;
        public int Writes { get; private set; }
        public Action? OnAvailable { get; set; }
        public Action? OnSet { get; set; }
        public void SetFullScreen(bool enabled)
        {
            OnSet?.Invoke();
            if (IsFullScreen == enabled) return;
            IsFullScreen = enabled; Writes++;
        }
    }
}
