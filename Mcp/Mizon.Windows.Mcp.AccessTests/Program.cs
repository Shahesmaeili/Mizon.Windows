using System.IO.Pipes;
using System.Security.Cryptography;
using Microsoft.UI.Dispatching;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;

internal static partial class Program
{
    internal static string PipeSuffix { get; private set; } = "AccessTests." + Guid.NewGuid().ToString("N");
    internal static string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "Mizon.Mcp.AccessTests", Guid.NewGuid().ToString("N"));
    private static int _checks;

    private static async Task Main()
    {
        Directory.CreateDirectory(StorageRoot);
        try
        {
            await ApprovalRestartAndOfflineRevoke();
            await RevocationCancelsEveryWorker();
            await FailedApprovalStaysPending();
            await FailedRevocationStopsLiveAccess();
            await LegacyConnectionDoesNotPersist();
            await AppearanceScopesAndState();
            await AppearanceRevocationAndRestart();
            await NavigationScopesAndRoutes();
            await NavigationLifecycle();
            await NavigationResultBehavior();
            await TaskbarScopesAndPinState();
            await TaskbarCancellationAndRestart();
            await MusicHomeScopesAndBehavior();
            await MusicHomeLifetimeAndRestoration();
            await PresenceScopesAndState();
            await PresenceLifetimeAndRestoration();
            await VpnScopesAndState();
            await VpnLifetimeAndRestoration();
            await WindowScopesAndState();
            await WindowLifetimeAndRestoration();
            WindowPresentationTransitions();
            Console.WriteLine($"PASS {_checks} service assertions; production service/VM, synthetic consent, isolated pipes/store. No real app, UI or account accessed.");
        }
        finally
        {
            var root = Path.GetFullPath(StorageRoot);
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Mizon.Mcp.AccessTests"));
            if (Path.GetDirectoryName(root) != parent || !Guid.TryParseExact(Path.GetFileName(root), "N", out _)) throw new InvalidOperationException("Unexpected test root.");
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ApprovalRestartAndOfflineRevoke()
    {
        var path = Path.Combine(StorageRoot, "restart", "grants.dat");
        var proof = Proof();
        McpCredentialIdentity.TryGetId(proof, out var id);
        await using (var host = await Host.StartAsync(path))
        {
            await using var first = await host.ConnectAsync(proof);
            Assert(first.Hello.Connection is { Status: "pending", IsRemembered: false }, "Fresh identity starts pending");
            Assert((await first.Call("get_state")).State is null, "Pending identity cannot read private state");
            await host.Approve(id, read: true, control: false, submit: false);
            Assert((await first.Call("get_state")).Success, "Explicit synthetic user approval allows requested read");
            Assert(!(await first.Call("submit")).Success, "Read approval cannot submit forms");
            await using var second = await host.ConnectAsync(proof);
            Assert(second.Hello.Connection is { Status: "approved", IsRemembered: true }, "Another worker restores the same proof's saved approval");
            Assert(await host.Ui(() => host.Service.Connections.Count) == 1, "Parallel workers share one visible client entry");
            await using var impostor = await host.ConnectAsync(Proof());
            Assert(impostor.Hello.Connection is { Status: "pending", AllowRead: false }, "Matching client name alone cannot restore another proof's grant");

            var started = Signal(); var finish = Signal();
            host.Auth.Handler = async (_, _) => { started.TrySetResult(); await finish.Task; return new() { Success = true, State = new() { Page = "login" } }; };
            var reading = second.Call("get_state");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await first.DisposeAsync();
            finish.TrySetResult();
            Assert((await reading).Success, "Disconnecting one worker preserves the other worker's in-flight read");
        }

        await using (var restarted = await Host.StartAsync(path))
        {
            await restarted.WaitUntil(() => restarted.Service.Connections.Any(item => item.Id == id));
            Assert(await restarted.Ui(() => restarted.Item(id) is { IsConnected: false, IsRemembered: true, CanRevoke: true }), "Saved approval is visible and revocable before reconnecting");
            await using var peer = await restarted.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { Status: "approved", AllowRead: true, AllowControl: false, AllowSubmit: false }, "App restart restores exact scopes without another approval");
            await peer.DisposeAsync();
            await restarted.WaitUntil(() => !restarted.Item(id).IsConnected);
            await restarted.Ui(() => restarted.Service.RevokeAsync(restarted.Item(id)));
            Assert(await restarted.Ui(() => !restarted.Item(id).IsRemembered), "Offline revocation removes remembered permission");
        }
        await using (var restarted = await Host.StartAsync(path))
        {
            await using var peer = await restarted.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { Status: "pending", IsRemembered: false, AllowRead: false }, "Revocation survives app restart and blocks old proof");
        }
    }

    private static async Task RevocationCancelsEveryWorker()
    {
        var path = Path.Combine(StorageRoot, "revoke", "grants.dat");
        var proof = Proof();
        McpCredentialIdentity.TryGetId(proof, out var id);
        await using var host = await Host.StartAsync(path);
        await using var first = await host.ConnectAsync(proof);
        await host.Approve(id, true, true, false);
        await using var second = await host.ConnectAsync(proof);
        var started = Signal(); var finish = Signal();
        var count = 0;
        host.Auth.Handler = async (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 2) started.TrySetResult();
            await finish.Task; // Deliberately ignores cancellation: service must reject stale result.
            return new() { Success = true, State = new() { Identifier = "old-grant-data" } };
        };
        var reading1 = first.Call("get_state"); var reading2 = second.Call("get_state");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, true, false, false);
        finish.TrySetResult();
        var replies = await Task.WhenAll(reading1, reading2);
        Assert(replies.All(reply => reply.Code == "cancelled" && reply.State is null), "Revoke/reapprove invalidates every old worker's in-flight result");
        host.Auth.Handler = (_, _) => Task.FromResult(new McpResponse { Success = true, State = new() { Page = "login" } });
        Assert((await first.Call("get_state")).Success && (await second.Call("get_state")).Success, "Both workers use the newly approved generation");
        Assert(!(await second.Call("set_form")).Success, "Reapproval did not inherit removed control scope");

        await host.Ui(() => host.Service.IsEnabled = false);
        Assert((await first.Call("get_state")).Code == "disabled", "Global disable blocks an approved identity");
        Assert(await host.Ui(() => host.Item(id).IsRemembered), "Global disable retains explicit saved permission");
        await host.Ui(() => host.Service.IsEnabled = true);
        Assert((await second.Call("get_state")).Success && !(await first.Call("set_form")).Success, "Re-enable restores exact prior scopes");
        await host.Ui(() => host.Service.RevokeAllAsync());
        Assert(!(await first.Call("get_state")).Success && !(await second.Call("get_state")).Success, "Revoke-all blocks every live worker");
        using var persisted = new McpRememberedGrantStore(path);
        Assert((await persisted.ReadAsync()).Count == 0, "Revoke-all durably clears all remembered grants");
    }

    private static async Task FailedApprovalStaysPending()
    {
        var path = Path.Combine(StorageRoot, "failure", "grants.dat");
        await using var host = await Host.StartAsync(path);
        var proof = Proof();
        McpCredentialIdentity.TryGetId(proof, out var id);
        await using var peer = await host.ConnectAsync(proof);
        await File.WriteAllTextAsync(path, "invalid synthetic store");
        await host.Approve(id, true, true, false);
        Assert(!(await peer.Call("get_state")).Success && !await host.Ui(() => host.Item(id).IsRemembered), "Failed approval save never grants access or reports durable approval");
        Assert(await host.Ui(() => host.Service.HasStorageError), "Failed approval is visible as a storage error");
    }

    private static async Task FailedRevocationStopsLiveAccess()
    {
        var path = Path.Combine(StorageRoot, "revoke-failure", "grants.dat");
        await using var host = await Host.StartAsync(path);
        var proof = Proof();
        McpCredentialIdentity.TryGetId(proof, out var id);
        await using var peer = await host.ConnectAsync(proof);
        await host.Approve(id, true, true, false);
        await File.WriteAllTextAsync(path, "invalid synthetic grant store");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        Assert(!(await peer.Call("get_state")).Success, "Failed durable revoke still blocks current access immediately");
        Assert(await host.Ui(() => host.Service.HasStorageError && host.Item(id).IsRemembered && host.Item(id).CanRevoke), "Failed durable revoke remains visible and retryable instead of claiming success");
        await using var worker = await host.ConnectAsync(proof);
        Assert(worker.Hello.Connection?.Status != "approved", "A reconnect cannot revive an unsuccessfully persisted revocation in the running app");
        await host.Ui(() => host.Service.RevokeAllAsync());
        Assert(await host.Ui(() => !host.Service.HasStorageError && !host.Item(id).IsRemembered), "Explicit revoke-all safely recovers corrupt storage and clears errors");
    }

    private static async Task LegacyConnectionDoesNotPersist()
    {
        var path = Path.Combine(StorageRoot, "legacy", "grants.dat");
        await using var host = await Host.StartAsync(path);
        await using var peer = await host.ConnectAsync(null);
        var id = peer.Hello.Connection!.Id;
        await host.Approve(id, true, false, false);
        Assert((await peer.Call("get_state")).Success, "Legacy connection can receive explicit session approval");
        Assert(!await host.Ui(() => host.Item(id).IsRemembered), "A missing proof is never persisted by name");
        await peer.DisposeAsync();
        await host.WaitUntil(() => !host.Item(id).IsConnected);
        await using var next = await host.ConnectAsync(null);
        Assert(next.Hello.Connection is { Status: "pending", AllowRead: false }, "Legacy reconnect requires approval without proof");
    }

    private static string Proof() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; Console.WriteLine("PASS " + message); }

    private sealed class Host : IAsyncDisposable
    {
        private readonly DispatcherQueue _ui = new();
        public McpAccessService Service { get; }
        public McpAuthenticationService Auth { get; } = new();
        public AppearanceSettingsService Preferences { get; } = new();
        public SyntheticAppearanceService Appearance { get; }
        public SyntheticNavigation NavigationBackend { get; } = new();
        public McpNavigationService Navigation { get; }
        public SyntheticTaskbar TaskbarBackend { get; } = new();
        public McpTaskbarPinService Taskbar { get; }
        public SyntheticMusicHomePage MusicPage { get; } = new();
        public bool MusicSession { get; set; } = true;
        public McpMusicHomeService MusicHome { get; }
        public SyntheticPresence PresenceBackend { get; } = new();
        public McpPresenceService Presence { get; }
        public SyntheticWindow WindowBackend { get; } = new();
        public McpWindowPresentationService Window { get; }
        public SyntheticVpnPage VpnPage { get; } = new();
        public bool VpnSession { get; set; } = true;
        public McpVpnService Vpn { get; }
        private readonly string _pipe;
        private Host(string path)
        {
            PipeSuffix = "AccessTests." + Guid.NewGuid().ToString("N");
            _pipe = McpPipeProtocol.PipePrefix + PipeSuffix;
            Service = new(new McpRememberedGrantStore(path));
            Appearance = new(Preferences);
            Navigation = new(NavigationBackend);
            Taskbar = new(TaskbarBackend);
            MusicHome = new(() => MusicPage.IsActive ? MusicPage : null, () => MusicSession);
            Presence = new(PresenceBackend);
            Window = new(() => WindowBackend);
            Vpn = new(() => VpnPage.IsActive ? VpnPage : null, () => VpnSession);
        }
        public static async Task<Host> StartAsync(string path)
        {
            var host = new Host(path);
            await host.Ui(() => host.Service.Start(host._ui, host.Auth, host.Appearance, host.Navigation, host.Taskbar, host.MusicHome, host.Presence, host.Window, host.Vpn));
            return host;
        }
        public McpConnectionItemViewModel Item(string id) => Service.Connections.Single(item => item.Id == id);
        public Task Approve(string id, bool read, bool control, bool submit, bool appearanceRead = false, bool appearanceControl = false,
            bool navigationRead = false, bool navigationControl = false, bool taskbarRead = false, bool taskbarControl = false,
            bool musicRead = false, bool musicControl = false, bool musicSubmit = false, bool presenceRead = false,
            bool windowRead = false, bool windowControl = false, bool vpnRead = false, bool vpnControl = false) => Ui(async () =>
        {
            var item = Item(id); item.AllowRead = read; item.AllowControl = control; item.AllowSubmit = submit;
            item.AllowAppearanceRead = appearanceRead; item.AllowAppearanceControl = appearanceControl;
            item.AllowNavigationRead = navigationRead; item.AllowNavigationControl = navigationControl;
            item.AllowTaskbarRead = taskbarRead; item.AllowTaskbarControl = taskbarControl;
            item.AllowMusicHomeRead = musicRead; item.AllowMusicHomeControl = musicControl; item.AllowMusicHomeSubmit = musicSubmit;
            item.AllowPresenceRead = presenceRead;
            item.AllowWindowRead = windowRead; item.AllowWindowControl = windowControl;
            item.AllowVpnRead = vpnRead; item.AllowVpnControl = vpnControl;
            await Service.ApproveAsync(item);
        });
        public async Task<Peer> ConnectAsync(string? proof)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(deadline.Token);
                await McpPipeProtocol.WriteAsync(pipe, new McpRequest { Operation = "connect", ClientName = "Codex", ClientCredential = proof }, McpJsonContext.Default.McpRequest, deadline.Token);
                var hello = await McpPipeProtocol.ReadAsync(pipe, McpJsonContext.Default.McpResponse, deadline.Token) ?? throw new IOException("Missing handshake.");
                return new(pipe, hello);
            }
            catch { pipe.Dispose(); throw; }
        }
        public Task Ui(Action action) => Ui(() => { action(); return Task.CompletedTask; });
        public Task<T> Ui<T>(Func<T> action) => Ui(() => Task.FromResult(action()));
        public async Task Ui(Func<Task> action) => await Ui(async () => { await action(); return true; });
        public Task<T> Ui<T>(Func<Task<T>> action)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ui.TryEnqueue(async () => { try { completion.TrySetResult(await action()); } catch (Exception error) { completion.TrySetException(error); } });
            return completion.Task;
        }
        public async Task WaitUntil(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (!await Ui(predicate)) await Task.Delay(20, timeout.Token);
        }
        public async ValueTask DisposeAsync()
        {
            await Ui(() => Service.DisposeAsync().AsTask());
            VpnPage.Dispose();
            _ui.Dispose();
        }
    }

    private sealed class Peer(NamedPipeClientStream pipe, McpResponse hello) : IAsyncDisposable
    {
        public McpResponse Hello { get; } = hello;
        public Task<McpResponse> Call(string operation) => Call(new McpRequest { Operation = operation });
        public async Task<McpResponse> Call(McpRequest request)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await McpPipeProtocol.WriteAsync(pipe, request, McpJsonContext.Default.McpRequest, deadline.Token);
            return await McpPipeProtocol.ReadAsync(pipe, McpJsonContext.Default.McpResponse, deadline.Token) ?? throw new IOException("Missing reply.");
        }
        public ValueTask DisposeAsync() { pipe.Dispose(); return ValueTask.CompletedTask; }
    }
}
