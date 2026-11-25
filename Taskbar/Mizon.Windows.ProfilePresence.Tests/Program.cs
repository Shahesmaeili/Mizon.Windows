using System.Text.Json;
using Mizon.Windows.Services;

var count = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    ++count;
}

var transport = new FakeTransport();
var clock = new TestClock();
var publications = new List<(long Session, ProfilePresenceState State)>();
var created = 0;
var errors = 0;
var service = new ProfilePresenceService(() => { ++created; return transport; },
    (session, state) => publications.Add((session, state)), _ => ++errors, clock);
Check(service.ReadState() == new ProfilePresenceState(false, false, false, false, false), "Initial offline state");
service.SetWindowActive(true);
Check(created == 0, "Welcome activity does not allocate SignalR");
service.SetSession(true, 1);
Check(created == 1 && transport.Intent, "Authenticated focus becomes desired presence");
Check(!service.ReadState().IsOnline && !service.ReadState().HasConfirmedState, "Intent alone is never online");
transport.SetState(true, true);
Check(service.ReadState().IsOnline && service.ReadState().HasConfirmedState, "Matching server confirmation becomes online");
var calls = transport.CallCount;
var publicationsBeforeLease = publications.Count;
clock.Tick();
Check(publications.Count == publicationsBeforeLease + 1 && transport.CallCount == calls, "IPC heartbeat does not create network requests");
service.SetWindowActive(false);
Check(!service.ReadState().IsOnline && !service.ReadState().HasConfirmedState, "Defocus immediately clears online before server ack");
transport.SetState(true, true);
Check(!service.ReadState().IsOnline, "Late online response cannot override deactivation");
transport.SetState(true, false);
Check(service.ReadState().HasConfirmedState && !service.ReadState().IsOnline, "Offline acknowledgement is synchronized");
var idlePublications = publications.Count;
clock.Tick();
Check(publications.Count == idlePublications, "No heartbeat work while offline");
service.SetWindowActive(true);
transport.SetState(true, true);
transport.SetState(false, null);
Check(!service.ReadState().IsOnline && !service.ReadState().IsConnected, "Disconnect invalidates online state");
transport.SetState(true, null);
Check(!service.ReadState().IsOnline, "Reconnected transport alone is not confirmation");
transport.SetState(true, true);
Check(service.ReadState().IsOnline, "Fresh reconnect confirmation restores online");
for (var i = 0; i < 100; i++) service.SetWindowActive(i % 2 == 0);
transport.SetState(true, true);
Check(!service.ReadState().IsOnline && !transport.Intent, "Rapid toggles keep newest inactive state");
calls = transport.CallCount;
service.SetWindowActive(false);
Check(transport.CallCount == calls, "Duplicate native activation is deduplicated");
service.SetSession(false, 1);
transport.SetState(true, true);
Check(!service.ReadState().IsOnline && !service.ReadState().HasSession, "Signed-out server callback cannot restore badge");
service.SetWindowActive(true);
service.SetSession(true, 2);
Check(!service.ReadState().HasConfirmedState, "New account starts unconfirmed");
service.RefreshSynchronization();
Check(transport.Intent, "Re-login restores focus intent after previous socket stop");
transport.SetState(true, true);
Check(publications[^1].Session == 2, "Publication belongs to current account session");
service.Dispose();
Check(transport.Disposed && transport.SubscriberCount == 0 && clock.Disposed, "Close releases transport subscriptions and heartbeat");
Check(!publications[^1].State.IsOnline, "Close publishes offline to surviving companion");
try { service.ReadState(); throw new Exception("Disposed state unexpectedly readable"); }
catch (ObjectDisposedException) { count++; }
service.Dispose();
Check(errors == 0, "Normal lifecycle has no unobserved errors");

