using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Shell;

namespace Mizon.Windows.Services;

internal sealed partial class TaskbarPinService
{
    private static TaskbarPinService? _current;
    internal static TaskbarPinService Current => _current ??= new(new TaskbarPinWindowsBackend());
    internal static void DisposeCurrent() => _current?.Dispose();
}

internal sealed partial class TaskbarPinWindowsBackend : ITaskbarPinBackend
{
    private bool _disposed;
    private bool? _desktopSupported;

    public void VerifyAccess()
    {
        if (App.MainMizonWindow?.DispatcherQueue.HasThreadAccess != true)
            throw new InvalidOperationException("Taskbar operations require the Mizon UI dispatcher.");
    }

    public bool IsForeground
    {
        get
        {
            VerifyAccess();
            return App.MainMizonWindow is { } window
                && GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
    }

    public async Task<TaskbarPinState> ReadAsync(CancellationToken token)
    {
        VerifyAccess();
        token.ThrowIfCancellationRequested();
        if (_disposed) return new(false, false, false, false, "unavailable");
        _desktopSupported ??= SupportsDesktop();
        if (_desktopSupported != true) return new(false, false, false, false, "unsupported");
        // Official capability detection for pre-KB5074105 versions. No token is
        // generated or borrowed, and no registry/policy setting is changed.
        if (RequiresLimitedAccessToken()) return new(false, false, false, false, "access_token_required");
        var manager = TaskbarManager.GetDefault();
        if (!manager.IsSupported) return new(false, false, false, false, "unsupported");
        var pinned = await manager.IsCurrentAppPinnedAsync().AsTask(token);
        token.ThrowIfCancellationRequested();
        var allowed = manager.IsPinningAllowed;
        return new(true, pinned, allowed, false, pinned ? "pinned" : allowed ? "not_pinned" : "not_allowed");
    }

    public async Task<bool> RequestPinAsync(CancellationToken token)
    {
        VerifyAccess();
        token.ThrowIfCancellationRequested();
        if (_disposed || !IsForeground) return false;
        var operation = TaskbarManager.GetDefault().RequestPinCurrentAppAsync();
        using var registration = token.Register(static state =>
        {
            try { ((IAsyncInfo)state!).Cancel(); }
            catch (Exception) { /* Windows may have completed the prompt already. */ }
        }, operation);
        // Do not pass the cancellation token to AsTask: observe the native
        // completion itself before releasing the service's single-prompt gate.
        return await operation.AsTask();
    }

    private static bool RequiresLimitedAccessToken()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\LimitedAccessFeatures\com.microsoft.windows.taskbar.pin", false);
        const string seed = "4096B239A7295B635C090E647E867B5707DA6AB6CB78340B01FE4E0C8F4953D4";
        return key?.GetValue(seed) is int value && value != 0;
    }

    private static bool SupportsDesktop()
    {
        nint runtimeClass = 0, factory = 0;
        try
        {
            const string name = "Windows.UI.Shell.TaskbarManager";
            Marshal.ThrowExceptionForHR(WindowsCreateString(name, (uint)name.Length, out runtimeClass));
            // ITaskbarManagerDesktopAppSupportStatics is an empty IInspectable
            // marker, defined in the Windows SDK windows.ui.shell.h.
            var iid = new Guid("cdfefd63-e879-4134-b9a7-8283f05f9480");
            return RoGetActivationFactory(runtimeClass, in iid, out factory) >= 0;
        }
        finally
        {
            if (factory != 0) Marshal.Release(factory);
            if (runtimeClass != 0) WindowsDeleteString(runtimeClass);
        }
    }

    public void Dispose() => _disposed = true;

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string value, uint length, out nint result);
    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint value);
    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint runtimeClass, in Guid iid, out nint factory);
}
