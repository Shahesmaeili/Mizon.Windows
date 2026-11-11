using System.Collections.Concurrent;
using Mizon.Windows.Services;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

var musicId = Guid.NewGuid();
var music = new AppActivationRequest(MusicPublicId: musicId);
var opened = new AppNavigationOutcome(true, "opened", "The destination was opened.", new()
{
    Destination = "music-details", Uri = music.ToUri()!, PublicId = musicId, CompletionStage = "opened"
});

// Keep the real shared transport in this harness: responses must survive the
// generated serializer with reflection disabled, including the NativeAOT run.
{
    var received = new List<AppDeepLinkResult>();
    var id = Guid.NewGuid();
    using var session = new AppDeepLinkResponseSession(id, received.Add);
    session.Complete(opened);
    session.Fail("failed", "A late failure.");
    session.Cancel("cancelled", "A late cancellation.");
    Check(received.Count == 1 && received[0].Success, "success must be reported exactly once");
    var json = AppDeepLinkResultProtocol.Serialize(received[0]);
    Check(AppDeepLinkResultProtocol.TryParse(json, id, out var parsed), "generated response round trip");
    Check(parsed?.Data?.PublicId == musicId && parsed.Data.CompletionStage == "opened", "additional data must survive");
    Check(session.IsCompleted && !session.Token.IsCancellationRequested, "success is not a cancellation");
}
{
    AppDeepLinkResult? received = null;
    using var session = new AppDeepLinkResponseSession(Guid.NewGuid(), result => received = result);
    session.Complete(new(true, "INVALID CODE", "bad\nmessage"));
    Check(received is { Success: false, Code: "invalid_result", Data: null }, "invalid metadata must become a safe failure");
}
{
    var received = new ConcurrentQueue<AppDeepLinkResult>();
    using var session = new AppDeepLinkResponseSession(Guid.NewGuid(), received.Enqueue);
    await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() =>
    {
        if (index % 2 == 0) session.Complete(opened);
        else session.Cancel("cancelled", "Concurrent cancellation.");
    })));
    Check(received.Count == 1, "concurrent success and cancellation must report once");
}
{
    AppDeepLinkResult? received = null;
    var session = new AppDeepLinkResponseSession(Guid.NewGuid(), result => received = result);
    var token = session.Token;
    using var registration = token.Register(session.Dispose);
    session.Cancel("superseded", "A newer request arrived.");
    session.Dispose();
    Check(token.IsCancellationRequested, "cancellation propagates to the operation");
    Check(received is { Code: "superseded" }, "reentrant disposal must not replace the selected response");
}
{
    AppDeepLinkResult? received = null;
    using var session = new AppDeepLinkResponseSession(Guid.NewGuid(), result => received = result);
    using var registration = session.Token.Register(() => throw new InvalidOperationException("consumer secret"));
    session.Cancel("cancelled", "The caller cancelled.");
    Check(received is { Code: "cancelled" }, "a throwing cancellation consumer cannot suppress the response");
}
{
    var attempts = 0;
    using var session = new AppDeepLinkResponseSession(Guid.NewGuid(), _ =>
    {
        attempts++;
        throw new InvalidOperationException("caller disconnected");
    });
    session.Complete(opened);
    session.Cancel("cancelled", "The caller cancelled.");
    Check(attempts == 1 && session.IsCompleted, "a closed transport is not retried or thrown");
}
{
    var response = new TaskCompletionSource<AppDeepLinkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var session = new AppDeepLinkResponseSession(Guid.NewGuid(), result => response.TrySetResult(result), TimeSpan.FromMilliseconds(20));
    var result = await response.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(result is { Code: "timed_out", Success: false }, "deadline returns a safe timeout");
    Check(session.Token.IsCancellationRequested, "deadline cancels the underlying navigation");
    session.Complete(opened);
    Check(ReferenceEquals(result, response.Task.Result), "late success cannot replace timeout");
}
{
    AppDeepLinkResult? received = null;
    var session = new AppDeepLinkResponseSession(Guid.NewGuid(), result => received = result);
    session.Dispose();
    session.Dispose();
    Check(received is { Code: "app_closed" } && session.Token.IsCancellationRequested, "disposing pending response closes it once");
}

await DispatchResult(opened, expectedSuccess: true, "opened");
await DispatchResult(new(false, "requires_sign_in", "Sign in first."), expectedSuccess: false, "requires_sign_in");
await DispatchException(new InvalidOperationException("secret path C:\\private\\token"), "navigation_failed");
await DispatchException(new OperationCanceledException(), "cancelled");
await DispatchException(new TimeoutException(), "timed_out");

async Task DispatchResult(AppNavigationOutcome outcome, bool expectedSuccess, string expectedCode)
{
    using var navigation = new FakeNavigation((_, _) => Task.FromResult(outcome));
    var dispatcher = new AppDeepLinkResultDispatcher(navigation);
    AppDeepLinkResult? received = null;
    var response = new AppDeepLinkResponseSession(Guid.NewGuid(), result => received = result);
    await dispatcher.DispatchAsync(music, response);
    Check(received?.Success == expectedSuccess && received.Code == expectedCode, "real navigation outcome must be reported");
    Check(navigation.Calls == 1, "one navigation per request");
    dispatcher.Dispose();
    Check(!navigation.Disposed, "dispatcher must not dispose the shared MCP navigation service");
}