var sharedTransport = new FakeTransport();
var sharedClock = new TestClock();
var sharedPublications = new List<(long Session, ProfilePresenceState State)>();
using (var shared = new ProfilePresenceService(() => sharedTransport,
    (session, state) => sharedPublications.Add((session, state)), _ => ++errors, sharedClock))
{
    shared.SetSession(true, 10);
    Check(sharedTransport.WatchCount == 0, "Account watch waits for the authenticated hub connection");
    sharedTransport.SetState(true, false);
    Check(sharedTransport.WatchCount == 1 && !sharedTransport.Intent,
        "An inactive Windows window still subscribes to the account");
    sharedTransport.SetAccountState(true);
    Check(shared.ReadState() == new ProfilePresenceState(true, false, true, true, true),
        "Another device confirms the account online while Windows is inactive");
    Check(sharedPublications[^1].State.IsOnline,
        "The same account snapshot marks the Windows taskbar publisher online");
    var reportsBeforeLease = sharedTransport.CallCount;
    var publicationsAtRemoteOnline = sharedPublications.Count;
    sharedClock.Tick();
    Check(sharedPublications.Count == publicationsAtRemoteOnline + 1 && sharedTransport.CallCount == reportsBeforeLease,
        "Remote-only presence renews local taskbar IPC without sending window focus again");

    shared.SetWindowActive(true);
    Check(sharedTransport.Intent && shared.ReadState().IsOnline, "Focus gain keeps account presence while reporting local activity");
    shared.SetWindowActive(false);
    sharedTransport.SetState(true, false);
    Check(!sharedTransport.Intent && shared.ReadState().IsOnline,
        "Deactivating Windows does not override another active device");
    sharedTransport.SetAccountState(false);
    Check(!shared.ReadState().IsOnline && shared.ReadState().HasConfirmedState,
        "The final device going inactive turns the confirmed account indicator off");

    sharedTransport.SetAccountState(true);
    sharedTransport.SetState(false, null);
    Check(!shared.ReadState().IsOnline && !shared.ReadState().HasConfirmedState, "Disconnect clears the account confirmation");
    sharedTransport.SetState(true, null);
    Check(sharedTransport.WatchCount == 2 && !shared.ReadState().IsOnline, "Reconnect watches again before showing fresh online state");
    sharedTransport.SetAccountState(true);
    Check(shared.ReadState().IsOnline, "The reconnected account watch updates an inactive window");
    shared.SetSession(false, 10);
    sharedTransport.SetAccountState(true);
    Check(!shared.ReadState().IsOnline && !shared.ReadState().HasSession,
        "Signing out clears presence and rejects later account events");
}

sharedTransport = new FakeTransport { FailWatch = true };
sharedTransport.SetState(true, null);
using (var legacy = new ProfilePresenceService(() => sharedTransport, (_, _) => { }, _ => { }))
{
    legacy.SetSession(true, 11);
    legacy.SetWindowActive(true);
    sharedTransport.SetState(true, true);
    Check(legacy.ReadState().IsOnline && legacy.ReadState().HasConfirmedState,
        "Unavailable account watch keeps legacy local focus confirmation");
    legacy.SetWindowActive(false);
    Check(!legacy.ReadState().IsOnline && sharedTransport.WatchCount == 1,
        "An older hub is not re-probed on each focus change");
}

var unfinishedWatch = new TaskCompletionSource();
var nextWatch = new TaskCompletionSource();
var oldWatchToken = CancellationToken.None;
var replacementTransport = new FakeTransport
{
    OnWatch = token =>
    {
        oldWatchToken = token;
        return unfinishedWatch.Task;
    }
};
replacementTransport.SetState(true, null);
using (var replacement = new ProfilePresenceService(() => replacementTransport, (_, _) => { }, _ => { }))
{
    replacement.SetSession(true, 12);
    replacementTransport.SetAccountState(true);
    Check(!replacement.ReadState().IsOnline, "A pending subscription does not publish a retained account snapshot");
    replacement.SetSession(false, 12);
    Check(oldWatchToken.IsCancellationRequested, "Logout cancels the old account subscription");
    replacementTransport.OnWatch = _ => nextWatch.Task;
    replacement.SetSession(true, 13);
    unfinishedWatch.SetResult();
    Check(!replacement.ReadState().IsOnline, "Completion from before logout cannot confirm the new session");
    replacementTransport.SetAccountState(false);
    nextWatch.SetResult();
    Check(replacement.ReadState().HasConfirmedState && !replacement.ReadState().IsOnline,
        "The new subscription confirms its own current snapshot");
}

var remoteOnlineTransport = new FakeTransport();
var remoteOnlineClock = new TestClock();
remoteOnlineTransport.SetState(true, false);
using (var remoteOnline = new ProfilePresenceService(() => remoteOnlineTransport, (_, _) => { },
    _ => { }, remoteOnlineClock))
{
    remoteOnline.SetSession(true, 14);
    remoteOnlineTransport.SetAccountState(true);
    remoteOnlineTransport.Fail = true;
    remoteOnline.SetWindowActive(true);
    var focusReports = remoteOnlineTransport.CallCount;
    remoteOnlineTransport.Fail = false;
    remoteOnlineClock.Tick();
    Check(remoteOnlineTransport.CallCount == focusReports + 1 && remoteOnlineTransport.Intent
        && remoteOnline.ReadState().IsOnline,
        "Account confirmation leaves local focus retries enabled");
}

