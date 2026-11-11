using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Mizon.Windows.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Graphics;
using Windows.Storage;
using WinRT;
using Launcher = Windows.System.Launcher;

namespace Mizon.Windows.Taskbar;

internal sealed partial class TaskbarProfileHost : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherQueueTimer _reloadTimer, _geometryTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskbarPresenceMonitor _presence;
    private DesktopWindowXamlSource? _source;
    private AuthActionButton? _button;
    private PresenceAvatar? _picture;
    private TaskbarMoodButton? _moodButton;
    private Grid? _content;
    private ProfileSnapshot _profile = new();
    private nint _window, _taskbar;
    private string? _loadedImagePath, _lastError;
    private bool _disposed, _geometryQueued, _imageLoading, _attaching;
    private long _readGeneration, _imageGeneration;
    private int _regionWidth, _regionHeight;
    private RectInt32 _bounds;
    private DateTime _snapshotWriteTime;
    private bool _layoutPending;
    private long _layoutGeneration;
    private Native.Rect? _screenBounds;
    private string? _lastPlacement;
    private nint _layoutHook;

    internal static TaskbarProfileHost? Current { get; private set; }

    internal TaskbarProfileHost(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _presence = new(() => _dispatcher.TryEnqueue(() => { if (!_disposed) ApplyPresence(); }));
        Current = this;
        Native.RegisterHostClass();
        Directory.CreateDirectory(TaskbarPaths.DirectoryPath);
        _reloadTimer = dispatcher.CreateTimer();
        _reloadTimer.Interval = TimeSpan.FromMilliseconds(150);
        _reloadTimer.IsRepeating = false;
        _reloadTimer.Tick += ReloadTimer_Tick;
        _geometryTimer = dispatcher.CreateTimer();
        _geometryTimer.Interval = TimeSpan.FromSeconds(2);
        _geometryTimer.Tick += GeometryTimer_Tick;
        _watcher = new(TaskbarPaths.DirectoryPath, "profile.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Changed += SnapshotChanged;
        _watcher.Created += SnapshotChanged;
        _watcher.Deleted += SnapshotChanged;
        _watcher.Renamed += SnapshotRenamed;
        _watcher.Error += WatcherError;
        _watcher.EnableRaisingEvents = true;
        _layoutHook = Native.WatchTaskbarLayout();
        QueueReload();
        _geometryTimer.Start();
    }

    private void SnapshotChanged(object sender, FileSystemEventArgs args) => QueueReload();
    private void SnapshotRenamed(object sender, RenamedEventArgs args) => QueueReload();
    private void WatcherError(object sender, ErrorEventArgs args) => QueueReload();
    private void QueueReload() => _dispatcher.TryEnqueue(() =>
    {
        if (_disposed) return;
        _reloadTimer.Stop();
        _reloadTimer.Start();
    });

    private async void ReloadTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var generation = ++_readGeneration;
        try
        {
            var snapshot = await ReadSnapshotAsync(_lifetime.Token);
            if (_disposed || generation != _readGeneration) return;
            _snapshotWriteTime = File.GetLastWriteTimeUtc(TaskbarPaths.SnapshotPath);
            if (snapshot.Version != 1) throw new InvalidDataException("Unsupported profile snapshot version.");
            var presentationChanged = !HasSamePresentation(_profile, snapshot);
            _profile = snapshot;
            _presence.Apply(snapshot, DateTime.UtcNow.Ticks);
            if (!snapshot.IsAuthenticated)
            {
                DestroyIsland();
                TaskbarPaths.Log("Signed out; taskbar child removed. Companion is waiting for the next login.");
                return;
            }
            EnsureIsland();
            if (presentationChanged) ApplyProfile(); else ApplyPresence();
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error)
        {
            if (!_disposed && generation == _readGeneration) _presence.Clear();
            LogOnce("Profile snapshot", error);
        }
    }

    private static async Task<ProfileSnapshot> ReadSnapshotAsync(CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(TaskbarPaths.SnapshotPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length > 65536) throw new InvalidDataException("Profile snapshot exceeds its size limit.");
                return await JsonSerializer.DeserializeAsync(stream, TaskbarJsonContext.Default.ProfileSnapshot, token)
                    ?? throw new InvalidDataException("Profile snapshot is empty.");
            }
            catch (FileNotFoundException) { return new(); }
            catch (IOException) when (attempt < 2) { await Task.Delay(100, token); }
        }
    }

    private void GeometryTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed) return;
        _presence.Evaluate(DateTime.UtcNow.Ticks);
        try
        {
            if (File.GetLastWriteTimeUtc(TaskbarPaths.SnapshotPath) != _snapshotWriteTime) QueueReload();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { LogOnce("Snapshot timestamp", error); }
        EnsureIsland();
    }

    internal void QueueGeometryUpdate()
    {
        if (_disposed || _geometryQueued || _attaching) return;
        _geometryQueued = true;
        _dispatcher.TryEnqueue(() => { _geometryQueued = false; if (!_disposed) EnsureIsland(); });
    }

    private void EnsureIsland()
    {
        if (_disposed || !_profile.IsAuthenticated) return;
        _attaching = true;
        try
        {
            var taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (taskbar == 0 || !Native.IsWindow(taskbar)) { DestroyIsland(); return; }
            if (_taskbar != taskbar || !Native.IsWindow(_window))
            {
                DestroyIsland();
                _taskbar = taskbar;
                TaskbarPaths.Log("Attachment: initializing a hidden, process-owned XAML host.");
                _window = Native.CreateWindowEx(Native.ToolWindowStyle, Native.HostClass, "Mizon profile",
                    Native.PopupStyle, 0, 0, 1, 1, 0, 0, Native.GetModuleHandle(null), 0);
                if (_window == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the taskbar child.");
                TaskbarPaths.Log($"Attachment: hidden native host 0x{_window:X}; creating XAML source.");
                _source = new DesktopWindowXamlSource();
                _source.ShouldConstrainPopupsToWorkArea = false;
                TaskbarPaths.Log("Attachment: initializing XAML source.");
                _source.Initialize(Win32Interop.GetWindowIdFromWindow(_window));
                TaskbarPaths.Log("Attachment: converting initialized host into a real taskbar child.");
                if (Native.SetWindowLongPtr(_window, -16, (nint)Native.ChildStyle) == 0 && Marshal.GetLastPInvokeError() != 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not change the host to WS_CHILD.");
                Native.SetParent(_window, taskbar);
                if (Native.GetParent(_window) != taskbar || ((long)Native.GetWindowLongPtr(_window, -16) & 0x40000000) == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "The profile host could not become a real taskbar child.");
                TaskbarPaths.Log("Attachment: creating XAML profile content.");
                _picture = new PresenceAvatar { IsHitTestVisible = false, Width = 32, Height = 32 };
                AutomationProperties.SetAccessibilityView(_picture, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                _button = new AuthActionButton
                {
                    Style = Application.Current.Resources["ProfileAvatarButtonStyle"].As<Style>(), HoverScale = 1.06, PressedScale = .91,
                    Content = _picture, Padding = new Thickness(2), MinWidth = 0, MinHeight = 0,
                    CornerRadius = new CornerRadius(100), BorderThickness = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent), HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center, UseSystemFocusVisuals = true,
                    Width = 36, Height = 36
                };
                _button.Click += ProfileClicked;
                _moodButton = new TaskbarMoodButton();
                _content = new Grid { ColumnSpacing = 8, FlowDirection = FlowDirection.LeftToRight };
                _content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(_button, 1);
                _content.Children.Add(_moodButton);
                _content.Children.Add(_button);
                _source.Content = _content;
                _source.TakeFocusRequested += Source_TakeFocusRequested;
                _source.SiteBridge.Show();
                ApplyProfile();
                TaskbarPaths.Log($"Taskbar child created: HWND=0x{_window:X}, parent=0x{taskbar:X}, WS_CHILD=True.");
            }
            UpdateGeometry();
            _lastError = null;
        }
        catch (Exception error)
        {
            DestroyIsland();
            LogOnce("Taskbar attachment", error);
        }
        finally { _attaching = false; }
    }

    private void UpdateGeometry()
    {
        if (_window == 0 || _source is null) return;
        if (!Native.IsWindowVisible(_taskbar)) { HideHost(_window); return; }
        if (_layoutPending) return;
        _ = UpdateGeometryAsync(_window, _taskbar, _layoutGeneration);
    }

    private async Task UpdateGeometryAsync(nint window, nint taskbar, long generation)
    {
        _layoutPending = true;
        try
        {
            var layout = await Task.Run(() => TaskbarLayoutLocator.Read(taskbar), _lifetime.Token);
            if (_disposed || window != _window || taskbar != _taskbar || generation != _layoutGeneration || _source is null) return;
            // Shell visibility can change while its automation tree is queried.
            if (!Native.IsWindowVisible(taskbar)) { HideHost(window); return; }
            if (!Native.GetWindowRect(taskbar, out var currentTaskbarBounds)
                || !currentTaskbarBounds.Equals(layout.Bounds))
            {
                HideHost(window);
                QueueGeometryUpdate();
                return;
            }
            var scale = Math.Max(1d, Native.GetDpiForWindow(taskbar) / 96d);
            // Placement uses the complete host footprint, independent of the current circular content.
            var hasMood = _moodButton?.HasMood == true;
            var hostWidth = Math.Max(16, (int)Math.Round((hasMood ? 80 : 36) * scale));
            var hostHeight = Math.Max(16, (int)Math.Round(36 * scale));
            var padding = (int)Math.Round(8 * scale);
            if (!TaskbarLayoutLocator.TryPlace(layout, Environment.ProcessId, hostWidth, hostHeight, padding, out var screenBounds, _screenBounds))
            {
                HideHost(window);
                LogPlacement("Taskbar layout: no verified free interval fits the profile and mood; child hidden.");
                return;
            }
            // Autohide may move a WS_VISIBLE taskbar below the monitor instead
            // of hiding its HWND. Do not keep an offscreen animation running.
            if (!Native.TryGetMonitorBounds(taskbar, out var monitor)
                || screenBounds.Left < monitor.Left || screenBounds.Top < monitor.Top
                || screenBounds.Right > monitor.Right || screenBounds.Bottom > monitor.Bottom)
            {
                HideHost(window);
                LogPlacement("Taskbar layout: profile and mood are outside the visible monitor; child paused and hidden.");
                return;
            }
            _screenBounds = screenBounds;
            var clientBounds = screenBounds;
            Native.MapWindowPoints(0, taskbar, ref clientBounds, 2);
            var bounds = new RectInt32(clientBounds.Left, clientBounds.Top, hostWidth, hostHeight);
            if (!_bounds.Equals(bounds))
            {
                if (!Native.SetWindowPos(window, 0, bounds.X, bounds.Y, hostWidth, hostHeight, Native.PositionFlags))
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not position the taskbar child.");
                _source.SiteBridge.MoveAndResize(new RectInt32(0, 0, hostWidth, hostHeight));
                if (_picture is not null) _picture.Width = _picture.Height = Math.Max(16, Math.Min(hostWidth, hostHeight) / scale - 4);
                _bounds = bounds;
            }
            else Native.ShowWindow(window, 4);
            if (_regionWidth != hostWidth || _regionHeight != hostHeight)
            {
                // Keep the gap transparent and hit-testable by the taskbar, while
                // clipping each of our two independent buttons to its own circle.
                var avatarWidth = Math.Max(16, (int)Math.Round(36 * scale));
                var region = Native.CreateEllipticRgn(0, 0, avatarWidth + 1, hostHeight + 1);
                if (region == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the profile hit region.");
                try
                {
                    if (hasMood)
                    {
                        var moodLeft = (int)Math.Round(44 * scale);
                        var moodRegion = Native.CreateEllipticRgn(moodLeft, 0, hostWidth + 1, hostHeight + 1);
                        if (moodRegion == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the mood hit region.");
                        try
                        {
                            if (Native.CombineRgn(region, region, moodRegion, 2) == 0) // RGN_OR; zero is ERROR.
                                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not combine the taskbar hit regions.");
                        }
                        finally { Native.DeleteObject(moodRegion); }
                    }
                    if (Native.SetWindowRgn(window, region, true) == 0)
                        throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not apply the taskbar hit region.");
                    region = 0; // Windows owns the successfully assigned region.
                    _regionWidth = hostWidth;
                    _regionHeight = hostHeight;
                }
                finally { if (region != 0) Native.DeleteObject(region); }
            }
            _moodButton?.SetHostVisible(true);
            _moodButton?.UpdateEffectPosition();
            LogPlacement($"Taskbar layout: verified free placement={screenBounds.Left},{screenBounds.Top},{screenBounds.Right},{screenBounds.Bottom}; padding={padding}px; inspected={layout.Items.Count}.");
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error)
        {
            if (!_disposed && window == _window)
            {
                HideHost(window);
                LogOnce("Taskbar layout query; child hidden", error);
            }
        }
        finally { _layoutPending = false; }
    }

    private void HideHost(nint window)
    {
        _moodButton?.SetHostVisible(false);
        Native.ShowWindow(window, 0);
    }

    private void LogPlacement(string message)
    {
        if (_lastPlacement == message) return;
        _lastPlacement = message;
        TaskbarPaths.Log(message);
    }

    internal void NotifyShellLayoutChanged(nint window)
    {
        if (_disposed || _taskbar == 0 || _window == window) return;
        if (window == _taskbar || Native.IsChild(_taskbar, window)) QueueGeometryUpdate();
    }

    private void ApplyProfile()
    {
        if (_picture is null || _button is null) return;
        var persian = _profile.LanguageTag?.StartsWith("en", StringComparison.OrdinalIgnoreCase) != true;
        var name = string.IsNullOrWhiteSpace(_profile.DisplayName) ? (persian ? "حساب میزون" : "Mizon account") : _profile.DisplayName.Trim();
        _picture.DisplayName = name;
        AutomationProperties.SetName(_button, _profile.ClientPublicId is { } id && id != Guid.Empty
            ? persian ? $"باز کردن پروفایل {name} در میزون" : $"Open {name}'s profile in Mizon"
            : persian ? $"باز کردن میزون؛ {name}" : $"Open Mizon; {name}");
        AutomationProperties.SetHelpText(_button, persian
            ? "پروفایل حساب واردشده؛ برای باز کردن میزون کلیک کنید." : "Signed-in account profile. Activate to open Mizon.");
        ApplyPresence();
        _moodButton?.Apply(_profile);
        if (_moodButton is not null) _moodButton.Visibility = _moodButton.HasMood ? Visibility.Visible : Visibility.Collapsed;
        if (_content is not null) _content.ColumnSpacing = _moodButton?.HasMood == true ? 8 : 0;
        QueueGeometryUpdate();
        if (_loadedImagePath == _profile.ImagePath && (_picture.Source is not null || _imageLoading)) return;
        _loadedImagePath = _profile.ImagePath;
        _picture.Source = null;
        var generation = ++_imageGeneration;
        _imageLoading = !string.IsNullOrWhiteSpace(_loadedImagePath);
        if (_imageLoading) _ = LoadImageAsync(_loadedImagePath!, generation);
    }

    private void ApplyPresence()
    {
        if (_picture is null || _button is null) return;
        var online = _presence.IsOnline;
        var label = online ? _profile.OnlinePresenceLabel : _profile.OfflinePresenceLabel;
        if (string.IsNullOrWhiteSpace(label))
        {
            var english = _profile.LanguageTag?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
            label = online ? english ? "Online" : "آنلاین" : english ? "Offline" : "آفلاین";
        }
        _picture.IsOnline = online;
        _picture.StatusText = label;
        AutomationProperties.SetItemStatus(_button, label);
        ToolTipService.SetToolTip(_button, string.IsNullOrWhiteSpace(_picture.DisplayName)
            ? label : _picture.DisplayName + " · " + label);
    }

    private static bool HasSamePresentation(ProfileSnapshot left, ProfileSnapshot right) =>
        left.IsAuthenticated == right.IsAuthenticated && left.ClientPublicId == right.ClientPublicId
        && left.DisplayName == right.DisplayName && left.ImagePath == right.ImagePath
        && left.MoodPublicId == right.MoodPublicId && left.MoodEmoji == right.MoodEmoji
        && left.MoodTitle == right.MoodTitle && left.MoodAnimationPath == right.MoodAnimationPath
        && left.MoodEffectPath == right.MoodEffectPath && left.LanguageTag == right.LanguageTag;

    private async Task LoadImageAsync(string path, long generation)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
                throw new InvalidDataException("The profile image must be an existing local cached file.");
            var token = _lifetime.Token;
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
            using var stream = await file.OpenReadAsync().AsTask(token);
            var image = new BitmapImage { DecodePixelWidth = 128 };
            await image.SetSourceAsync(stream).AsTask(token);
            if (!_disposed && _profile.IsAuthenticated && generation == _imageGeneration && _picture is not null)
            {
                _picture.Source = image;
                TaskbarPaths.Log($"Profile image decoded and assigned: {image.PixelWidth}x{image.PixelHeight}.");
            }
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error) { LogOnce("Profile image", error); }
        finally { if (generation == _imageGeneration) _imageLoading = false; }
    }

    private async void ProfileClicked(object sender, RoutedEventArgs args)
    {
        if (_disposed || !_profile.IsAuthenticated) return;
        try
        {
            var uri = _profile.ClientPublicId is { } id && id != Guid.Empty ? $"mizon://client/{id:D}" : "mizon://music";
            if (!await Launcher.LaunchUriAsync(new Uri(uri))) TaskbarPaths.Log("Mizon URI activation was not handled.");
        }
        catch (Exception error) { LogOnce("Profile activation", error); }
    }

    private void Source_TakeFocusRequested(DesktopWindowXamlSource sender, DesktopWindowXamlSourceTakeFocusRequestedEventArgs args)
    {
        var reason = args.Request.Reason == XamlSourceFocusNavigationReason.Last
            ? XamlSourceFocusNavigationReason.Last : XamlSourceFocusNavigationReason.First;
        sender.NavigateFocus(new XamlSourceFocusNavigationRequest(reason));
    }

    internal void HostDestroyed(nint window)
    {
        if (_window != window) return;
        _window = 0;
        QueueGeometryUpdate(); // Explorer may have restarted; never destroy its replacement.
    }

    private void DestroyIsland()
    {
        ++_layoutGeneration;
        ++_imageGeneration;
        _moodButton?.Dispose();
        _moodButton = null;
        if (_button is not null) _button.Click -= ProfileClicked;
        if (_picture is not null) _picture.Source = null;
        var source = _source;
        _source = null;
        if (source is not null)
        {
            try
            {
                source.TakeFocusRequested -= Source_TakeFocusRequested;
                source.Content = null;
                source.Dispose();
            }
            catch (Exception error) { TaskbarPaths.Log($"Island cleanup: {error.Message}"); }
        }

        _button = null;
        _picture = null;
        _content = null;
        var window = _window;
        _window = _taskbar = 0;
        if (window != 0 && Native.IsWindow(window)) Native.DestroyWindow(window);
        _regionWidth = _regionHeight = 0;
        _bounds = default;
        _loadedImagePath = null;
        _imageLoading = false;
        _screenBounds = null;
        _lastPlacement = null;
    }

    private void LogOnce(string stage, Exception error)
    {
        var message = $"{stage}: {error.GetType().Name}: {error.Message} (0x{error.HResult:X8})";
        if (_lastError != message) TaskbarPaths.Log(message);
        _lastError = message;
    }

    public void Dispose()
    {
        if (_disposed) return;
        TaskbarPaths.Log("Companion cleanup beginning.");
        _disposed = true;
        _presence.Dispose();
        if (_layoutHook != 0) { Native.UnhookWinEvent(_layoutHook); _layoutHook = 0; }
        _lifetime.Cancel();
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= SnapshotChanged;
        _watcher.Created -= SnapshotChanged;
        _watcher.Deleted -= SnapshotChanged;
        _watcher.Renamed -= SnapshotRenamed;
        _watcher.Error -= WatcherError;
        _watcher.Dispose();
        _reloadTimer.Stop(); _geometryTimer.Stop();
        _reloadTimer.Tick -= ReloadTimer_Tick;
        _geometryTimer.Tick -= GeometryTimer_Tick;
        DestroyIsland();
        _lifetime.Dispose();
        Current = null;
        TaskbarPaths.Log("Companion disposed; child HWND and XAML resources released.");
    }

}
