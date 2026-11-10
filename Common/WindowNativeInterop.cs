using Microsoft.UI;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Mizon.Windows.Common;

public class WindowNativeInterop
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE dwAttribute, ref int pvAttribute, int cbAttribute);

    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x00080000;
    private enum DWMWINDOWATTRIBUTE
    {
        DWMWA_NCRENDERING_ENABLED = 1,
        DWMWA_NCRENDERING_POLICY = 2,
        DWMWA_TRANSITIONS_FORCEDISABLED = 3,
        DWMWA_ALLOW_NCPAINT = 4,
        DWMWA_CAPTION_BUTTON_BOUNDS = 5,
        DWMWA_NONCLIENT_RTL_LAYOUT = 6,
        DWMWA_FORCE_ICONIC_REPRESENTATION = 7,
        DWMWA_FLIP3D_POLICY = 8,
        DWMWA_EXTENDED_FRAME_BOUNDS = 9,
        DWMWA_HAS_ICONIC_BITMAP = 10,
        DWMWA_DISALLOW_PEEK = 11,
        DWMWA_EXCLUDED_FROM_PEEK = 12,
        DWMWA_CLOAK = 13,
        DWMWA_CLOAKED = 14,
        DWMWA_FREEZE_REPRESENTATION = 15,
        DWMWA_PASSIVE_UPDATE_MODE = 16,
        DWMWA_USE_HOSTBACKDROPBRUSH = 17,
        DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        DWMWA_WINDOW_CORNER_PREFERENCE = 33,
        DWMWA_BORDER_COLOR = 34,
        DWMWA_CAPTION_COLOR = 35,
        DWMWA_TEXT_COLOR = 36,
        DWMWA_VISIBLE_FRAME_BORDER_THICKNESS = 37,
        DWMWA_SYSTEMBACKDROP_TYPE = 38,
        DWMWA_MICA_EFFECT = 1029
    }

    public static void RemoveNativeWindowMenu(Window window)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(window);
        int style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~WS_SYSMENU;
        SetWindowLong(hwnd, GWL_STYLE, style);
    }

    public static void SetWindowBorderColor(Window window, Color color)
    {
        RestoreWindowBorderColor(window, ToColorRef(color));
    }

    public static int GetWindowBorderColorValue(Window window)
    {
        // DWMWA_BORDER_COLOR is set-only: DwmGetWindowAttribute returns E_INVALIDARG.
        // Keep the last successfully applied frame per window instead of reading it back.
        return BorderAnimations.TryGetValue(window, out var animation) ? animation.CurrentColor : -1;
    }

    public static void RestoreWindowBorderColor(Window window, int color)
    {
        if (BorderAnimations.TryGetValue(window, out var animation)) animation.Cancel();
        ApplyWindowBorderColor(window, color);
    }

    public static ValueTask AnimateWindowBorderColor(Window window, Color to, int durationMs = 240) =>
        AnimateWindowBorderColor(window, ToColorRef(to), durationMs);

    public static ValueTask AnimateWindowBorderColor(Window window, int to, int durationMs = 240) =>
        BorderAnimations.GetValue(window, static _ => new BorderAnimation()).AnimateAsync(window, to, durationMs);

    private static readonly ConditionalWeakTable<Window, BorderAnimation> BorderAnimations = new();

    private static int ToColorRef(Color color) => (color.B << 16) | (color.G << 8) | color.R;

    private static void ApplyWindowBorderColor(Window window, int color)
    {
        if (DwmSetWindowAttribute(WindowNative.GetWindowHandle(window), DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, ref color, 4) >= 0)
            BorderAnimations.GetValue(window, static _ => new BorderAnimation()).CurrentColor = color;
    }

    private sealed class BorderAnimation
    {
        private readonly UISettings _settings = new();
        private CancellationTokenSource? _pending;
        internal int CurrentColor = -1;

        internal void Cancel() => _pending?.Cancel();

        internal async ValueTask AnimateAsync(Window window, int target, int durationMs)
        {
            Cancel();
            // Continue from the last applied frame, including an interrupted transition.
            var current = GetWindowBorderColorValue(window);
            using var cancellation = new CancellationTokenSource();
            _pending = cancellation;
            try
            {
                // -2 means no border; -1 means restore the Windows default accent.
                if (current == target || current == -2 || target == -2 || durationMs <= 0 || !_settings.AnimationsEnabled)
                {
                    ApplyWindowBorderColor(window, target);
                    return;
                }

                var from = ResolveColor(current);
                var to = ResolveColor(target);
                var started = Stopwatch.GetTimestamp();
                while (true)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var progress = Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds / durationMs, 0, 1);
                    var eased = progress * progress * (3 - 2 * progress);
                    var color = Color.FromArgb(255,
                        (byte)Math.Round(from.R + (to.R - from.R) * eased),
                        (byte)Math.Round(from.G + (to.G - from.G) * eased),
                        (byte)Math.Round(from.B + (to.B - from.B) * eased));
                    ApplyWindowBorderColor(window, ToColorRef(color));
                    if (progress >= 1) break;
                    await Task.Delay(16, cancellation.Token);
                }
                // Preserve the native default-color sentinel after the transition finishes.
                ApplyWindowBorderColor(window, target);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(_pending, cancellation)) _pending = null;
            }
        }

        private Color ResolveColor(int color) => color == -1
            ? _settings.GetColorValue(UIColorType.Accent)
            : Color.FromArgb(255, (byte)(color & 0xFF), (byte)((color >> 8) & 0xFF), (byte)((color >> 16) & 0xFF));
    }

    //public static Color GetWindowBorderColor(Window window)
    //{
    //    IntPtr hwnd = WindowNative.GetWindowHandle(window);

    //    int colorRef;
    //    int hr = DwmGetWindowAttribute(
    //        hwnd,
    //        DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR,
    //        out colorRef,
    //        sizeof(int));

    //    if (hr != 0)
    //        return Colors.Transparent; // یا یک مقدار پیش‌فرض دیگر

    //    byte r = (byte)(colorRef & 0xFF);
    //    byte g = (byte)((colorRef >> 8) & 0xFF);
    //    byte b = (byte)((colorRef >> 16) & 0xFF);

    //    return Color.FromArgb(255, r, g, b);
    //}

    public static void HideFromTaskbar(Window window)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(window);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_APPWINDOW = 0x00040000;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        // WS_EX_APPWINDOW باعث می‌شود در Taskbar نمایش داده شود
        // WS_EX_TOOLWINDOW باعث می‌شود پنجره مثل پنجره کمکی/Popup باشد و در Taskbar نیاید
        exStyle &= ~WS_EX_APPWINDOW;
        exStyle |= WS_EX_TOOLWINDOW;

        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    public static void RemoveWindowBorder(Window window)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(window);

        const int GWL_STYLE = -16;
        const int WS_CAPTION = 0x00C00000;       // Title bar + border
        const int WS_THICKFRAME = 0x00040000;    // Resizable border
        const int WS_SYSMENU = 0x00080000;       // System menu (close, minimize, maximize)
        const int WS_MINIMIZEBOX = 0x00020000;
        const int WS_MAXIMIZEBOX = 0x00010000;
        const int WS_EX_NOACTIVATE = 0x08000000;

        int style = GetWindowLong(hwnd, GWL_STYLE);

        // حذف همه استایل‌هایی که باعث border و title bar می‌شوند
        style &= ~(WS_CAPTION | WS_EX_NOACTIVATE | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);

        SetWindowLong(hwnd, GWL_STYLE, style);

        // برای اعمال تغییرات باید پنجره را ری‌درآو کنیم
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
}
