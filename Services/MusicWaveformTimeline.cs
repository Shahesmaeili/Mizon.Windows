using System;

namespace Mizon.Windows.Services;

internal static class MusicWaveformTimeline
{
    internal const double Width = 1500d;
    internal const double Height = 110d;
    internal const double MarkerSize = 28d;
    private const double BarStrokeWidth = 2d;
    private const double MarkerTop = Height * 2 / 3 + 2;
    private const int MarkerStackLevels = 5;

    internal static int GetBarIndex(double contentX, int barCount) =>
        !double.IsFinite(contentX) || contentX < 0 || contentX > Width || barCount <= 0
            ? -1
            : Math.Min(barCount - 1, (int)(contentX * barCount / Width));

    internal static int GetHoverBarIndex(double contentX, int barCount)
    {
        if (barCount <= 0 || contentX > Width) return -1;

        // Each stroke is centered in its slot. Change the highlight at the
        // leading edge of the stroke, beyond the space between bars.
        var leadingGap = (Width / barCount - BarStrokeWidth) / 2;
        return GetBarIndex(contentX - leadingGap, barCount);
    }

    internal static double GetCommentMarkerTop(int stackIndex)
    {
        const double step = (Height - MarkerSize - MarkerTop) / (MarkerStackLevels - 1);
        return MarkerTop + stackIndex % MarkerStackLevels * step;
    }

    internal static bool IsOwnComment(Guid authorId, Guid? currentClientId) =>
        authorId != Guid.Empty && authorId == currentClientId;

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
        return ratio * Width - MarkerSize / 2;
    }
}
