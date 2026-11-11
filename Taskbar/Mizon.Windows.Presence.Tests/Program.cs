using Mizon.Windows.Controls;
using Mizon.Windows.Taskbar;
using System.Text.Json;

var assertions = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    assertions++;
}
var now = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc).Ticks;
var snapshot = new ProfileSnapshot
{
    IsAuthenticated = true, IsOnline = true, MainProcessId = 100,
    MainProcessStartTimeUtcTicks = now - TimeSpan.FromMinutes(1).Ticks,
    PresenceUpdatedAtUtcTicks = now, DisplayName = "Synthetic account",
    OnlinePresenceLabel = "Online", OfflinePresenceLabel = "Offline", LanguageTag = "en-US"
};
var processes = new FakeProcessFactory();
var changes = 0;
using var monitor = new TaskbarPresenceMonitor(() => changes++, processes.Watch);
monitor.Apply(new() { IsAuthenticated = true }, now);
Check(!monitor.IsOnline && processes.Created.Count == 0, "Legacy authentication cannot infer online presence");
monitor.Apply(snapshot, now);
Check(monitor.IsOnline && changes == 1 && processes.Created.Count == 1, "Fresh server snapshot and verified process are online");
var first = processes.Created[0];
monitor.Apply(snapshot with { PresenceUpdatedAtUtcTicks = now + TimeSpan.FromSeconds(10).Ticks }, now + TimeSpan.FromSeconds(10).Ticks);
Check(monitor.IsOnline && processes.Created.Count == 1 && changes == 1, "Heartbeat reuses one process watch without presentation churn");
monitor.Evaluate(now + TimeSpan.FromSeconds(40).Ticks);
Check(monitor.IsOnline, "Thirty-second lease boundary remains valid");
monitor.Evaluate(now + TimeSpan.FromSeconds(40).Ticks + 1);
Check(!monitor.IsOnline && first.Disposed && changes == 2, "Expired heartbeat becomes offline and releases owner watch");
monitor.Evaluate(now + TimeSpan.FromSeconds(41).Ticks);
Check(processes.Created.Count == 1 && changes == 2, "Expiry timer does not reopen processes or repeat notifications");

var fresh = snapshot with { PresenceUpdatedAtUtcTicks = now + TimeSpan.FromSeconds(42).Ticks };
monitor.Apply(fresh, fresh.PresenceUpdatedAtUtcTicks);
Check(monitor.IsOnline && processes.Created.Count == 2, "A new heartbeat can renew the same live app");
var second = processes.Created[1];
second.Exit();
Check(!monitor.IsOnline && second.Disposed, "Process exit immediately invalidates presence without waiting for expiry");
monitor.Apply(fresh with { IsOnline = false }, fresh.PresenceUpdatedAtUtcTicks);
Check(!monitor.IsOnline && processes.Created.Count == 2, "Server offline remains offline even while authenticated");
processes.RefuseOwner = true;
monitor.Apply(fresh, fresh.PresenceUpdatedAtUtcTicks);
Check(!monitor.IsOnline, "Unverifiable or recycled PID cannot show online");
processes.RefuseOwner = false;
monitor.Apply(fresh with { MainProcessId = 101 }, fresh.PresenceUpdatedAtUtcTicks);
Check(monitor.IsOnline && processes.LastId == 101 && processes.LastStart == fresh.MainProcessStartTimeUtcTicks,
    "Factory receives exact PID/start identity for a new process");
var third = processes.Created[^1];
first.SignalLateExit();
Check(monitor.IsOnline, "Old owner callbacks cannot invalidate a renewed generation");
monitor.Apply(fresh with { MainProcessId = 101, MainProcessStartTimeUtcTicks = now }, fresh.PresenceUpdatedAtUtcTicks);
Check(monitor.IsOnline && third.Disposed && processes.Created.Count == 4, "Changed start time replaces a recycled PID watch");
monitor.Apply(fresh with { IsAuthenticated = false }, fresh.PresenceUpdatedAtUtcTicks);
Check(!monitor.IsOnline && processes.Created[^1].Disposed, "Sign-out clears online state and releases ownership");

