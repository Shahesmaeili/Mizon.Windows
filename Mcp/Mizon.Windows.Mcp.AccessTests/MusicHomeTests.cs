using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static McpRequest HomeAction(string action, string section = "featured", bool? enabled = null) => new()
    {
        Operation = action is "like" or "follow" ? "music_home_submit" : "music_home_action",
        MusicHome = new() { Section = section, Action = action, Enabled = enabled }
    };

    private static async Task MusicHomeScopesAndBehavior()
    {
        await using var host = await Host.StartAsync(Path.Combine(StorageRoot, "music-home-scopes", "grants.dat"));
        await using var peer = await host.ConnectAsync(Proof());
        var id = peer.Hello.Connection!.Id;
        Assert((await peer.Call("get_music_home")).Code == "approval_required" && host.MusicPage.Reads == 0,
            "Pending music home access does not read the active page");
        await host.Approve(id, true, true, true, true, true, true, true, true, true);
        Assert(!(await peer.Call("get_music_home")).Success && !(await peer.Call(HomeAction("pause"))).Success
            && host.MusicPage.Actions == 0, "All older grants still authorize no music home data or control");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, musicRead: true);
        var state = await peer.Call("get_music_home");
        Assert(state is { Success: true, State: null, MusicHome: { HasSession: true, IsAvailable: true } }
            && state.MusicHome.Sections[0] is { Source: "api", Items.Length: 1 }, "Actual pipe reads typed home card/source state through production adapter");
        Assert(!(await peer.Call(HomeAction("pause"))).Success, "Music read cannot control playback");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, musicControl: true);
        var pause = await peer.Call(HomeAction("pause"));
        Assert(pause is { Success: true, MusicHome: null } && !host.MusicPage.Playing,
            "Control uses the live page and returns no snapshot without read consent");
        Assert(!(await peer.Call(HomeAction("like", enabled: true))).Success && !host.MusicPage.Liked,
            "Playback control never implies permission to submit a like");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, musicRead: true, musicControl: true, musicSubmit: true);
        var like = await peer.Call(HomeAction("like", enabled: true));
        Assert(like.Success && like.MusicHome?.Hero?.IsLiked == true && host.MusicPage.Liked,
            "Explicit submit grant writes the synthetic desired state through production adapter");
        var language = await peer.Call(new McpRequest
        {
            Operation = "music_home_action", MusicHome = new() { Section = "all", Action = "set_language", Language = "en-US" }
        });
        Assert(language.MusicHome?.Language == "en-US" && host.MusicPage.Liked,
            "Language control updates the same page while preserving its existing state");
        var before = host.MusicPage.Actions;
        var invalid = await peer.Call(HomeAction("seek") with { MusicHome = new() { Section = "featured", Action = "seek", Value = -1 } });
        Assert(!invalid.Success && host.MusicPage.Actions == before, "Invalid positions never reach the page");
        Assert(!(await peer.Call(HomeAction("like", enabled: false) with { Operation = "music_home_action" })).Success
            && host.MusicPage.Liked, "Forging a write under the control operation cannot bypass submit validation");
        host.MusicPage.RefreshHandler = _ => throw new IOException("private-path-and-secret");
        var failure = await peer.Call(new McpRequest { Operation = "refresh_music_home", MusicHome = new() { Section = "library" } });
        Assert(failure.Code == "operation_failed" && !failure.Message.Contains("private-path"), "Backend refresh failure is sanitized with no sensitive paths");
        host.MusicPage.RefreshHandler = null;
        Assert((await peer.Call(new McpRequest { Operation = "refresh_music_home", MusicHome = new() { Section = "library" } })).Success,
            "Failed action releases the adapter gate for the next refresh");
        host.MusicSession = false;
        Assert((await peer.Call("get_music_home")).Code == "requires_sign_in", "Signed-out account cannot read retained home data");
        host.MusicSession = true; host.MusicPage.IsActive = false;
        Assert((await peer.Call("get_music_home")).Code == "page_mismatch", "Offscreen cached page is not an MCP data source");
    }

    private static async Task MusicHomeLifetimeAndRestoration()
    {
        var path = Path.Combine(StorageRoot, "music-home-lifetime", "grants.dat");
        var proof = Proof(); McpCredentialIdentity.TryGetId(proof, out var id);
        McpMusicHomeService released;
        await using (var host = await Host.StartAsync(path))
        {
            released = host.MusicHome;
            await using var peer = await host.ConnectAsync(proof);
            await host.Approve(id, false, false, false, musicRead: true, musicControl: true);
            var started = Signal(); var canceled = Signal();
            host.MusicPage.RefreshHandler = async token =>
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
            };
            var loading = peer.Call(new McpRequest { Operation = "refresh_music_home", MusicHome = new() { Section = "all" } });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            Assert((await loading) is { Code: "cancelled", MusicHome: null }, "Revoking home consent cancels active refresh and suppresses stale response");
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert(!(await peer.Call("get_music_home")).Success, "Revoked connection cannot read cached music state");
            await host.Approve(id, false, false, false, musicRead: true);
        }
        Assert((await released.ExecuteAsync(new() { Operation = "get_music_home" }, default)).Code == "unavailable",
            "App host shutdown disposes the home adapter");
        await using (var restarted = await Host.StartAsync(path))
        {
            await using var peer = await restarted.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { IsRemembered: true, AllowMusicHomeRead: true, AllowMusicHomeControl: false, AllowMusicHomeSubmit: false },
                "Restart restores exact independent music home scopes");
            Assert((await peer.Call("get_music_home")).Success && !(await peer.Call(HomeAction("pause"))).Success,
                "Remembered read-only music consent stays read-only after restart");
        }
        var page = new SyntheticMusicHomePage();
        using var adapter = new McpMusicHomeService(() => page, () => true);
        using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
        try { await adapter.ExecuteAsync(HomeAction("pause"), preCancelled.Token); Assert(false, "Pre-cancel must throw"); }
        catch (OperationCanceledException) { Assert(page.Actions == 0, "Pre-cancelled action cannot touch playback"); }
        var signal = Signal();
        page.RefreshHandler = async token => { signal.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
        var request = adapter.ExecuteAsync(new() { Operation = "refresh_music_home", MusicHome = new() { Section = "all" } }, default);
        await signal.Task; page.Close();
        Assert((await request).Code == "page_changed", "Leaving the page cancels requests using the real page lifetime contract");
        adapter.Dispose(); adapter.Dispose();

        var playbackPage = new SyntheticMusicHomePage();
        using var playbackAdapter = new McpMusicHomeService(() => playbackPage, () => true);
        playbackPage.ActionHandler = (_, token) =>
        {
            playbackPage.IsPlaybackHandoff = true;
            playbackPage.Close();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new McpResponse { Success = true, Code = "playing" });
        };
        Assert((await playbackAdapter.ExecuteAsync(HomeAction("play"), default)) is { Success: true, MusicHome: null },
            "Explicit playback hand-off survives its source page unloading and drops obsolete state");
        var departedPage = new SyntheticMusicHomePage();
        using var departedAdapter = new McpMusicHomeService(() => departedPage, () => true);
        departedPage.ActionHandler = (_, token) =>
        {
            departedPage.Close();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new McpResponse { Success = true });
        };
        Assert((await departedAdapter.ExecuteAsync(HomeAction("play"), default)).Code == "page_changed",
            "Leaving before an explicit playback hand-off cancels the pending play request");
        var failedPage = new SyntheticMusicHomePage();
        using var failedAdapter = new McpMusicHomeService(() => failedPage, () => true);
        failedPage.ActionHandler = (_, _) => Task.FromResult(new McpResponse { Code = "action_unavailable" });
        Assert(!(await failedAdapter.ExecuteAsync(HomeAction("play"), default)).Success,
            "Playback failure never becomes a successful action response");
    }

    private sealed class SyntheticMusicHomePage : IMcpMusicHomePage
    {
        private readonly CancellationTokenSource _lifetime = new();
        public bool IsActive { get; set; } = true;
        public CancellationToken Lifetime => _lifetime.Token;
        public bool IsPlaybackHandoff { get; set; }
        public Func<McpMusicHomeRequest, CancellationToken, Task<McpResponse>>? ActionHandler { get; set; }
        public bool Playing { get; set; } = true;
        public bool Liked { get; set; }
        public string Language { get; set; } = "fa-IR";
        public int Reads { get; private set; }
        public int Actions { get; private set; }
        public Func<CancellationToken, Task>? RefreshHandler { get; set; }
        public McpMusicHomeState GetState()
        {
            Reads++;
            return new()
            {
                IsAvailable = true, HasSession = true, Language = Language,
                Hero = new() { IsPlaying = Playing, IsLiked = Liked },
                Sections = [new() { Id = "featured", Source = "api", Items = [new() { Id = "synthetic-track", Kind = "music", Title = "Synthetic track" }] }]
            };
        }
        public Task RefreshAsync(string section, CancellationToken token) => RefreshHandler?.Invoke(token) ?? Task.CompletedTask;
        public Task<McpResponse> ExecuteActionAsync(McpMusicHomeRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Actions++;
            if (ActionHandler is { } handler) return handler(request, token);
            if (request.Action == "pause") Playing = false;
            if (request.Action == "like") Liked = request.Enabled!.Value;
            if (request.Action == "set_language") Language = request.Language!;
            return Task.FromResult(new McpResponse { Success = true, Code = "updated" });
        }
        public void Close() { IsActive = false; _lifetime.Cancel(); }
    }
}
