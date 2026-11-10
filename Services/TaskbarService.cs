using Microsoft.UI.Dispatching;
using System;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Services;

public interface ITaskbarService
{
    void Initialize(IntPtr windowHandle);
    void SetProgressState(TaskbarProgressState state);
    void SetProgressValue(double currentValue, double maximumValue);
    void ClearProgress();
    void SetProgress(double percentage, TaskbarProgressState state = TaskbarProgressState.Normal);
    void NotifyWindowActivated();
    void SetPlaybackButtonState(AudioPlayerState state);
    void SetNavigationAvailability(bool previous, bool next);
    event EventHandler? PlaybackToggleRequested;
    event EventHandler? PreviousRequested;
    event EventHandler? NextRequested;
    event EventHandler? FullScreenRequested;
}

public enum TaskbarProgressState { NoProgress, Indeterminate, Normal, Error, Paused }

/// <summary>Owns the native thumbnail toolbar and its icons on the window dispatcher.</summary>
public sealed class TaskbarService(DispatcherQueue dispatcherQueue) : ITaskbarService, IDisposable
{
    private readonly DispatcherQueue _dispatcher = dispatcherQueue;
    private TaskbarNative? _native;
    private nint _window;
    private GCHandle _self;
    private bool _toolbarAdded, _disposed, _canPrevious, _canNext;
    private AudioPlayerState _playbackState = AudioPlayerState.Idle;
    private ThumbnailTransportIcons? _icons;
    private static readonly TaskbarNative.SubclassProc Callback = WindowProcedure;
    private static readonly uint CreatedMessage = TaskbarNative.RegisterWindowMessage("TaskbarButtonCreated");
    private const uint PreviousId = 10, PlayId = 11, NextId = 12, FullScreenId = 13;

    public event EventHandler? PlaybackToggleRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? FullScreenRequested;

    public void Initialize(nint windowHandle)
    {
        if (_disposed || _window == windowHandle && _native is not null) return;
        _window = windowHandle;
        try
        {
            _native = new TaskbarNative();
            _self = GCHandle.Alloc(this);
            if (!TaskbarNative.SetWindowSubclass(_window, Callback, 1, (nuint)GCHandle.ToIntPtr(_self)))
            {
                _self.Free();
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            }
            AppLog.Information("Taskbar native service initialized; thumbnail toolbar handler installed.");
        }
        catch (Exception error) { _native?.Dispose(); _native = null; AppLog.Error("Taskbar initialization failed.", error); }
    }

    public void SetProgressState(TaskbarProgressState state) => Dispatch(() => _native?.SetState(_window, ProgressFlags(state)));
    public void ClearProgress() => SetProgressState(TaskbarProgressState.NoProgress);
    public void SetProgress(double percentage, TaskbarProgressState state = TaskbarProgressState.Normal)
    {
        if (!double.IsFinite(percentage)) return;
        Dispatch(() => { _native?.SetState(_window, ProgressFlags(state)); _native?.SetValue(_window, (ulong)Math.Round(Math.Clamp(percentage, 0, 1) * 1000), 1000); });
    }
    public void SetProgressValue(double currentValue, double maximumValue)
    {
        if (!double.IsFinite(currentValue) || !double.IsFinite(maximumValue) || maximumValue <= 0) return;
        var fraction = Math.Clamp(currentValue / maximumValue, 0, 1);
        Dispatch(() => _native?.SetValue(_window, (ulong)Math.Round(fraction * 1000000), 1000000));
    }
    private static uint ProgressFlags(TaskbarProgressState state) => state switch
    { TaskbarProgressState.Indeterminate => 1, TaskbarProgressState.Normal => 2, TaskbarProgressState.Error => 4, TaskbarProgressState.Paused => 8, _ => 0 };

    public void SetPlaybackButtonState(AudioPlayerState state) => Dispatch(() => { _playbackState = state; UpdateToolbar(); });
    public void SetNavigationAvailability(bool previous, bool next) => Dispatch(() => { _canPrevious = previous; _canNext = next; UpdateToolbar(); });
    public void NotifyWindowActivated() => Dispatch(EnsureToolbar);

    private void EnsureToolbar()
    {
        if (_toolbarAdded || _native is null) return;
        _icons ??= new ThumbnailTransportIcons(_window);
        WriteButtons(add: true);
        _toolbarAdded = true; // Set only after the native HRESULT succeeds.
        AppLog.Information("Taskbar toolbar added: previous, play/pause, next, fullscreen (native THUMBBUTTON array).");
    }
    private void UpdateToolbar() { if (_toolbarAdded) WriteButtons(add: false); }
    private void WriteButtons(bool add)
    {
        if (_native is null || _icons is null) return;
        var playing = _playbackState == AudioPlayerState.Playing;
        _native.SetButtons(_window, add,
        [
            new(PreviousId, _icons.Previous, "آهنگ قبلی", _canPrevious),
            new(PlayId, playing ? _icons.Pause : _icons.Play, playing ? "توقف موقت پخش" : "پخش آهنگ", true),
            new(NextId, _icons.Next, "آهنگ بعدی", _canNext),
            new(FullScreenId, _icons.FullScreen, "تغییر حالت تمام‌صفحه", true)
        ]);
    }
    private void Dispatch(Action action)
    {
        void Run()
        {
            if (_disposed || _window == 0) return;
            try { action(); } catch (Exception error) { AppLog.Error("Taskbar operation failed.", error); }
        }
        if (_dispatcher.HasThreadAccess) Run(); else _dispatcher.TryEnqueue(Run);
    }
    internal void HandleWindowCommand(nint parameter)
    {
        if (((ulong)parameter >> 16 & 0xffff) != 0x1800) return;
        switch ((uint)((ulong)parameter & 0xffff))
        {
            case PreviousId when _canPrevious: PreviousRequested?.Invoke(this, EventArgs.Empty); break;
            case PlayId: PlaybackToggleRequested?.Invoke(this, EventArgs.Empty); break;
            case NextId when _canNext: NextRequested?.Invoke(this, EventArgs.Empty); break;
            case FullScreenId: FullScreenRequested?.Invoke(this, EventArgs.Empty); break;
        }
    }
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (data != 0 && GCHandle.FromIntPtr((nint)data).Target is TaskbarService service)
            {
                if (message == 0x0111) service.HandleWindowCommand(wParam);
                else if (message == CreatedMessage)
                {
                    service.Dispatch(() =>
                    {
                        // Activation fallback can win the race with this notification.
                        // Update first; a recreated Explorer button has no toolbar yet.
                        if (service._toolbarAdded)
                        {
                            try { service.WriteButtons(add: false); return; }
                            catch (COMException) { service._toolbarAdded = false; }
                        }
                        service.EnsureToolbar();
                    });
                }
                else if (message == 0x02E0) service.Dispatch(() =>
                {
                    var old = service._icons;
                    service._icons = null;
                    try { service._icons = new ThumbnailTransportIcons(window); service.UpdateToolbar(); }
                    finally { old?.Dispose(); }
                });
                else if (message == 0x0082) service.Dispose(); // WM_NCDESTROY
            }
        }
        catch (Exception error) { AppLog.Error("Taskbar window message failed.", error); }
        return TaskbarNative.DefSubclassProc(window, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_self.IsAllocated) { TaskbarNative.RemoveWindowSubclass(_window, Callback, 1); _self.Free(); }
        _icons?.Dispose(); _icons = null;
        _native?.Dispose(); _native = null;
        PlaybackToggleRequested = PreviousRequested = NextRequested = FullScreenRequested = null;
        _window = 0;
    }
}
