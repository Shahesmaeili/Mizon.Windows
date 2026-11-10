using System.Collections.Concurrent;
using System.ComponentModel;
using Mizon.Windows.Mcp.Contracts;

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly ConcurrentQueue<Action> _paused = new();
        private readonly Thread _thread;
        public volatile bool Pause;
        public DispatcherQueue()
        {
            _thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new QueueContext(this));
                foreach (var action in _queue.GetConsumingEnumerable()) action();
            }) { IsBackground = true };
            _thread.Start();
        }
        public bool TryEnqueue(Action callback)
        {
            if (Pause) _paused.Enqueue(callback);
            else _queue.Add(callback);
            return true;
        }
        public void Flush()
        {
            Pause = false;
            while (_paused.TryDequeue(out var action)) _queue.Add(action);
        }
        private sealed class QueueContext(DispatcherQueue owner) : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) => owner.TryEnqueue(() => d(state));
        }
        public void Dispose() { _queue.CompleteAdding(); _thread.Join(1000); _queue.Dispose(); }
    }
}

namespace Microsoft.Windows.Storage
{
    public sealed class ApplicationData
    {
        private static readonly ApplicationData Instance = new();
        public static ApplicationData GetDefault() => Instance;
        public Settings LocalSettings { get; } = new();
        public Folder LocalFolder { get; } = new();
        public sealed class Folder { public string Path => Program.StorageRoot; }
        public sealed class Settings { public Dictionary<string, object> Values { get; } = []; }
    }
}

namespace Mizon.Windows.Services
{
    // Compiled only in this test project. The production service's SID lookup
    // resolves here so its real Start/listener runs on an isolated pipe.
    internal sealed class WindowsIdentity : IDisposable
    {
        public static WindowsIdentity GetCurrent() => new();
        public TestUser User { get; } = new();
        public void Dispose() { }
        internal sealed class TestUser { public string Value => Program.PipeSuffix; }
    }
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Current { get; } = new();
        public string LanguageTag => "en-US";
        public int Revision => 0;
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public string Format(string key, params object[] values) => key;
        public string GetString(string key) => key;
    }
    public static class AppLog { public static void Information(string text) => Console.WriteLine(text); }
    internal sealed class McpAuthenticationService : IDisposable
    {
        public Func<McpRequest, CancellationToken, Task<McpResponse>> Handler { get; set; } =
            (_, _) => Task.FromResult(new McpResponse { Success = true, Code = "state", State = new() { Page = "login" } });
        public Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken token) => Handler(request, token);
        public void Dispose() { }
    }

    // Synthetic preference backend only; production access dispatcher and appearance
    // adapter are source-linked. These types never enter the real application build.
    public enum AppearanceThemeMode { System, Light, Dark }
    public enum AppearanceBackdropMode { Acrylic, AcrylicBase, AcrylicThin, Mica, MicaAlt }
    public enum AppearanceFallbackReason { None, WindowUnavailable, HighContrast, TransparencyDisabled, Unsupported, Unavailable }
    public sealed record AppearanceState
    {
        public AppearanceThemeMode ThemeMode { get; init; }
        public AppearanceThemeMode EffectiveTheme { get; init; } = AppearanceThemeMode.Light;
        public AppearanceBackdropMode BackdropMode { get; init; }
        public AppearanceBackdropMode? EffectiveBackdrop { get; init; } = AppearanceBackdropMode.Acrylic;
        public AppearanceFallbackReason FallbackReason { get; init; }
        public bool IsHighContrast { get; init; }
        public bool IsTransparencyEnabled { get; init; } = true;
        public bool IsMicaSupported { get; init; } = true;
        public bool IsAcrylicSupported { get; init; } = true;
        public bool IsPreferenceSaved { get; init; } = true;
        public bool IsWindowAttached { get; init; } = true;
    }
    public sealed class AppearanceSettingsService
    {
        public AppearanceState State { get; set; } = new();
        public int Writes { get; private set; }
        public bool FailWrite { get; set; }
        public void SetPreferences(AppearanceThemeMode? themeMode = null, AppearanceBackdropMode? backdropMode = null)
        {
            if (FailWrite) throw new IOException("Synthetic private detail must not escape");
            Writes++;
            State = State with
            {
                ThemeMode = themeMode ?? State.ThemeMode,
                EffectiveTheme = themeMode is null or AppearanceThemeMode.System ? State.EffectiveTheme : themeMode.Value,
                BackdropMode = backdropMode ?? State.BackdropMode,
                EffectiveBackdrop = State.FallbackReason == AppearanceFallbackReason.None ? backdropMode ?? State.BackdropMode : null
            };
        }
    }
    internal sealed class SyntheticAppearanceService(AppearanceSettingsService preferences) : IMcpAppearanceService
    {
        private readonly McpAppearanceService _adapter = new(preferences);
        public Func<McpRequest, CancellationToken, Task<McpResponse>>? Handler { get; set; }
        public Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken token) =>
            Handler is null ? _adapter.ExecuteAsync(request, token) : Handler(request, token);
    }
}
