using Microsoft.Graphics.Canvas;
using System;
using Windows.Foundation;
using Windows.UI;

namespace Mizon.Windows.Common;

internal readonly record struct HeroMediaPalette(Color Base, Color Accent, Color Highlight)
{
    private const int SampleSize = 24;

    public static HeroMediaPalette? Sample(CanvasDevice device, CanvasBitmap bitmap)
    {
        using var sample = new CanvasRenderTarget(device, SampleSize, SampleSize, 96);
        using (var drawing = sample.CreateDrawingSession())
        {
            drawing.Clear(Microsoft.UI.Colors.Transparent);
            drawing.DrawImage(bitmap, new Rect(0, 0, SampleSize, SampleSize), bitmap.Bounds,
                1, CanvasImageInterpolation.Linear);
        }
        return Extract(sample.GetPixelColors());
    }

    internal static HeroMediaPalette? Extract(ReadOnlySpan<Color> pixels)
    {
        Span<ColorBucket> buckets = stackalloc ColorBucket[512];
        buckets.Clear();
        foreach (var pixel in pixels)
        {
            if (pixel.A < 128) continue;
            var index = ((pixel.R >> 5) << 6) | ((pixel.G >> 5) << 3) | (pixel.B >> 5);
            ref var bucket = ref buckets[index];
            bucket.Count++;
            bucket.Red += pixel.R;
            bucket.Green += pixel.G;
            bucket.Blue += pixel.B;
        }

        var primary = Pick(buckets, null, null);
        if (primary is not { } baseColor) return null;
        var accent = Pick(buckets, baseColor, null);
        var highlight = Pick(buckets, baseColor, accent);
        // Monochrome material keeps its own hue and supplies tonal stops.
        return new(Lift(baseColor, 125), Lift(accent ?? baseColor, 175), Lift(highlight ?? accent ?? baseColor, 225));
    }

    private static Color? Pick(ReadOnlySpan<ColorBucket> buckets, Color? first, Color? second)
    {
        Color? best = null;
        var bestScore = 0d;
        foreach (var bucket in buckets)
        {
            if (bucket.Count == 0) continue;
            var color = bucket.Average;
            if ((first is { } a && DistanceSquared(color, a) < 52 * 52)
                || (second is { } b && DistanceSquared(color, b) < 52 * 52)) continue;
            var peak = Math.Max(color.R, Math.Max(color.G, color.B));
            var floor = Math.Min(color.R, Math.Min(color.G, color.B));
            // Prefer substantial regions and give colored detail a chance
            // against black letterboxing or a very large neutral backdrop.
            var score = Math.Sqrt(bucket.Count) * (.16 + .35 * peak / 255 + 1.25 * (peak - floor) / 255);
            if (score <= bestScore) continue;
            bestScore = score;
            best = color;
        }
        return best;
    }

    private static int DistanceSquared(Color a, Color b) =>
        (a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B);

    private static Color Lift(Color color, int minimumPeak)
    {
        var peak = Math.Max(color.R, Math.Max(color.G, color.B));
        if (peak >= minimumPeak) return color;
        var ratio = (minimumPeak - peak) / (255d - peak);
        byte LiftChannel(byte value) => (byte)Math.Round(value + (255 - value) * ratio);
        return Color.FromArgb(255, LiftChannel(color.R), LiftChannel(color.G), LiftChannel(color.B));
    }

    private struct ColorBucket
    {
        public int Count, Red, Green, Blue;
        public readonly Color Average => Color.FromArgb(255, (byte)(Red / Count), (byte)(Green / Count), (byte)(Blue / Count));
    }
}
