using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static async Task NavigationResultBehavior()
    {
        await using var host = await Host.StartAsync(Path.Combine(StorageRoot, "navigation-results", "grants.dat"));
        await using var peer = await host.ConnectAsync(Proof());
        var id = peer.Hello.Connection!.Id;
        Assert((await peer.Call(Navigate("music"))) is { Success: false, NavigationResult: null },
            "Pending navigation cannot disclose invocation result data");
        await host.Approve(id, true, true, false, navigationControl: true);
        host.Auth.Handler = (_, _) => Task.FromResult(new McpResponse
        {
            Success = true, Code = "synthetic", NavigationResult = new()
            {
                RequestId = Guid.NewGuid(), Success = true, Data = new() { Destination = "client", Uri = "mizon://client/private" }
            }
        });
        Assert((await peer.Call("get_state")).NavigationResult is null,
            "Independent navigation read gate redacts results even when another adapter accidentally provides them");
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        await host.Approve(id, false, false, false, navigationRead: true, navigationControl: true);
        var clientId = Guid.NewGuid();
        var data = new AppDeepLinkResultData
        {
            Destination = "client", Uri = $"mizon://client/{clientId:D}", PublicId = clientId, CompletionStage = "completed"
        };
        host.NavigationBackend.Handler = (_, _) => Task.FromResult(new AppNavigationOutcome(true, "completed", "Profile is ready.", data));
        var completed = await peer.Call(Navigate("client", clientId));
        Assert(completed.NavigationResult is { Success: true, Code: "completed", Data.CompletionStage: "completed" }
            && completed.NavigationResult.Data.PublicId == clientId,
            "A real completed route outcome remains completed instead of being inferred from a navigation snapshot");
        host.NavigationBackend.Handler = (_, _) => Task.FromResult(new AppNavigationOutcome(false, "not_found", "The profile is unavailable."));
        var failed = await peer.Call(Navigate("client", clientId));
        Assert(failed is { Success: false, Code: "not_found", NavigationResult: { Success: false, Code: "not_found", Data: null } }
            && failed.NavigationResult.RequestId != completed.NavigationResult!.RequestId,
            "A route failure has its own correlated receipt and never reuses successful destination data");
        host.NavigationBackend.Handler = (_, _) => Task.FromResult(new AppNavigationOutcome(true, "completed", "Profile ready.",
            data with { Uri = "file:///C:/private-account-data" }));
        var invalid = await peer.Call(Navigate("client", clientId));
        Assert(invalid is { Code: "operation_failed", NavigationResult: null, Navigation: null }
            && !invalid.Message.Contains("private", StringComparison.Ordinal),
            "Malformed route data is rejected and cannot leak a local path in the MCP receipt");
        host.NavigationBackend.Handler = (_, _) => throw new TimeoutException();
        Assert((await peer.Call(Navigate("client", clientId))) is { Code: "page_not_ready", NavigationResult: null },
            "A timeout does not manufacture a completed navigation receipt");
        var ready = Signal();
        host.NavigationBackend.Handler = async (_, token) =>
        {
            ready.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new(true, "completed", "Must never escape.", data);
        };
        var running = peer.Call(Navigate("client", clientId));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
        Assert((await running) is { Success: false, NavigationResult: null, Navigation: null },
            "Revocation cancels in-flight navigation and suppresses its receipt and extra data");

        var backend = new SyntheticNavigation();
        var adapter = new McpNavigationService(backend);
        var completion = new TaskCompletionSource<AppNavigationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Handler = (_, _) => completion.Task;
        var late = adapter.ExecuteAsync(Navigate("music"), default);
        adapter.Dispose();
        completion.SetResult(new(true, "completed", "Must not appear after disposal.", data));
        Assert((await late) is { Code: "unavailable", NavigationResult: null, Navigation: null },
            "Disposal while navigation is pending prevents a late successful receipt");
        backend = new();
        using var canceledAdapter = new McpNavigationService(backend);
        using var cancellation = new CancellationTokenSource();
        backend.Handler = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new AppNavigationOutcome(true, "completed", "Cancelled outcome.", data));
        };
        try
        {
            await canceledAdapter.ExecuteAsync(Navigate("music"), cancellation.Token);
            throw new InvalidOperationException("Cancelled result escaped.");
        }
        catch (OperationCanceledException) { Assert(true, "Cancellation after route completion still suppresses the result"); }
    }
}
