using Mizon.Windows.Services;
using Mizon.Windows.Taskbar;
using System.Text.Json;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    ++count;
}
var profile = new ProfileSnapshot { IsAuthenticated = true, ClientPublicId = Guid.NewGuid(), PackageFamilyName = "Mizon.Test_abcd1234", LanguageTag = "en-US" };
string Reply(TaskbarDeepLinkRequest request, bool success = true) => AppDeepLinkResultProtocol.Serialize(new()
{
    RequestId = request.RequestId, Success = success, Code = success ? "opened" : "not_found",
    Message = success ? "Opened." : "Unavailable.",
    Data = success ? new() { Destination = "profile", Uri = request.Uri.AbsoluteUri, PublicId = profile.ClientPublicId } : null
});

var transport = new FakeTransport();
var clock = new ManualClock();
using var activation = new TaskbarProfileActivation(transport, clock);
var changes = 0;
activation.Changed += (_, _) => changes++;
Check((await activation.OpenAsync()).Code == "canceled" && transport.Requests.Count == 0, "No account cannot activate");
activation.ApplyProfile(profile);
var first = activation.OpenAsync();
Check(activation.IsBusy && activation.Outcome is null && changes == 1, "One in-flight request owns busy presentation");
Check(transport.Requests[0].Uri.AbsoluteUri == $"mizon://client/{profile.ClientPublicId:D}" && transport.Requests[0].PackageFamilyName == profile.PackageFamilyName,
    "Caller sends the actual profile route and exact target package");
Check((await activation.OpenAsync()).Code == "busy" && transport.Requests.Count == 1, "Repeated clicks do not launch duplicate requests");
activation.ApplyProfile(profile with { Revision = 20, IsOnline = false, LanguageTag = "fa-IR", ImagePath = "synthetic.jpg" });
Check(activation.IsBusy && !transport.Tokens[0].IsCancellationRequested, "Heartbeat, language, presence and artwork changes do not cancel navigation");
transport.Completions[0].SetResult(new(TaskbarLaunchStatus.Success, Reply(transport.Requests[0])));
Check((await first).Success && !activation.IsBusy && activation.Outcome?.Success == true && changes == 2, "Correlated receiver success ends loading");
Check(clock.Timers.All(timer => timer.Disposed), "Completion releases deadline timer");

async Task CheckReply(string? json, TaskbarLaunchStatus status, string expected, bool success = false)
{
    transport.Handler = request => Task.FromResult(new TaskbarDeepLinkResponse(status, json == "$valid" ? Reply(request, success) : json));
    var outcome = await activation.OpenAsync();
    Check(outcome.Code == expected && outcome.Success == success && !activation.IsBusy, $"Transport/result outcome {expected}");
}
await CheckReply(null, TaskbarLaunchStatus.Success, "invalid_response");
await CheckReply("", TaskbarLaunchStatus.Success, "invalid_response");
await CheckReply("{}", TaskbarLaunchStatus.Success, "invalid_response");
await CheckReply(Reply(new(Guid.NewGuid(), new Uri("mizon://music"), profile.PackageFamilyName!)), TaskbarLaunchStatus.Success, "invalid_response");
await CheckReply(new string('x', AppDeepLinkResultProtocol.MaximumJsonBytes + 1), TaskbarLaunchStatus.Success, "invalid_response");
await CheckReply(null, TaskbarLaunchStatus.AppUnavailable, "app_unavailable");
await CheckReply(null, TaskbarLaunchStatus.ProtocolUnavailable, "protocol_unavailable");
await CheckReply(null, TaskbarLaunchStatus.Unknown, "launch_failed");
await CheckReply("$valid", TaskbarLaunchStatus.Success, "not_found");
transport.Handler = request => Task.FromResult(new TaskbarDeepLinkResponse(TaskbarLaunchStatus.Success, Reply(request).Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal)));
Check((await activation.OpenAsync()).Code == "invalid_response", "Wrong envelope version fails closed");
transport.Handler = _ => throw new IOException("Private native diagnostic must not reach UI");
Check((await activation.OpenAsync()).Code == "launch_failed" && !activation.IsBusy, "Synchronous native exception releases loading with sanitized failure");
transport.Handler = _ => Task.FromException<TaskbarDeepLinkResponse>(new InvalidOperationException("Private failure"));
Check((await activation.OpenAsync()).Code == "launch_failed", "Asynchronous native exception is observed and sanitized");