foreach (var invalid in new[]
{
    snapshot with { MainProcessId = 0 }, snapshot with { MainProcessId = -1 },
    snapshot with { MainProcessStartTimeUtcTicks = 0 }, snapshot with { MainProcessStartTimeUtcTicks = now + 1 },
    snapshot with { PresenceUpdatedAtUtcTicks = 0 }, snapshot with { PresenceUpdatedAtUtcTicks = now + 1 },
    snapshot with { PresenceUpdatedAtUtcTicks = snapshot.MainProcessStartTimeUtcTicks - 1 },
    snapshot with { PresenceUpdatedAtUtcTicks = long.MaxValue }
}) Check(!TaskbarPresenceMonitor.IsFresh(invalid, now), "Invalid/future/older-than-owner heartbeats fail closed");
Check(!TaskbarPresenceMonitor.IsFresh(snapshot, DateTime.MaxValue.Ticks + 1), "Out-of-range clock fails closed");
monitor.Apply(snapshot, now);
monitor.Clear();
Check(!monitor.IsOnline && processes.Created[^1].Disposed, "Unreadable snapshot clears presence while host can retain artwork");
monitor.Apply(snapshot, now);
var disposedWatch = processes.Created[^1];
monitor.Dispose();
var before = changes;
disposedWatch.SignalLateExit();
monitor.Apply(snapshot, now);
monitor.Evaluate(now);
monitor.Clear();
Check(!monitor.IsOnline && disposedWatch.Disposed && changes == before, "Disposed monitor cannot notify or revive from late callbacks");

var json = JsonSerializer.Serialize(snapshot, TaskbarJsonContext.Default.ProfileSnapshot);
var restored = JsonSerializer.Deserialize(json, TaskbarJsonContext.Default.ProfileSnapshot)!;
Check(restored == snapshot, "Source-generated snapshot preserves freshness identity and localized labels");
var legacy = JsonSerializer.Deserialize("{\"version\":1,\"isAuthenticated\":true}", TaskbarJsonContext.Default.ProfileSnapshot)!;
Check(!TaskbarPresenceMonitor.IsFresh(legacy, now), "Old IPC schema safely renders offline");
foreach (var size in new[] { 16d, 24, 32, 48, 64, 128 })
{
    var geometry = PresenceAvatarMetrics.ForSize(size);
    Check(geometry.BadgeCenter > size / 2 && geometry.BadgeCenter + geometry.BadgeDiameter / 2 <= size,
        "Badge stays at physical bottom right and inside the reserved avatar bounds");
    Check(geometry.CutoutRadius > geometry.BadgeDiameter / 2,
        "Transparent exclusion extends beyond the badge; there is no painted background ring");
    Check(geometry.BadgeCenter - geometry.CutoutRadius > size / 2,
        "Cutout preserves the central portrait");
}
foreach (var size in new[] { 0d, -1, double.NaN, double.PositiveInfinity, double.MaxValue })
    Check(PresenceAvatarMetrics.ForSize(size) == default, "Invalid geometry does not reach native composition");
Console.WriteLine($"PASS: {assertions} avatar presence checks. No app, shell, account or backend was accessed.");

sealed class FakeProcessFactory
{
    internal readonly List<FakeProcessWatch> Created = [];
    internal bool RefuseOwner;
    internal int LastId;
    internal long LastStart;
    internal IPresenceProcessWatch? Watch(int id, long start, Action exited)
    {
        LastId = id; LastStart = start;
        if (RefuseOwner) return null;
        var watch = new FakeProcessWatch(exited);
        Created.Add(watch);
        return watch;
    }
}
sealed class FakeProcessWatch(Action exited) : IPresenceProcessWatch
{
    internal bool Disposed;
    public bool HasExited { get; private set; }
    internal void Exit() { HasExited = true; exited(); }
    internal void SignalLateExit() => exited();
    public void Dispose() => Disposed = true;
}
