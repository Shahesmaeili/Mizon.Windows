using System;

namespace Mizon.Windows.Controls;

// Logical pixels; the compositor applies the actual window DPI. All parts use the
// same geometry so a resized avatar cannot leave a colored halo around its badge.
internal readonly record struct PresenceAvatarMetrics(double Size, double BadgeDiameter, double BadgeCenter, double CutoutRadius)
{
    internal static PresenceAvatarMetrics ForSize(double size)
    {
        if (!double.IsFinite(size) || size <= 0 || size > float.MaxValue) return default;
        var badge = size * (9d / 32);
        var center = size - badge / 2 - size / 64;
        return new(size, badge, center, badge / 2 + size * (2.5 / 32));
    }
}
