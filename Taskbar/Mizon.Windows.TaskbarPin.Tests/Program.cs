using Mizon.Windows.Services;

var assertions = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    assertions++;
    Console.WriteLine("PASS " + label);
}

var backend = new FakeBackend();
using (var service = new TaskbarPinService(backend))
{
    Check(!service.State.CanRequest, "Unknown initial state never offers pinning");
    backend.Pinned = true;
    Check((await service.RefreshAsync()).IsPinned && !service.State.CanRequest, "Fresh OS pinned state hides the offer");
    backend.Pinned = false;
    Check((await service.RefreshAsync()).CanRequest && backend.ReadCount == 2, "External unpin is detected by a new OS query");
    backend.Pinned = true;
    Check((await service.RequestPinAsync()).Success && backend.PromptCount == 0, "Already pinned does not open a prompt");
    backend.Pinned = false;
    backend.Allowed = false;
    Check(!(await service.RequestPinAsync()).Success && backend.PromptCount == 0, "Windows policy denial never opens a prompt");
    backend.Allowed = true;
    backend.Supported = false;
    Check(!(await service.RequestPinAsync()).Success && backend.PromptCount == 0, "Unsupported Windows never opens a prompt");
    backend.Supported = true;
    backend.IsForeground = false;
    Check((await service.RequestPinAsync()).Code == "foreground_required" && backend.PromptCount == 0,
        "Background requests are refused without stealing focus");
    backend.IsForeground = true;
    backend.Accept = true;
    var result = await service.RequestPinAsync();
    Check(result.Success && result.State.IsPinned && !result.State.IsBusy && !service.State.CanRequest,
        "Confirmed pin queries actual OS state and clears busy state");
    backend.Pinned = false;
    backend.Accept = false;
    result = await service.RequestPinAsync();
    Check(!result.Success && result.Code == "not_confirmed" && service.State.CanRequest,
        "Declining pin keeps the option available without retrying");
    backend.ThrowOnRead = true;
    Check((await service.RefreshAsync()).Code == "unavailable" && !service.State.CanRequest,
        "OS read failure produces an unavailable state without leaking native errors");
    backend.ThrowOnRead = false;
    backend.ThrowOnPin = true;
    result = await service.RequestPinAsync();
    Check(!result.Success && result.Code == "unavailable" && !result.Message.Contains("SECRET"),
        "Native prompt failure is sanitized and releases the gate");
    backend.ThrowOnPin = false;
    Check((await service.RefreshAsync()).CanRequest, "A later fresh query recovers after failure");
}
Check(backend.Disposed, "Disposal releases the platform boundary");

backend = new FakeBackend { HoldPrompt = true };
using (var service = new TaskbarPinService(backend))
{
    using var cancellation = new CancellationTokenSource();
    var pending = service.RequestPinAsync(cancellation.Token);
    await backend.Started.Task;
    Check(service.State.IsBusy, "Outstanding native prompt is busy");
    var duplicate = await service.RequestPinAsync();
    Check(duplicate.Code == "busy" && duplicate.State.IsBusy && backend.PromptCount == 1,
        "Repeated clicks/MCP calls cannot open duplicate native prompts");
    var read = await service.RefreshAsync();
    Check(read.IsBusy && backend.ReadCount == 1, "Refresh does not race an active prompt");
    cancellation.Cancel();
    try { await pending; throw new InvalidOperationException("Cancellation was ignored"); }
    catch (OperationCanceledException) { Check(true, "Caller cancellation returns promptly"); }
    Check(backend.CancellationObserved && service.State.IsBusy, "Native cancellation is requested while keeping prompt ownership");
    Check((await service.RequestPinAsync()).Code == "busy" && backend.PromptCount == 1,
        "Canceled caller cannot expose a second prompt before native completion");
    var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    service.StateChanged += (_, _) => { if (!service.State.IsBusy) settled.TrySetResult(); };
    backend.Completion.SetResult(false);
    await settled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check(!service.State.IsBusy, "Native completion releases canceled prompt ownership");
    Check((await service.RefreshAsync()).CanRequest, "State remains refreshable after cancellation");
}

backend = new FakeBackend { HoldPrompt = true };
var disposedService = new TaskbarPinService(backend);
var operation = disposedService.RequestPinAsync();
await backend.Started.Task;
disposedService.Dispose();
var notifications = 0;
disposedService.StateChanged += (_, _) => notifications++;
backend.Completion.SetResult(false);
await operation;
Check(backend.Disposed && backend.CancellationObserved && notifications == 0,
    "Shutdown cancels native work and cannot publish late UI changes");
Check(!(await disposedService.RequestPinAsync()).Success && backend.PromptCount == 1,
    "Disposed service is inert and cannot open another native dialog");
disposedService.Dispose();
Check(!disposedService.State.IsBusy && !disposedService.State.CanRequest, "Repeated disposal is safe");

backend = new FakeBackend();
using (var service = new TaskbarPinService(backend))
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try { await service.RequestPinAsync(cancellation.Token); throw new InvalidOperationException("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(backend.PromptCount == 0 && backend.ReadCount == 0, "Pre-canceled request never reaches Windows"); }
}
Console.WriteLine($"PASS {assertions} taskbar-pin service assertions; fake OS only, no Windows pin changed or dialog shown.");

sealed class FakeBackend : ITaskbarPinBackend
{
    public bool Pinned;
    public bool Supported = true;
    public bool Allowed = true;
    public bool IsForeground { get; set; } = true;
    public bool Accept;
    public bool HoldPrompt;
    public bool ThrowOnRead;
    public bool ThrowOnPin;
    public bool CancellationObserved;
    public bool Disposed;
    public int ReadCount;
    public int PromptCount;
    public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void VerifyAccess() { }
    public Task<TaskbarPinState> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ThrowOnRead) throw new InvalidOperationException("SECRET path/native failure");
        ReadCount++;
        return Task.FromResult(new TaskbarPinState(Supported, Pinned, Allowed, false,
            !Supported ? "unsupported" : !Allowed ? "not_allowed" : Pinned ? "pinned" : "not_pinned"));
    }
    public async Task<bool> RequestPinAsync(CancellationToken token)
    {
        PromptCount++;
        if (ThrowOnPin) throw new InvalidOperationException("SECRET native error");
        Started.TrySetResult();
        using var registration = token.Register(() => CancellationObserved = true);
        var accepted = HoldPrompt ? await Completion.Task : Accept;
        if (accepted) Pinned = true;
        return accepted;
    }
    public void Dispose() => Disposed = true;
}