async Task DispatchException(Exception exception, string expectedCode)
{
    using var navigation = new FakeNavigation((_, _) => Task.FromException<AppNavigationOutcome>(exception));
    using var dispatcher = new AppDeepLinkResultDispatcher(navigation);
    AppDeepLinkResult? received = null;
    await dispatcher.DispatchAsync(music, new(Guid.NewGuid(), result => received = result));
    Check(received is { Success: false } && received.Code == expectedCode, "navigation exception must become a bounded failure");
    Check(!received!.Message.Contains("secret", StringComparison.OrdinalIgnoreCase)
        && !received.Message.Contains("private", StringComparison.OrdinalIgnoreCase), "exception details must not reach callers");
}

{
    using var navigation = new FakeNavigation((_, _) => Task.FromResult(opened));
    using var dispatcher = new AppDeepLinkResultDispatcher(navigation);
    AppDeepLinkResult? received = null;
    await dispatcher.DispatchAsync(new(), new(Guid.NewGuid(), result => received = result));
    Check(received is { Code: "invalid_uri", Success: false }, "invalid route must return a response");
    Check(navigation.Calls == 0, "invalid route must not call navigation");
}
{
    var blocked = new TaskCompletionSource<AppNavigationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var navigation = new FakeNavigation((_, _) => blocked.Task);
    using var dispatcher = new AppDeepLinkResultDispatcher(navigation);
    var received = new ConcurrentQueue<AppDeepLinkResult>();
    var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var response = new AppDeepLinkResponseSession(Guid.NewGuid(), result =>
    {
        received.Enqueue(result); reported.TrySetResult();
    }, TimeSpan.FromMilliseconds(20));
    var dispatch = dispatcher.DispatchAsync(music, response);
    await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(!dispatch.IsCompleted && received.Single().Code == "timed_out", "timeout must report while an uncooperative operation is still blocked");
    blocked.SetResult(opened);
    await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
    Check(received.Count == 1, "uncooperative late success must not report again");
}
{
    var blocked = new TaskCompletionSource<AppNavigationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var navigation = new FakeNavigation((request, _) => request.Destination == "login" ? Task.FromResult(opened) : blocked.Task);
    using var dispatcher = new AppDeepLinkResultDispatcher(navigation);
    var previous = new List<AppDeepLinkResult>();
    var next = new List<AppDeepLinkResult>();
    var first = dispatcher.DispatchAsync(music, new(Guid.NewGuid(), previous.Add));
    await dispatcher.DispatchAsync(new(Destination: "login"), new(Guid.NewGuid(), next.Add));
    Check(previous.Count == 1 && previous[0].Code == "superseded", "new request reports previous cancellation immediately");
    Check(next.Count == 1 && next[0].Success, "new request gets its own independent success");
    blocked.SetResult(opened);
    await first.WaitAsync(TimeSpan.FromSeconds(5));
    Check(previous.Count == 1, "previous operation cannot publish after supersession");
}
{
    var blocked = new TaskCompletionSource<AppNavigationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var navigation = new FakeNavigation((_, _) => blocked.Task);
    var dispatcher = new AppDeepLinkResultDispatcher(navigation);
    var results = new List<AppDeepLinkResult>();
    var response = new AppDeepLinkResponseSession(Guid.NewGuid(), results.Add);
    var dispatch = dispatcher.DispatchAsync(music, response);
    await dispatcher.DispatchAsync(music, response);
    Check(navigation.Calls == 1 && results.Count == 0, "duplicate envelope must not start or cancel another operation");
    dispatcher.Dispose();
    Check(results.Count == 1 && results[0].Code == "app_closed", "app shutdown reports to the active caller");
    var denied = new List<AppDeepLinkResult>();
    await dispatcher.DispatchAsync(music, new(Guid.NewGuid(), denied.Add));
    Check(denied.Count == 1 && denied[0].Code == "app_closed" && navigation.Calls == 1, "closed dispatcher cannot reopen a destination");
    blocked.SetResult(opened);
    await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
    Check(results.Count == 1, "shutdown cannot be overwritten by delayed success");
}
{
    var blocked = new TaskCompletionSource<AppNavigationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var navigation = new FakeNavigation((_, _) => blocked.Task);
    using var dispatcher = new AppDeepLinkResultDispatcher(navigation);
    var results = new List<AppDeepLinkResult>();
    var response = new AppDeepLinkResponseSession(Guid.NewGuid(), results.Add);
    var dispatch = dispatcher.DispatchAsync(music, response);
    dispatcher.CancelActive();
    Check(response.Token.IsCancellationRequested && results.Single().Code == "superseded", "ordinary deep link cancels a pending result navigation");
    blocked.SetResult(opened);
    await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
    Check(results.Count == 1, "ordinary replacement does not permit a second report");
}
{
    using var navigation = new FakeNavigation((_, _) => Task.FromResult(opened));
    using var dispatcher = new AppDeepLinkResultDispatcher(navigation);
    var results = new List<AppDeepLinkResult>();
    var response = new AppDeepLinkResponseSession(Guid.NewGuid(), results.Add);
    response.Cancel("cancelled", "Cancelled during app startup.");
    await dispatcher.DispatchAsync(music, response);
    Check(navigation.Calls == 0 && results.Count == 1, "expired startup envelope cannot navigate later");
}

Console.WriteLine($"Passed {checks} deep-link response lifecycle assertions.");

sealed class FakeNavigation(Func<AppActivationRequest, CancellationToken, Task<AppNavigationOutcome>> open) : IAppDeepLinkNavigation, IDisposable
{
    public int Calls { get; private set; }
    public bool Disposed { get; private set; }
    public AppNavigationSnapshot GetState() => new("login", "mizon://login", null, false, false);
    public Task<AppNavigationOutcome> OpenAsync(AppActivationRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        return open(request, cancellationToken);
    }
    public void Dispose() => Disposed = true;
}
