using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;

var passed = 0;
await Check("active lifetime owns observers and honors reduced motion", () =>
{
    var environment = new TestEnvironment();
    using var viewModel = new VPNPageViewModel(environment);
    viewModel.Activate();
    viewModel.Activate();
    Require(environment.Starts == 1 && environment.Observers == 1, "Activation is idempotent.");
    Require(viewModel.IsLoading && !viewModel.IsParticleAnimationEnabled, "Loading has no speculative motion.");
    environment.Publish(new(true, "internet", true));
    Require(!viewModel.IsLoading && viewModel.NetworkStatus == "internet" && viewModel.IsParticleAnimationEnabled, "Initial snapshot is visible.");
    Require(!viewModel.IsConfigured && !viewModel.IsConnected, "Internet access must never claim a VPN connection.");
    environment.Publish(new(true, "local", false));
    Require(viewModel.NetworkStatus == "local" && !viewModel.IsParticleAnimationEnabled, "Reduced motion takes effect immediately.");
    viewModel.Deactivate();
    viewModel.Deactivate();
    Require(environment.Stops == 1 && environment.Observers == 0 && !viewModel.IsActive, "Inactive pages release observers.");
    environment.Publish(new(true, "internet", true));
    Require(viewModel.NetworkStatus == "local" && !viewModel.IsParticleAnimationEnabled, "Late event cannot affect inactive page.");
    viewModel.Activate();
    Require(environment.Starts == 2 && environment.Observers == 1 && viewModel.IsParticleAnimationEnabled, "Cached page can reactivate.");
    return Task.CompletedTask;
});

await Check("only allowlisted network status and sanitized observer errors are exposed", () =>
{
    var environment = new TestEnvironment { Snapshot = new(true, "secret-adapter-name", true) };
    using var viewModel = new VPNPageViewModel(environment);
    viewModel.Activate();
    Require(viewModel.NetworkStatus == "unknown", "Unknown network values are bounded.");
    environment.FailRead = true;
    environment.Publish(new(true, "offline", false));
    Require(viewModel.LastErrorCode == "environment_unavailable" && !viewModel.IsLoading && !viewModel.MotionAllowed, "Observer failure is safe.");
    return Task.CompletedTask;
});

await Check("particle preference preserves unsaved choice and can retry", () =>
{
    var environment = new TestEnvironment { Snapshot = new(true, "internet", true) };
    using var viewModel = new VPNPageViewModel(environment);
    viewModel.Activate();
    viewModel.SetParticlesEnabled(true);
    Require(environment.Saves == 0, "An unchanged saved choice does not write storage.");
    environment.SaveSucceeds = false;
    viewModel.SetParticlesEnabled(false);
    Require(!viewModel.ParticlesEnabled && !viewModel.IsPreferenceSaved && viewModel.LastErrorCode == "preference_save_failed", "Live preference remains usable on storage failure.");
    viewModel.Deactivate();
    viewModel.Activate();
    Require(!viewModel.ParticlesEnabled, "Reactivation preserves an unsaved live preference.");
    environment.SaveSucceeds = true;
    viewModel.SetParticlesEnabled(false);
    Require(viewModel.IsPreferenceSaved && viewModel.LastErrorCode is null && environment.Saves == 2, "Retry persists the same choice.");
    viewModel.Deactivate();
    viewModel.SetParticlesEnabled(true);
    Require(environment.Saves == 2, "Inactive page cannot change storage.");
    return Task.CompletedTask;
});

await Check("settings launch is bounded to one request and reports failure safely", async () =>
{
    var completion = Pending();
    var environment = new TestEnvironment { Launch = _ => completion.Task };
    using var viewModel = new VPNPageViewModel(environment);
    Require(!await viewModel.OpenWindowsSettingsAsync(), "An inactive page cannot launch settings.");
    viewModel.Activate();
    var first = viewModel.OpenWindowsSettingsAsync();
    Require(viewModel.IsBusy && !viewModel.CanOpenSystemSettings, "Busy state protects duplicate clicks.");
    Require(!await viewModel.OpenWindowsSettingsAsync() && environment.Launches == 1, "A duplicate does not launch another window.");
    completion.SetResult(false);
    Require(!await first && !viewModel.IsBusy && viewModel.LastErrorCode == "settings_unavailable", "Platform rejection is sanitized.");
    environment.Launch = _ => Task.FromException<bool>(new InvalidOperationException("private native text"));
    Require(!await viewModel.OpenWindowsSettingsAsync() && viewModel.LastErrorCode == "settings_failed" && !viewModel.IsBusy, "Platform exception does not escape or leave busy state.");
    environment.Launch = _ => Task.FromResult(true);
    Require(await viewModel.OpenWindowsSettingsAsync() && viewModel.LastErrorCode is null && !viewModel.IsBusy, "Retry succeeds and clears previous error.");
});