var failedTransport = new FakeTransport { Fail = true };
using (var failed = new ProfilePresenceService(() => failedTransport, (_, _) => { }, _ => ++errors))
{
    failed.SetWindowActive(true);
    failed.SetSession(true, 3);
    Check(!failed.ReadState().IsOnline && errors > 0, "Failed publication remains safely offline");
}

var pendingTransport = new FakeTransport { WaitForCancellation = true };
var pending = new ProfilePresenceService(() => pendingTransport, (_, _) => { }, _ => ++errors);
pending.SetSession(true, 4);
pending.Dispose();
await pendingTransport.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
Check(pendingTransport.Disposed, "Close cancels pending presence update");

var retryTransport = new FakeTransport { Fail = true };
retryTransport.SetState(true, null);
var retryClock = new TestClock();
using (var retry = new ProfilePresenceService(() => retryTransport, (_, _) => { }, _ => { }, retryClock))
{
    retry.SetWindowActive(true);
    retry.SetSession(true, 5);
    var queuedRetry = retryClock.CaptureQueuedTick();
    Check(!retry.ReadState().IsOnline, "Failed connected invocation stays offline");
    retryTransport.Fail = false;
    retryClock.Tick();
    Check(retryTransport.CallCount == 2 && retryTransport.Intent, "Transient invocation failure retries latest active intent");
    retryTransport.SetState(true, true);
    Check(retry.ReadState().IsOnline, "Retry acknowledgement recovers presence without a focus toggle");
    var recoveredCalls = retryTransport.CallCount;
    queuedRetry();
    Check(retryTransport.CallCount == recoveredCalls && retry.ReadState().IsOnline, "A disposed retry already queued cannot invalidate a confirmed state");
}
retryTransport = new FakeTransport { Fail = true };
retryTransport.SetState(true, null);
retryClock = new TestClock();
using (var retry = new ProfilePresenceService(() => retryTransport, (_, _) => { }, _ => { }, retryClock))
{
    retry.SetWindowActive(true);
    retry.SetSession(true, 6);
    var queuedRetry = retryClock.CaptureQueuedTick();
    retryTransport.SetState(true, true);
    queuedRetry();
    Check(retryTransport.CallCount == 1 && retry.ReadState().IsOnline,
        "A confirmation racing an already queued retry preserves the same confirmed revision");
}
retryTransport = new FakeTransport { Fail = true };
retryTransport.SetState(true, null);
retryClock = new TestClock();
using (var retry = new ProfilePresenceService(() => retryTransport, (_, _) => { }, _ => { }, retryClock))
{
    retry.SetWindowActive(true);
    retry.SetSession(true, 6);
    for (var i = 0; i < 8; i++) retryClock.Tick();
    Check(retryTransport.CallCount == 4, "Permanent errors stop after three bounded retries");
    retry.SetWindowActive(false);
    retryTransport.Fail = false;
    retryClock.Tick();
    Check(!retryTransport.Intent && !retry.ReadState().IsOnline, "A queued retry cannot undo newer deactivation");
}

