using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;

internal static class Program
{
    private static int _checks;
    private static async Task Main()
    {
        await ActivePageAndActions();
        await CancellationAndLifecycle();
        Console.WriteLine($"PASS {_checks} production authentication adapter assertions. Synthetic pages only; no UI, accounts or real consent.");
    }

    private static McpRequest Action(McpAuthGamepadAction? action = McpAuthGamepadAction.Next) => new() { Operation = "auth_gamepad_action", GamepadAction = action };
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }

    private static async Task ActivePageAndActions()
    {
        var navigation = new NavigationService();
        var storage = new LocalStorageService();
        using var adapter = new McpAuthenticationService(navigation, storage);
        Check((await adapter.ExecuteAsync(Action(), default)) is { Code: "page_mismatch", State.Gamepad: null }, "Absent auth page rejects controller action and exposes no gamepad state");
        navigation.CurrentMainPage = new HomePage();
        Check((await adapter.ExecuteAsync(Action(), default)) is { Code: "page_mismatch", State.Gamepad: null }, "Other page cannot receive controller actions or gamepad reads");
        var login = new LoginPage { IsLoaded = false };
        navigation.CurrentMainPage = login;
        Check((await adapter.ExecuteAsync(Action(), default)) is { Code: "page_mismatch", State.Gamepad: null }
            && login.ActionCalls == 0 && login.StateReads == 0, "Unloaded auth page is neither controlled nor queried for gamepad state");
        login.Load();
        foreach (var registration in new[] { false, true })
        {
            login.ViewModel.IsRegistrationMode = registration;
            var state = await adapter.ExecuteAsync(new() { Operation = "get_state" }, default);
            Check(state.State?.Page == (registration ? "register" : "login") && state.State.Gamepad == login.Gamepad,
                "Active login/register reads the actual page-owned controller snapshot");
            foreach (var action in Enum.GetValues<McpAuthGamepadAction>())
            {
                var result = await adapter.ExecuteAsync(Action(action), default);
                Check(result is { Success: true, Code: "gamepad_action_applied" } && login.LastAction == action && login.Submissions == 0,
                    "Bounded " + action + " reuses page action without submitting " + state.State!.Page);
            }
            var json = JsonSerializer.Serialize(state, McpJsonContext.Default.McpResponse);
            Check(!json.Contains("synthetic-secret", StringComparison.Ordinal), "Controller state never returns password text");
        }
        var calls = login.ActionCalls;
        Check((await adapter.ExecuteAsync(Action(null), default)).Code == "invalid_action"
            && (await adapter.ExecuteAsync(Action((McpAuthGamepadAction)999), default)).Code == "invalid_action"
            && login.ActionCalls == calls, "Missing or undefined actions fail before touching the page");
        login.ViewModel.ControlsIsEnabled = false;
        Check((await adapter.ExecuteAsync(Action(), default)).Code == "busy" && login.ActionCalls == calls,
            "Busy authentication blocks navigation changes");
        login.ViewModel.ControlsIsEnabled = true;
        login.Action = (_, _) => Task.FromResult(false);
        Check((await adapter.ExecuteAsync(Action(), default)).Code == "action_unavailable", "Unavailable page action is not reported as applied");

        var welcome = new WelcomePage { IsMcpInitializing = true };
        navigation.CurrentMainPage = welcome;
        Check((await adapter.ExecuteAsync(Action(), default)).Code == "busy" && welcome.ActionCalls == 0, "Initializing Welcome blocks controller changes");
        welcome.IsMcpInitializing = false;
        Check((await adapter.ExecuteAsync(Action(McpAuthGamepadAction.ShowHelp), default)) is { Success: true, State.Page: "welcome" }
            && welcome.LastAction == McpAuthGamepadAction.ShowHelp && welcome.Retries == 0, "Welcome controller action shares page flow without triggering a retry");
        Check(login.ActionCalls == calls + 1, "Cached offscreen login never receives Welcome actions");
    }

    private static async Task CancellationAndLifecycle()
    {
        var login = new LoginPage();
        var navigation = new NavigationService { CurrentMainPage = login };
        var storage = new LocalStorageService();
        var adapter = new McpAuthenticationService(navigation, storage);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await adapter.ExecuteAsync(Action(), cancelled.Token); throw new InvalidOperationException("Expected cancellation."); }
        catch (OperationCanceledException) { Check(login.ActionCalls == 0, "Pre-cancelled controller request never touches the page"); }
        using var pending = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        login.Action = async (_, token) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return true; };
        var operation = adapter.ExecuteAsync(Action(), pending.Token);
        await started.Task;
        pending.Cancel();
        try { await operation; throw new InvalidOperationException("Expected cancellation."); }
        catch (OperationCanceledException) { Check(true, "Active controller request propagates its cancellation into the page hook"); }
        login.Action = (_, _) => throw new OperationCanceledException();
        Check((await adapter.ExecuteAsync(Action(), default)).Code == "page_changed", "Page-owned cancellation is a sanitized page-changed result");
        login.Action = (_, _) => { login.Unload(); return Task.FromResult(true); };
        Check((await adapter.ExecuteAsync(Action(), default)) is { Code: "page_changed", State.Gamepad: null }, "Unload invalidates successful late controller result and its state");
        login.Load();
        login.Action = (_, _) => { navigation.CurrentMainPage = new HomePage(); return Task.FromResult(true); };
        Check((await adapter.ExecuteAsync(Action(), default)) is { Code: "page_changed", State.Gamepad: null }, "Navigation invalidates late controller result from old page");

        var waitingPage = new LoginPage { IsLoaded = false };
        using var loading = new CancellationTokenSource();
        var waiting = McpAuthenticationService.WaitForLoadedAsync(waitingPage, loading.Token);
        Check(waitingPage.Subscribers == 2, "Load waiter owns only its two temporary page subscriptions");
        loading.Cancel();
        try { await waiting; throw new InvalidOperationException("Expected cancellation."); }
        catch (OperationCanceledException) { Check(waitingPage.Subscribers == 0, "Cancelled auth page wait releases both lifecycle subscriptions"); }
        Check(storage.Subscribers == 1, "Auth adapter owns one session-state subscription");
        adapter.Dispose();
        adapter.Dispose();
        Check(storage.Subscribers == 0 && (await adapter.ExecuteAsync(Action(), default)).Code == "unavailable",
            "Disposed auth adapter detaches session subscription and rejects controller work");
    }
}
