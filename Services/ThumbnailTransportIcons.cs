using FluentIcons.Common;
using Symbol = FluentIcons.Common.Symbol;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Services;

/// <summary>Rasterizes the installed FluentIcons font itself into DPI-sized white HICONs.</summary>
internal sealed partial class ThumbnailTransportIcons : IDisposable
{
    private const float GlyphScale = 0.82f;
    internal nint Previous, Play, Pause, Next, FullScreen;
    internal ThumbnailTransportIcons(nint window)
    {
        var size = Math.Max(16, GetSystemMetricsForDpi(49, GetDpiForWindow(window))); // SM_CXSMICON
        try
        {
            Previous = Create(Symbol.Previous, size);
            Play = Create(Symbol.Play, size);
            Pause = Create(Symbol.Pause, size);
            Next = Create(Symbol.Next, size);
            FullScreen = Create(Symbol.FullScreenMaximize, size);
        }
        catch { Dispose(); throw; }
    }
    private static nint Create(Symbol symbol, int size)
    {
        var resolver = new GlyphResolver { Symbol = symbol, IconVariant = IconVariant.Filled };
        var device = CanvasDevice.GetSharedDevice();
        using var format = new CanvasTextFormat { FontFamily = resolver.FontSource, FontSize = 100 };
        using var text = new CanvasTextLayout(device, resolver.Text, format, 0, 0);
        using var geometry = CanvasGeometry.CreateText(text);
        var bounds = geometry.ComputeBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException($"Empty Fluent icon: {symbol}.");
        var scale = (float)((size - 2) * GlyphScale / Math.Max(bounds.Width, bounds.Height));
        var transform = Matrix3x2.CreateTranslation(-(float)(bounds.X + bounds.Width / 2), -(float)(bounds.Y + bounds.Height / 2))
            * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(size / 2f, size / 2f);
        using var target = new CanvasRenderTarget(device, size, size, 96);
        using (var draw = target.CreateDrawingSession())
        {
            draw.Clear(Colors.Transparent);
            draw.Transform = transform;
            draw.FillGeometry(geometry, Colors.White);
        }
        var pixels = target.GetPixelBytes(); // premultiplied BGRA, exactly the native color bitmap format
        var color = CreateBitmap(size, size, 1, 32, pixels);
        var mask = CreateBitmap(size, size, 1, 1, new byte[((size + 15) / 16 * 2) * size]);
        try
        {
            if (color == 0 || mask == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            var icon = CreateIconIndirect(new IconInfo { IsIcon = 1, Color = color, Mask = mask });
            if (icon == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            return icon;
        }
        finally { if (color != 0) DeleteObject(color); if (mask != 0) DeleteObject(mask); }
    }
    public void Dispose()
    {
        foreach (var icon in new[] { Previous, Play, Pause, Next, FullScreen }) if (icon != 0) DestroyIcon(icon);
        Previous = Play = Pause = Next = FullScreen = 0;
    }
    private sealed class GlyphResolver : FluentIcons.WinUI.SymbolIcon
    { internal string Text => IconText; internal string FontSource => IconFont.Source; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { internal int IsIcon; internal uint X, Y; internal nint Mask, Color; }
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint window);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateBitmap(int width, int height, uint planes, uint bits, byte[] pixels);
    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint CreateIconIndirect(in IconInfo info);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyIcon(nint icon);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint bitmap);
}
