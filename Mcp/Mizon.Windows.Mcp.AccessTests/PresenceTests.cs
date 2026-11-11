using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static async Task PresenceScopesAndState()
    {
        await using var host = await Host.StartAsync(Path.Combine(StorageRoot, "presence-scopes", "grants.dat"));
        await using var peer = await host.ConnectAsync(Proof());
        var id = peer.Hello.Connection!.Id;
        Assert((await peer.Call("get_presence_state")) is { Code: "approval_required", Presence: null }
            && host.PresenceBackend.Reads == 0, "Pending presence read never reaches the shared presence service");
        await host.Approve(id, true, true, true, true, true, true, true, true, true, true, true, true);
        Assert(!(await peer.Call("get_presence_state")).Success && host.PresenceBackend.Reads == 0,
            "All older permission groups still grant no presence access");
        host.Auth.Handler = (_, _) => Task.FromResult(new McpResponse
        {
            Success = true, Presence = new() { IsOnline = true, HasConfirmedState = true }
        });
        Assert((await peer.Call("get_state")).Presence is null,
            "Presence is independently redacted even if another adapter includes a snapshot");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, presenceRead: true);
        var states = new ProfilePresenceState[]
        {
            new(true, true, true, true, true),
            new(true, false, true, false, true),
            new(true, true, false, false, false),
            new(true, true, true, false, false),
            new(false, false, false, false, false)
        };
        foreach (var expected in states)
        {
            host.PresenceBackend.State = expected;
            var response = await peer.Call("get_presence_state");
            Assert(response is { Success: true, Code: "presence_state", State: null, MusicHome: null }
                && response.Presence is { } actual
                && actual.HasSession == expected.HasSession && actual.IsWindowActive == expected.IsWindowActive
                && actual.IsConnected == expected.IsConnected && actual.IsOnline == expected.IsOnline
                && actual.HasConfirmedState == expected.HasConfirmedState,
                "Presence read preserves real active/inactive/disconnected/pending/signed-out state without publishing");
        }
        Assert(!(await peer.Call("get_state")).Success && !(await peer.Call("get_music_home")).Success
            && !(await peer.Call("set_presence")).Success && !(await peer.Call("activate_window")).Success,
            "Presence consent cannot read another domain or manufacture online status/focus");
        var reads = host.PresenceBackend.Reads;
        Assert((await peer.Call(new McpRequest { Operation = "get_presence_state", Page = "login" })).Code == "invalid_request"
            && host.PresenceBackend.Reads == reads, "Presence does not accept mutation/navigation payloads");
        host.PresenceBackend.OnRead = () => throw new IOException("private-session-token-and-cache-path");
        var failed = await peer.Call("get_presence_state");
        Assert(failed is { Code: "operation_failed", Presence: null }
            && !failed.Message.Contains("private-session", StringComparison.Ordinal), "Presence failure returns no data or raw exception text");
        host.PresenceBackend.OnRead = () => throw new ObjectDisposedException("private-presence-owner");
        Assert((await peer.Call("get_presence_state")) is { Code: "unavailable", Presence: null },
            "Closed shared presence service is unavailable rather than a cached successful snapshot");
        host.PresenceBackend.OnRead = null;
        Assert((await peer.Call("get_presence_state")).Success, "A failed read leaves the presence adapter usable");
        await host.Ui(() => host.Service.IsEnabled = false);
        reads = host.PresenceBackend.Reads;
        Assert(!(await peer.Call("get_presence_state")).Success && host.PresenceBackend.Reads == reads,
            "Globally disabling MCP blocks presence without touching the backend");
        await host.Ui(() => host.Service.IsEnabled = true);
        Assert((await peer.Call("get_presence_state")).Success, "Re-enabling MCP restores only the saved presence scope");
        Task? revocation = null;
        host.PresenceBackend.OnRead = () => revocation = host.Service.RevokeAsync(host.Item(id));
        var revokedRead = await peer.Call("get_presence_state");
        if (revocation is not null) await revocation;
        Assert(revokedRead is { Code: "cancelled", Presence: null },
            "Revoking consent during a snapshot suppresses the result from the old grant");
        host.PresenceBackend.OnRead = null;
        Assert(!(await peer.Call("get_presence_state")).Success, "Revoked presence remains inaccessible");
    }

    private static async Task PresenceLifetimeAndRestoration()
    {
        var path = Path.Combine(StorageRoot, "presence-restart", "grants.dat");
        var proof = Proof(); McpCredentialIdentity.TryGetId(proof, out var id);
        McpPresenceService released;
        SyntheticPresence backend;
        await using (var host = await Host.StartAsync(path))
        {
            released = host.Presence;
            backend = host.PresenceBackend;
            await using var peer = await host.ConnectAsync(proof);
            await host.Approve(id, false, false, false, presenceRead: true);
        }
        Assert((await released.ExecuteAsync(new() { Operation = "get_presence_state" }, default)).Code == "unavailable"
            && !backend.IsDisposed, "Host disposal releases the adapter without disposing shared presence ownership");
        await using (var restarted = await Host.StartAsync(path))
        {
            await using var peer = await restarted.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { IsRemembered: true, AllowPresenceRead: true, AllowRead: false,
                AllowControl: false, AllowMusicHomeRead: false }, "Restart restores the exact independent presence grant");
            Assert((await peer.Call("get_presence_state")).Success, "Presence read works after restart without new synthetic approval");
            await peer.DisposeAsync();
            await restarted.WaitUntil(() => !restarted.Item(id).IsConnected);
            await restarted.Ui(() => restarted.Service.RevokeAsync(restarted.Item(id)));
        }
        await using (var restarted = await Host.StartAsync(path))
        {
            await using var peer = await restarted.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { Status: "pending", AllowPresenceRead: false },
                "Offline revocation removes presence access across restart");
        }
        var source = new SyntheticPresence();
        using var adapter = new McpPresenceService(source);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await adapter.ExecuteAsync(new() { Operation = "get_presence_state" }, cancelled.Token); Assert(false, "Pre-cancellation must throw"); }
        catch (OperationCanceledException) { Assert(source.Reads == 0, "Pre-cancelled presence requests never read shared state"); }
        using var duringRead = new CancellationTokenSource();
        source.OnRead = duringRead.Cancel;
        try { await adapter.ExecuteAsync(new() { Operation = "get_presence_state" }, duringRead.Token); Assert(false, "Cancellation during read must throw"); }
        catch (OperationCanceledException) { Assert(source.Reads == 1, "Cancellation is checked again before presence leaves the adapter"); }
        source.OnRead = adapter.Dispose;
        Assert((await adapter.ExecuteAsync(new() { Operation = "get_presence_state" }, default)).Code == "unavailable",
            "Disposal during a snapshot cannot return stale presence");
        adapter.Dispose(); adapter.Dispose();
        Assert(!source.IsDisposed, "Adapter disposal is idempotent and does not stop the app's shared presence service");
    }

    private sealed class SyntheticPresence : IProfilePresenceService, IDisposable
    {
        public ProfilePresenceState State { get; set; } = new(true, true, true, true, true);
        public Action? OnRead { get; set; }
        public int Reads { get; private set; }
        public bool IsDisposed { get; private set; }
        public ProfilePresenceState ReadState() { Reads++; OnRead?.Invoke(); return State; }
        public void Dispose() => IsDisposed = true;
    }
}
