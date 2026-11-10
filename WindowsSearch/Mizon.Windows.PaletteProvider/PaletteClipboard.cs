using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Mizon.Windows.PaletteProvider;

internal static unsafe class PaletteClipboard
{
    internal static void Copy(string text)
    {
        // An owned message-only window is required for EmptyClipboard/SetClipboardData.
        var window = CreateWindowEx(0, "STATIC", "Mizon Palette Clipboard", 0,
            0, 0, 0, 0, new nint(-3), 0, 0, 0);
        if (window == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint memory = 0;
        var opened = false;
        try
        {
            for (var attempt = 0; attempt < 3 && !opened; attempt++)
            {
                opened = OpenClipboard(window);
                if (!opened && attempt < 2) Thread.Sleep(20);
            }
            if (!opened) throw new Win32Exception(Marshal.GetLastWin32Error());
            memory = GlobalAlloc(2, checked((nuint)((text.Length + 1) * sizeof(char))));
            if (memory == 0) throw new OutOfMemoryException();
            var pointer = GlobalLock(memory);
            if (pointer == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                text.AsSpan().CopyTo(new Span<char>((void*)pointer, text.Length));
                ((char*)pointer)[text.Length] = '\0';
            }
            finally { GlobalUnlock(memory); }
            if (!EmptyClipboard() || SetClipboardData(13, memory) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            memory = 0; // The clipboard owns the allocation after SetClipboardData.
        }
        finally
        {
            if (memory != 0) GlobalFree(memory);
            if (opened) CloseClipboard();
            DestroyWindow(window);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
