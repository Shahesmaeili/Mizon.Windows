using Microsoft.UI.Xaml;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Foundation;

namespace Mizon.Windows.Taskbar;

// XAML hit testing does not remove a native popup's input surface. Scope this
// change to our one decorative popup; never subclass or change Explorer HWNDs.
internal sealed unsafe partial class TaskbarEffectInputScope : IDisposable
{
    private const long Layered = 0x00080000, Transparent = 0x20, NoActivate = 0x08000000;
    private const string PopupClass = "Microsoft.UI.Content.PopupWindowSiteBridge";
    private readonly nint _window;
    private readonly uint _thread;
    private readonly long _addedStyles;
    private bool _disposed;

    private TaskbarEffectInputScope(nint window, uint thread)
    {
        _window = window;
        _thread = thread;
        var original = (long)Native.GetWindowLongPtr(window, -20);
        var desired = original | Layered | Transparent | NoActivate;
        _addedStyles = desired & ~original;
        if (Native.SetWindowLongPtr(window, -20, (nint)desired) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not configure the mood effect input surface.");
        if (!SetLayeredWindowAttributes(window, 0, 255, 2)) // LWA_ALPHA: preserve full-color composition.
        {
            var error = Marshal.GetLastPInvokeError();
            Dispose();
            throw new Win32Exception(error, "Could not preserve the mood effect's transparent composition.");
        }
    }

    internal static TaskbarEffectInputScope? TryCreate(FrameworkElement effect)
    {
        var root = effect.XamlRoot ?? throw new InvalidOperationException("The mood effect has no XAML root.");
        var origin = effect.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var first = root.CoordinateConverter.ConvertLocalToScreen(origin);
        var last = root.CoordinateConverter.ConvertLocalToScreen(new Point(origin.X + effect.ActualWidth, origin.Y + effect.ActualHeight));
        var search = new Search
        {
            Bounds = new Native.Rect
            {
                Left = first.X, Top = first.Y,
                Right = last.X, Bottom = last.Y
            },
            ShadowAllowance = (int)Math.Ceiling(64 * root.RasterizationScale)
        };
        var thread = GetCurrentThreadId();
        // Popup exposes its owner's AppWindowId, not its private native bridge.
        // Match only our thread, process, exact class and rendered content bounds.
        EnumThreadWindows(thread, &FindPopup, (nint)(&search));
        if (search.Matches != 1) return null;
        return new(search.Window, thread);
    }

    private struct Search
    {
        internal Native.Rect Bounds;
        internal int ShadowAllowance, Matches;
        internal nint Window;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FindPopup(nint window, nint parameter)
    {
        var search = (Search*)parameter;
        if (!Native.IsWindowVisible(window) || !IsOwnPopup(window, GetCurrentThreadId())
            || !Native.GetWindowRect(window, out var bounds)) return 1;
        var expected = search->Bounds;
        var allowance = search->ShadowAllowance;
        if (bounds.Left > expected.Left + 2 || bounds.Top > expected.Top + 2
            || bounds.Right < expected.Right - 2 || bounds.Bottom < expected.Bottom - 2
            || expected.Left - bounds.Left > allowance || expected.Top - bounds.Top > allowance
            || bounds.Right - expected.Right > allowance || bounds.Bottom - expected.Bottom > allowance) return 1;
        search->Window = window;
        search->Matches++;
        return 1;
    }

    private static bool IsOwnPopup(nint window, uint thread)
    {
        if (Native.GetWindowThreadProcessId(window, out var process) != thread || process != Environment.ProcessId) return false;
        var name = stackalloc char[96];
        var length = GetClassName(window, name, 96);
        return new ReadOnlySpan<char>(name, length).SequenceEqual(PopupClass);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!Native.IsWindow(_window) || !IsOwnPopup(_window, _thread)) return;
        // WinUI may reuse its native popup. Remove only flags added by this scope.
        var current = (long)Native.GetWindowLongPtr(_window, -20);
        Native.SetWindowLongPtr(_window, -20, (nint)(current & ~_addedStyles));
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumThreadWindows(uint thread, delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static partial int GetClassName(nint window, char* name, int capacity);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
}
