using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static McpRequest AppearanceWrite(McpAppearanceTheme? theme = null, McpAppearanceBackdrop? backdrop = null) =>
        new() { Operation = "set_appearance", Appearance = new() { Theme = theme, Backdrop = backdrop } };

    private static async Task AppearanceScopesAndState()
    {
        await using var host = await Host.StartAsync(Path.Combine(StorageRoot, "appearance-scopes", "grants.dat"));
        await using var peer = await host.ConnectAsync(Proof());
        var id = peer.Hello.Connection!.Id;
        Assert((await peer.Call("get_appearance")).Code == "approval_required", "Pending connections cannot read appearance");
        Assert(!(await peer.Call(AppearanceWrite(McpAppearanceTheme.Dark))).Success && host.Preferences.Writes == 0,
            "Pending appearance writes never reach the preference backend");
        await host.Approve(id, true, true, true);
        var oldGrant = await peer.Call("get_appearance");
        Assert(!oldGrant.Success && oldGrant.Appearance is null && oldGrant.Connection is { AllowAppearanceRead: false, AllowAppearanceControl: false },
            "All legacy authentication scopes still grant no appearance permissions");
        Assert(!(await peer.Call(AppearanceWrite(McpAppearanceTheme.Dark))).Success && host.Preferences.Writes == 0,
            "Authentication control cannot mutate appearance");

        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, appearanceRead: true);
        var read = await peer.Call("get_appearance");
        Assert(read is { Success: true, State: null, Appearance.Theme: McpAppearanceTheme.System },
            "Appearance-only reader receives appearance without authentication state");
        Assert(!(await peer.Call("get_state")).Success, "Appearance read never authorizes authentication reads");
        Assert(!(await peer.Call(AppearanceWrite(McpAppearanceTheme.Dark))).Success && host.Preferences.Writes == 0,
            "Appearance read does not authorize mutation");

        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, appearanceControl: true);
        var write = await peer.Call(AppearanceWrite(McpAppearanceTheme.Dark, McpAppearanceBackdrop.MicaAlt));
        Assert(write is { Success: true, Appearance: null, State: null } && host.Preferences.Writes == 1
            && host.Preferences.State is { ThemeMode: AppearanceThemeMode.Dark, BackdropMode: AppearanceBackdropMode.MicaAlt },
            "Control-only appearance mutation reaches shared adapter and returns no private state");
        Assert(!(await peer.Call("get_appearance")).Success && !(await peer.Call("set_language")).Success,
            "Appearance control cannot read state or control authentication/language");

        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, appearanceRead: true, appearanceControl: true);
        var updated = await peer.Call(AppearanceWrite(backdrop: McpAppearanceBackdrop.AcrylicThin));
        Assert(updated is { Success: true, Appearance: { Theme: McpAppearanceTheme.Dark, Backdrop: McpAppearanceBackdrop.AcrylicThin, IsPreferenceSaved: true } },
            "Partial update preserves omitted theme and returns applied appearance through real pipe/dispatcher");
        await host.Ui(() => host.Preferences.State = host.Preferences.State with
        {
            FallbackReason = AppearanceFallbackReason.HighContrast, IsHighContrast = true,
            EffectiveBackdrop = null, IsPreferenceSaved = false
        });
        var fallback = await peer.Call("get_appearance");
        Assert(fallback.Appearance is { FallbackReason: McpAppearanceFallback.HighContrast, EffectiveBackdrop: null, IsPreferenceSaved: false },
            "Read state distinguishes requested material, solid fallback and failed persistence");
        Assert((await peer.Call(AppearanceWrite(McpAppearanceTheme.Light))).Code == "appearance_not_saved",
            "Failed persistence returns explicit session-only outcome");
        var writes = host.Preferences.Writes;
        Assert((await peer.Call(AppearanceWrite())).Code == "invalid_arguments" && host.Preferences.Writes == writes,
            "Empty appearance mutation is rejected before backend writes");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try
            {
                await new McpAppearanceService(host.Preferences).ExecuteAsync(AppearanceWrite(McpAppearanceTheme.System), canceled.Token);
                throw new InvalidOperationException("Canceled appearance mutation reached the backend.");
            }
            catch (OperationCanceledException) { }
            Assert(host.Preferences.Writes == writes, "Production appearance adapter honors cancellation before synchronous mutation");
        }
        await host.Ui(() => host.Preferences.FailWrite = true);
        var failed = await peer.Call(AppearanceWrite(McpAppearanceTheme.System));
        Assert(failed.Code == "operation_failed" && !failed.Message.Contains("private", StringComparison.Ordinal)
            && failed.Appearance is null && host.Preferences.Writes == writes, "Backend appearance failures are sanitized and never replayed");
    }

    private static async Task AppearanceRevocationAndRestart()
    {
        var path = Path.Combine(StorageRoot, "appearance-restart", "grants.dat");
        var proof = Proof();
        McpCredentialIdentity.TryGetId(proof, out var id);
        await using (var host = await Host.StartAsync(path))
        {
            await using var first = await host.ConnectAsync(proof);
            await using var second = await host.ConnectAsync(proof);
            await host.Approve(id, false, false, false, appearanceRead: true, appearanceControl: true);
            var entered = Signal();
            var canceled = Signal();
            host.Appearance.Handler = async (_, token) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
                return new();
            };
            var pending = first.Call(AppearanceWrite(McpAppearanceTheme.Dark));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert((await pending).Code == "cancelled" && host.Preferences.Writes == 0,
                "Revocation cancels in-flight appearance work before mutation");
            Assert(!(await second.Call("get_appearance")).Success, "Revocation blocks every appearance connection for the identity");
            host.Appearance.Handler = null;
            await host.Approve(id, false, false, false, appearanceRead: true);
            Assert((await second.Call("get_appearance")).Success && !(await second.Call(AppearanceWrite(McpAppearanceTheme.Dark))).Success,
                "Reapproval restores only selected appearance scopes");
            await host.Ui(() => host.Service.IsEnabled = false);
            Assert((await second.Call("get_appearance")).Code == "disabled", "Global disable suspends appearance access");
            await host.Ui(() => host.Service.IsEnabled = true);
            Assert((await first.Call("get_appearance")).Success, "Re-enable restores exact remembered appearance scopes");
        }
        await using (var restarted = await Host.StartAsync(path))
        {
            await using var peer = await restarted.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { AllowRead: false, AllowControl: false, AllowAppearanceRead: true, AllowAppearanceControl: false },
                "Restart restores exact appearance grants without promoting authentication or appearance control");
            Assert((await peer.Call("get_appearance")).Success && !(await peer.Call(AppearanceWrite(McpAppearanceTheme.Dark))).Success,
                "Restored appearance reader remains read-only");
            await peer.DisposeAsync();
            await restarted.WaitUntil(() => !restarted.Item(id).IsConnected);
            await restarted.Ui(() => restarted.Service.RevokeAsync(restarted.Item(id)));
        }
        await using var revoked = await Host.StartAsync(path);
        await using var reconnect = await revoked.ConnectAsync(proof);
        Assert((await reconnect.Call("get_appearance")).Code == "approval_required", "Offline appearance revocation remains effective after restart");
    }
}