transport.Handler = null;
var timed = activation.OpenAsync();
var timedIndex = transport.Requests.Count - 1;
clock.Timers[^1].Fire();
Check((await timed).Code == "timeout" && !activation.IsBusy && transport.Tokens[timedIndex].IsCancellationRequested, "Sixty-second deadline ends a non-cooperative native operation");
transport.Completions[timedIndex].SetResult(new(TaskbarLaunchStatus.Success, Reply(transport.Requests[timedIndex])));
Check(activation.Outcome?.Code == "timeout", "Late success cannot overwrite timeout");
Check(clock.Timers[^1].DueTime == TimeSpan.FromSeconds(60) && clock.Timers[^1].Disposed, "Deadline is sixty seconds and disposed");

var old = activation.OpenAsync();
var oldIndex = transport.Requests.Count - 1;
activation.ApplyProfile(profile with { ClientPublicId = Guid.NewGuid() });
Check(!activation.IsBusy && activation.Outcome is null && transport.Tokens[oldIndex].IsCancellationRequested, "Changing accounts clears busy state and cancels old native work");
var newer = activation.OpenAsync();
var newIndex = transport.Requests.Count - 1;
Check((await old).Code == "canceled" && activation.IsBusy, "Old completion cannot clear a newer request's busy state");
transport.Completions[oldIndex].SetResult(new(TaskbarLaunchStatus.Success, Reply(transport.Requests[oldIndex])));
Check(activation.IsBusy && activation.Outcome is null, "Late old-account reply cannot update current presentation");
activation.ApplyProfile(profile with { IsAuthenticated = false });
Check((await newer).Code == "canceled" && !activation.IsBusy, "Sign-out cancels current activation");
Check((await activation.OpenAsync()).Code == "canceled", "Signed-out clicks cannot launch");
activation.ApplyProfile(profile);
var destroyed = activation.OpenAsync();
activation.Cancel();
Check((await destroyed).Code == "canceled" && !activation.IsBusy && activation.Outcome is null, "Island destruction cancels and clears UI outcome");

foreach (var missing in new[] { null, "", "bad", "bad package_name", "Mizon_\n", new string('a', 256) + "_b" })
{
    var before = transport.Requests.Count;
    activation.ApplyProfile(profile with { PackageFamilyName = missing });
    Check((await activation.OpenAsync()).Code == "app_unavailable" && !activation.IsBusy && transport.Requests.Count == before,
        "Missing/corrupt/legacy package identity never launches or assumes success");
}
activation.ApplyProfile(profile with { ClientPublicId = null });
transport.Handler = request => Task.FromResult(new TaskbarDeepLinkResponse(TaskbarLaunchStatus.Success, Reply(request)));
Check((await activation.OpenAsync()).Success && transport.Requests[^1].Uri.AbsoluteUri == "mizon://music/", "Unknown profile ID preserves the music-home fallback route");
activation.ApplyProfile(profile);
transport.Handler = null;
var disposed = activation.OpenAsync();
var beforeDispose = changes;
activation.Dispose();
Check((await disposed).Code == "canceled" && !activation.IsBusy && changes == beforeDispose, "Disposal cancels without notifying released UI");
Check((await activation.OpenAsync()).Code == "disposed", "Disposed owner cannot restart activation");

foreach (var tag in new[] { "fa-IR", "en-US" })
{
    Check(!string.IsNullOrWhiteSpace(TaskbarActivationText.Status(tag, true, null)), "Localized loading exists");
    foreach (var code in new[] { "timeout", "timed_out", "app_unavailable", "protocol_unavailable", "invalid_response", "not_found", "authentication_required",
        "requires_sign_in", "profile_unavailable", "app_closed", "unavailable", "unexpected" })
        Check(!string.IsNullOrWhiteSpace(TaskbarActivationText.Status(tag, false, new(code))), "Localized error exists");
    Check(TaskbarActivationText.Status(tag, false, new("requires_sign_in")) == TaskbarActivationText.Status(tag, false, new("authentication_required")),
        "Actual receiver sign-in code retains the specific localized guidance");
    Check(TaskbarActivationText.Status(tag, false, new("timed_out")) == TaskbarActivationText.Status(tag, false, new("timeout")),
        "Receiver and local deadlines share specific localized timeout guidance");
    Check(TaskbarActivationText.Status(tag, false, new("canceled")) is null, "Lifecycle cancellation is not announced as a user error");
}
Check(TaskbarActivationText.Status("en-US", false, new("private exception text")) == TaskbarActivationText.Status("en-US", false, new("unexpected")),
    "Unrecognized response cannot inject error or exception text into the UI");
var snapshotJson = JsonSerializer.Serialize(profile, TaskbarJsonContext.Default.ProfileSnapshot);
Check(JsonSerializer.Deserialize(snapshotJson, TaskbarJsonContext.Default.ProfileSnapshot)?.PackageFamilyName == profile.PackageFamilyName,
    "Version-one source-generated IPC preserves target package identity");
