using System;

namespace Mizon.Windows.Services;

internal static class MusicWaveformTimeline
{
    internal const double Width = 1500d;
    private const double MarkerRadius = 14d;

    internal static int GetBarIndex(double contentX, int barCount) =>
        !double.IsFinite(contentX) || contentX < 0 || contentX > Width || barCount <= 0
            ? -1
            : Math.Min(barCount - 1, (int)(contentX * barCount / Width));

    internal static double GetCommentStackOffset(int stackIndex) => (stackIndex % 5) switch
    {
        0 => 0,
        1 => -6,
        2 => 4,
        3 => -12,
        _ => -18
    };

    internal static TimeSpan? GetTimestamp(double contentX, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || !double.IsFinite(contentX))
            return null;

        var ratio = Math.Clamp(contentX / Width, 0d, 1d);
        return TimeSpan.FromTicks((long)Math.Round(duration.Ticks * ratio));
    }

    internal static double GetMarkerLeft(TimeSpan? timestamp, TimeSpan duration, int fallbackIndex)
    {
        if (timestamp is null)
            return 16 + fallbackIndex * 32d;
        if (duration <= TimeSpan.Zero)
            return 16;

        var ratio = Math.Clamp((double)timestamp.Value.Ticks / duration.Ticks, 0d, 1d);
        return ratio * Width - MarkerRadius;
    }
}