var fixturePath = Path.Combine(Path.GetTempPath(), "MizonPresence-" + Guid.NewGuid().ToString("N"));
var publisher = new TaskbarProfilePublisher(fixturePath, Path.Combine(fixturePath, "missing-payload"));
try
{
    publisher.SetPackageFamilyName("Mizon.Test_abcd1234");
    publisher.SynchronizeAuthentication("synthetic-token-1");
    var session = publisher.CurrentSession;
    publisher.SetPresenceLocalization("en-US", "Online", "Offline");
    publisher.PublishPresence(session, true); // Confirmation may arrive before avatar download.
    publisher.PublishProfile(session, "Test profile", null);
    await publisher.WaitForIdleAsync();
    using (var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixturePath, "profile.json"))))
    {
        var root = snapshot.RootElement;
        Check(root.GetProperty("packageFamilyName").GetString() == "Mizon.Test_abcd1234", "Current installed package identity is published without changing the IPC version");
        Check(root.GetProperty("isOnline").GetBoolean(), "Late avatar publication preserves presence");
        Check(root.GetProperty("mainProcessId").GetInt32() == Environment.ProcessId && root.GetProperty("mainProcessStartTimeUtcTicks").GetInt64() > 0, "Companion receives verified process lifetime identity");
        Check(root.GetProperty("presenceUpdatedAtUtcTicks").GetInt64() > 0 && root.GetProperty("offlinePresenceLabel").GetString() == "Offline", "Lease and independent offline Narrator label published");
    }
    publisher.SetPackageFamilyName("Mizon.Reinstalled_abcd1234");
    await publisher.WaitForIdleAsync();
    using (var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixturePath, "profile.json"))))
        Check(snapshot.RootElement.GetProperty("packageFamilyName").GetString() == "Mizon.Reinstalled_abcd1234"
            && snapshot.RootElement.GetProperty("isAuthenticated").GetBoolean(), "Package identity refresh preserves the current authenticated account");
    publisher.PublishMood(session, Guid.NewGuid(), "🎵", "Music", null, null);
    publisher.PublishProfile(session, "Refreshed profile", null);
    publisher.PublishPresence(session, false);
    await publisher.WaitForIdleAsync();
    using (var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixturePath, "profile.json"))))
        Check(!snapshot.RootElement.GetProperty("isOnline").GetBoolean() && snapshot.RootElement.GetProperty("moodEmoji").GetString() == "🎵", "Presence does not erase mood/profile and final offline wins");
    publisher.SynchronizeAuthentication("synthetic-token-2");
    publisher.PublishPresence(session, true);
    publisher.PublishProfile(publisher.CurrentSession, "Second profile", null);
    await publisher.WaitForIdleAsync();
    using (var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixturePath, "profile.json"))))
        Check(!snapshot.RootElement.GetProperty("isOnline").GetBoolean(), "Old account lease cannot mark new account online");
    publisher.SynchronizeAuthentication(null);
    await publisher.WaitForIdleAsync();
    using (var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixturePath, "profile.json"))))
        Check(!snapshot.RootElement.GetProperty("isAuthenticated").GetBoolean() && !snapshot.RootElement.GetProperty("isOnline").GetBoolean(), "Logout clears durable companion presence");
}
finally
{
    // This exact directory was generated above solely for these synthetic fixtures.
    if (Directory.Exists(fixturePath)) Directory.Delete(fixturePath, recursive: true);
}
Console.WriteLine($"PASS {count} profile presence focus/confirmation, reconnect, account, cancellation, cleanup and IPC assertions.");

internal sealed class FakeTransport : IProfilePresenceTransport
{
    public event EventHandler? StateChanged;
    public int SubscriberCount => StateChanged?.GetInvocationList().Length ?? 0;
    public int CallCount;
    public int WatchCount;
    public bool Intent, Disposed, Fail, WaitForCancellation;
    public bool FailWatch;
    public Func<CancellationToken, Task>? OnWatch;
    public TaskCompletionSource Canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ProfilePresenceTransportState _state;
    public ProfilePresenceTransportState ReadState() => _state;
    public Task SetWindowPresenceAsync(bool isActive, CancellationToken token)
    {
        ++CallCount;
        Intent = isActive;
        _state = _state with { ConfirmedWindowOnline = null };
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (Fail) throw new IOException("Synthetic presence send failed");
        return WaitForCancellation ? WaitAsync(token) : Task.CompletedTask;
    }
    public Task WatchAccountPresenceAsync(CancellationToken token)
    {
        ++WatchCount;
        return FailWatch ? Task.FromException(new IOException("Synthetic older hub"))
            : OnWatch?.Invoke(token) ?? Task.CompletedTask;
    }
    private async Task WaitAsync(CancellationToken token)
    {
        try { await Task.Delay(Timeout.Infinite, token); }
        finally { Canceled.TrySetResult(); }
    }
    public void SetState(bool connected, bool? online)
    {
        _state = new(connected, online, connected ? _state.AccountOnline : null);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SetAccountState(bool? online)
    {
        _state = _state with { AccountOnline = online };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() { Disposed = true; StateChanged = null; }
}

internal sealed class TestClock : TimeProvider
{
    private TestTimer? _timer;
    public bool Disposed => _timer?.Disposed ?? false;
    public void Tick() => _timer?.Tick();
    public Action CaptureQueuedTick() => _timer is { } timer ? timer.InvokeQueued : () => { };
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        _timer = new(callback, state, dueTime);
    private sealed class TestTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        private bool _enabled = dueTime != Timeout.InfiniteTimeSpan;
        public bool Disposed;
        public bool Change(TimeSpan due, TimeSpan period) { _enabled = due != Timeout.InfiniteTimeSpan; return true; }
        public void Tick() { if (!Disposed && _enabled) callback(state); }
        public void InvokeQueued() => callback(state);
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

namespace Mizon.Windows.Services
{
    internal static class AppLog { internal static void Information(string message) { } }
}