Check(JsonSerializer.Deserialize("{\"version\":1,\"isAuthenticated\":true}", TaskbarJsonContext.Default.ProfileSnapshot)?.PackageFamilyName is null,
    "Legacy snapshots remain readable and cannot invent a target package");

var boundaryTransport = new FakeTransport();
var boundaryClock = new ManualClock();
using var boundary = new TaskbarProfileActivation(boundaryTransport, boundaryClock);
var observedChanges = 0;
boundary.Changed += (_, _) => throw new ArgumentException("Synthetic disconnected UI observer");
boundary.Changed += (_, _) => ++observedChanges;
boundary.ApplyProfile(profile);
var observed = boundary.OpenAsync();
Check(boundary.IsBusy && observedChanges == 1, "A throwing presentation observer does not prevent native launch or other observers");
boundaryTransport.Completions[^1].SetResult(new(TaskbarLaunchStatus.Success, Reply(boundaryTransport.Requests[^1])));
Check((await observed).Success && !boundary.IsBusy && observedChanges == 2, "Throwing completion observer cannot retain a disposed CTS or busy state");
boundary.ApplyProfile(profile with { PackageFamilyName = null });
Check((await boundary.OpenAsync()).Code == "app_unavailable" && !boundary.IsBusy, "Invalid-target notification cannot fault the public async operation");
boundary.ApplyProfile(profile);
var callbackCount = 0;
boundaryTransport.Handler = _ =>
{
    boundaryTransport.Tokens[^1].Register(() => { ++callbackCount; throw new InvalidOperationException("Synthetic native cancellation failure"); });
    return boundaryTransport.Completions[^1].Task;
};
var explicitCancellation = boundary.OpenAsync();
boundary.Cancel();
Check((await explicitCancellation).Code == "canceled" && !boundary.IsBusy && callbackCount == 1, "Throwing native cancellation callback cannot break explicit cleanup");
var throwingDeadline = boundary.OpenAsync();
boundaryClock.Timers[^1].Fire();
Check((await throwingDeadline).Code == "timeout" && !boundary.IsBusy && callbackCount == 2, "Throwing native cancellation callback cannot escape the deadline timer");
var finallyCancellation = boundary.OpenAsync();
boundaryTransport.Completions[^1].SetResult(new(TaskbarLaunchStatus.Success, Reply(boundaryTransport.Requests[^1])));
Check((await finallyCancellation).Success && !boundary.IsBusy && callbackCount == 3, "Throwing cancellation callback in finally still releases successful request ownership");
var throwingDisposal = boundary.OpenAsync();
boundary.Dispose();
Check((await throwingDisposal).Code == "canceled" && !boundary.IsBusy && callbackCount == 4, "Throwing cancellation callback cannot interrupt disposal");
Check(boundaryClock.Timers.All(timer => timer.Disposed), "Every exceptional-boundary request releases its deadline timer");
var disposalTransport = new FakeTransport();
using var observerDisposal = new TaskbarProfileActivation(disposalTransport, new ManualClock());
var lateNotifications = 0;
observerDisposal.Changed += (_, _) => observerDisposal.Dispose();
observerDisposal.Changed += (_, _) => ++lateNotifications;
observerDisposal.ApplyProfile(profile);
Check((await observerDisposal.OpenAsync()).Code == "canceled" && !observerDisposal.IsBusy && disposalTransport.Requests.Count == 0,
    "Disposal during the first presentation notification prevents native launch");
Check(lateNotifications == 0, "An observer disposing the owner prevents later observers from touching released UI");
Console.WriteLine($"PASS {count} taskbar activation lifecycle, strict response, deadline, localization and IPC assertions.");

internal sealed class FakeTransport : ITaskbarDeepLinkTransport
{
    internal readonly List<TaskbarDeepLinkRequest> Requests = [];
    internal readonly List<CancellationToken> Tokens = [];
    internal readonly List<TaskCompletionSource<TaskbarDeepLinkResponse>> Completions = [];
    internal Func<TaskbarDeepLinkRequest, Task<TaskbarDeepLinkResponse>>? Handler;
    public Task<TaskbarDeepLinkResponse> LaunchAsync(TaskbarDeepLinkRequest request, CancellationToken token)
    {
        Requests.Add(request); Tokens.Add(token);
        var completion = new TaskCompletionSource<TaskbarDeepLinkResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        Completions.Add(completion);
        return Handler?.Invoke(request) ?? completion.Task;
    }
}

internal sealed class ManualClock : TimeProvider
{
    internal readonly List<ManualTimer> Timers = [];
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, dueTime);
        Timers.Add(timer);
        return timer;
    }
    internal sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        internal TimeSpan DueTime = dueTime;
        internal bool Disposed;
        internal void Fire() { if (!Disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) { DueTime = dueTime; return !Disposed; }
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