await Check("caller cancellation propagates and releases busy state", async () =>
{
    var completion = Pending();
    var environment = new TestEnvironment { Launch = _ => completion.Task };
    using var viewModel = new VPNPageViewModel(environment);
    viewModel.Activate();
    using var cancellation = new CancellationTokenSource();
    var request = viewModel.OpenWindowsSettingsAsync(cancellation.Token);
    cancellation.Cancel();
    try { await request; throw new Exception("Expected cancellation."); }
    catch (OperationCanceledException) { }
    Require(!viewModel.IsBusy && environment.LastLaunchToken.IsCancellationRequested && viewModel.LastErrorCode is null, "Cancellation is not presented as an error.");
    completion.SetResult(true);
    Require(!viewModel.IsBusy, "Late success cannot restore busy state.");
});

await Check("timeout stops waiting even if OS operation ignores cancellation", async () =>
{
    var clock = new TestClock();
    var completion = Pending();
    var environment = new TestEnvironment { Launch = _ => completion.Task };
    using var viewModel = new VPNPageViewModel(environment, clock);
    viewModel.Activate();
    var request = viewModel.OpenWindowsSettingsAsync();
    clock.FireTimers();
    Require(!await request && viewModel.LastErrorCode == "settings_timeout" && !viewModel.IsBusy, "Bounded timeout recovers the page.");
    Require(environment.LastLaunchToken.IsCancellationRequested, "Timeout also requests platform cancellation.");
    completion.SetResult(true);
});

await Check("a completed old request cannot change a new active page lifetime", async () =>
{
    var oldCompletion = Pending();
    var newCompletion = Pending();
    var environment = new TestEnvironment { Launch = _ => oldCompletion.Task };
    using var viewModel = new VPNPageViewModel(environment);
    viewModel.Activate();
    var oldRequest = viewModel.OpenWindowsSettingsAsync();
    viewModel.Deactivate();
    Require(!await oldRequest && !viewModel.IsBusy, "Deactivation cancels pending action.");
    environment.Launch = _ => newCompletion.Task;
    viewModel.Activate();
    var newRequest = viewModel.OpenWindowsSettingsAsync();
    oldCompletion.SetResult(false);
    Require(viewModel.IsBusy && viewModel.LastErrorCode is null, "Old completion cannot overwrite new state.");
    newCompletion.SetResult(true);
    Require(await newRequest && !viewModel.IsBusy, "New lifetime finishes normally.");
});

await Check("disposal cancels actions and releases all observers once", async () =>
{
    var completion = Pending();
    var environment = new TestEnvironment { Launch = _ => completion.Task };
    var viewModel = new VPNPageViewModel(environment);
    viewModel.Activate();
    var request = viewModel.OpenWindowsSettingsAsync();
    viewModel.Dispose();
    viewModel.Dispose();
    Require(!await request && environment.Disposes == 1 && environment.Observers == 0 && !viewModel.IsActive && !viewModel.IsBusy, "Dispose is final and idempotent.");
    completion.SetResult(true);
    try { viewModel.Activate(); throw new Exception("Expected disposed rejection."); }
    catch (ObjectDisposedException) { }
});

Console.WriteLine($"PASS: {passed} VPN state/lifecycle scenarios. No Windows settings or user preferences were changed.");

async Task Check(string name, Func<Task> action)
{
    await action();
    passed++;
    Console.WriteLine($"PASS {name}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static TaskCompletionSource<bool> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

sealed class TestEnvironment : IVpnPageEnvironment
{
    private EventHandler? _changed;
    public event EventHandler? StateChanged { add { _changed += value; Observers++; } remove { _changed -= value; Observers--; } }
    public int Observers { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public int Saves { get; private set; }
    public int Launches { get; private set; }
    public int Disposes { get; private set; }
    public bool FailRead { get; set; }
    public bool SavedParticles { get; set; } = true;
    public bool SaveSucceeds { get; set; } = true;
    public CancellationToken LastLaunchToken { get; private set; }
    public VpnPageEnvironmentState Snapshot { get; set; } = new(false, "unknown", false);
    public Func<CancellationToken, Task<bool>> Launch { get; set; } = _ => Task.FromResult(true);
    public VpnPageEnvironmentState ReadState() => FailRead ? throw new Exception("private native error") : Snapshot;
    public bool ReadParticlesEnabled() => SavedParticles;
    public bool TrySaveParticlesEnabled(bool enabled) { Saves++; if (SaveSucceeds) SavedParticles = enabled; return SaveSucceeds; }
    public void Start() => Starts++;
    public void Stop() => Stops++;
    public Task<bool> OpenSystemSettingsAsync(CancellationToken token) { Launches++; LastLaunchToken = token; return Launch(token); }
    public void Publish(VpnPageEnvironmentState state) { Snapshot = state; _changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose() => Disposes++;
}

sealed class TestClock : TimeProvider
{
    private readonly List<TestTimer> _timers = [];
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TestTimer(callback, state);
        _timers.Add(timer);
        return timer;
    }
    public void FireTimers() { foreach (var timer in _timers.ToArray()) timer.Fire(); }

    private sealed class TestTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public void Fire() { if (!_disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
