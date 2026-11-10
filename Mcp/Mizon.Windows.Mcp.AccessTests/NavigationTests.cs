using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static McpRequest Navigate(string destination, Guid? id = null, Guid? comment = null,
        string? query = null, string? section = null) => new()
    {
        Operation = "open_destination", Navigation = new()
        { Destination = destination, PublicId = id, CommentPublicId = comment, SearchQuery = query, SettingsSection = section }
    };

    private static async Task NavigationScopesAndRoutes()
    {
        await using var host = await Host.StartAsync(Path.Combine(StorageRoot, "navigation-scopes", "grants.dat"));
        await using var peer = await host.ConnectAsync(Proof());
        var id = peer.Hello.Connection!.Id;
        Assert((await peer.Call("get_navigation")).Code == "approval_required", "Pending identity cannot read a current destination");
        Assert(!(await peer.Call(Navigate("music"))).Success && host.NavigationBackend.Opens == 0,
            "Pending navigation never reaches the app adapter");
        await host.Approve(id, true, true, true, appearanceRead: true, appearanceControl: true);
        var old = await peer.Call("get_navigation");
        Assert(!old.Success && old.Navigation is null && old.Connection is { AllowNavigationRead: false, AllowNavigationControl: false },
            "Existing authentication and appearance grants never authorize navigation");
        Assert(!(await peer.Call(Navigate("music"))).Success && host.NavigationBackend.Opens == 0,
            "Existing control permissions cannot open a content page");

        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, navigationRead: true);
        var read = await peer.Call("get_navigation");
        Assert(read is { Success: true, State: null, Appearance: null, Navigation: { Destination: "home", Uri: "mizon://home", HasSession: true } },
            "Navigation reader receives only typed location state through app pipe and dispatcher");
        Assert(!(await peer.Call(Navigate("music"))).Success && !(await peer.Call("get_state")).Success
            && !(await peer.Call("get_appearance")).Success, "Navigation read cannot navigate or access other domains");

        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, navigationControl: true);
        var write = await peer.Call(Navigate("music"));
        Assert(write is { Success: true, State: null, Appearance: null, Navigation: null }
            && host.NavigationBackend.Last is { Destination: "music" }, "Control-only navigation opens declared page without disclosing state");
        Assert(!(await peer.Call("get_navigation")).Success && !(await peer.Call("set_appearance")).Success
            && !(await peer.Call("submit")).Success, "Navigation control cannot read location, edit appearance or submit credentials");

        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, navigationRead: true, navigationControl: true);
        var musicId = Guid.NewGuid(); var commentId = Guid.NewGuid();
        var comment = await peer.Call(Navigate("music-comment", musicId, commentId));
        Assert(comment.Success && comment.Navigation?.Uri == $"mizon://music/{musicId:D}/comments/{commentId:D}"
            && host.NavigationBackend.Last is { MusicAction: MusicActivationAction.Details, MusicCommentPublicId: var actualComment }
            && actualComment == commentId, "Comment navigation uses production route builder and never starts playback");
        var query = "آهنگ & + #";
        var search = await peer.Call(Navigate("search", query: query));
        Assert(search.Success && AppActivationRequest.TryParse(search.Navigation?.Uri, out var parsed) && parsed.SearchQuery == query,
            "Unicode search uses the same encoded canonical deep link as protocol activation");
        var settings = await peer.Call(Navigate("settings", section: "ai-connections"));
        Assert(settings.Success && settings.Navigation?.Uri == "mizon://settings/ai-connections",
            "Settings navigation selects a declared section without changing permissions");

        var opens = host.NavigationBackend.Opens;
        foreach (var invalid in new[]
        {
            Navigate("music-play", musicId), Navigate("music-download", musicId),
            Navigate("MUSIC-PLAY", musicId), Navigate("MUSIC-DOWNLOAD", musicId),
            Navigate("https://example.com"), Navigate("music-details"), Navigate("client", Guid.Empty),
            Navigate("music", musicId), Navigate("settings", section: "execute-shell"),
            Navigate("search", query: "bad\nquery"), Navigate("search", query: new string('x', 121))
        })
            Assert((await peer.Call(invalid)).Code == "invalid_destination" && host.NavigationBackend.Opens == opens,
                "Production adapter rejects unsupported navigation before any UI operation");

        await host.Ui(() => host.NavigationBackend.State = new("login", "mizon://login", null, false, false));
        var signIn = await peer.Call(Navigate("story", Guid.NewGuid()));
        Assert(signIn.Code == "requires_sign_in" && host.NavigationBackend.Opens == opens
            && host.NavigationBackend.State.PendingUri is null, "Signed-out content request never queues future navigation under a later consent generation");
        Assert((await peer.Call(Navigate("register"))).Success && host.NavigationBackend.State.Uri == "mizon://register",
            "Allowed account navigation works while signed out without submitting the form");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        opens = host.NavigationBackend.Opens;
        try { await host.Navigation.ExecuteAsync(Navigate("login"), cancelled.Token); throw new InvalidOperationException("Canceled navigation executed."); }
        catch (OperationCanceledException) { }
        Assert(host.NavigationBackend.Opens == opens, "Canceled adapter call does not navigate");
        await host.Ui(() => host.NavigationBackend.Handler = (_, _) => throw new IOException("synthetic-private-failure"));
        var failed = await peer.Call(Navigate("login"));
        Assert(failed.Code == "operation_failed" && failed.Navigation is null && !failed.Message.Contains("synthetic-private", StringComparison.Ordinal),
            "Navigation failures are sanitized and not retried");
        await host.Ui(() => host.NavigationBackend.Handler = (_, _) => throw new TimeoutException());
        Assert((await peer.Call(Navigate("login"))).Code == "page_not_ready", "Page readiness timeout has an explicit outcome");
    }

    private static async Task NavigationLifecycle()
    {
        var path = Path.Combine(StorageRoot, "navigation-lifecycle", "grants.dat");
        var proof = Proof(); McpCredentialIdentity.TryGetId(proof, out var id);
        SyntheticNavigation released;
        await using (var host = await Host.StartAsync(path))
        {
            released = host.NavigationBackend;
            await using var first = await host.ConnectAsync(proof);
            await using var second = await host.ConnectAsync(proof);
            await host.Approve(id, false, false, false, navigationRead: true, navigationControl: true);
            var entered = Signal(); var canceled = Signal();
            host.NavigationBackend.Handler = async (_, token) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
                return new(true, "opened", "Synthetic");
            };
            var active = first.Call(Navigate("music"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert((await active).Code == "cancelled" && host.NavigationBackend.Opens == 0,
                "Revocation cancels pending navigation before mutation");
            Assert(!(await second.Call("get_navigation")).Success, "Revocation blocks all same-identity navigation workers");
            host.NavigationBackend.Handler = null;
            await host.Approve(id, false, false, false, navigationRead: true);
            Assert((await second.Call("get_navigation")).Success && !(await first.Call(Navigate("music"))).Success,
                "Reapproval restores only selected navigation scopes");
            await host.Ui(() => host.Service.IsEnabled = false);
            Assert((await second.Call("get_navigation")).Code == "disabled", "Global disable suspends navigation");
            await host.Ui(() => host.Service.IsEnabled = true);
            Assert((await second.Call("get_navigation")).Success, "Re-enable preserves exact navigation approval");
        }
        Assert(released.Disposed, "Production permission service releases navigation adapter on shutdown");
        await using (var restarted = await Host.StartAsync(path))
        {
            await using var peer = await restarted.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { Status: "approved", AllowNavigationRead: true, AllowNavigationControl: false,
                AllowRead: false, AllowControl: false, AllowAppearanceRead: false, AllowAppearanceControl: false },
                "App restart restores only the saved navigation read scope without another approval");
            Assert((await peer.Call("get_navigation")).Success && !(await peer.Call(Navigate("music"))).Success,
                "Restored navigation reader remains read-only");
            await peer.DisposeAsync();
            await restarted.WaitUntil(() => !restarted.Item(id).IsConnected);
            await restarted.Ui(() => restarted.Service.RevokeAsync(restarted.Item(id)));
        }
        await using var revoked = await Host.StartAsync(path);
        await using var reconnect = await revoked.ConnectAsync(proof);
        Assert((await reconnect.Call("get_navigation")).Code == "approval_required", "Offline navigation revocation survives restart");
        var backend = new SyntheticNavigation();
        var adapter = new McpNavigationService(backend);
        adapter.Dispose(); adapter.Dispose();
        Assert((await adapter.ExecuteAsync(Navigate("login"), default)).Code == "unavailable" && backend.Opens == 0 && backend.Disposed,
            "Disposed navigation adapter remains inert and releases its backend");
    }

    private sealed class SyntheticNavigation : IAppDeepLinkNavigation, IDisposable
    {
        public AppNavigationSnapshot State { get; set; } = new("home", "mizon://home", null, false, true);
        public Func<AppActivationRequest, CancellationToken, Task<AppNavigationOutcome>>? Handler { get; set; }
        public int Opens { get; private set; }
        public AppActivationRequest? Last { get; private set; }
        public bool Disposed { get; private set; }
        public AppNavigationSnapshot GetState() => State;
        public Task<AppNavigationOutcome> OpenAsync(AppActivationRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Handler is not null) return Handler(request, token);
            Opens++; Last = request;
            State = new(request.RouteKey!, request.ToUri(), null, true, State.HasSession);
            return Task.FromResult(new AppNavigationOutcome(true, "opened", "Synthetic destination opened."));
        }
        public void Dispose() => Disposed = true;
    }
}
