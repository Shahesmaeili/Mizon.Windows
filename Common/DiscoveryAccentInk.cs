using Microsoft.UI.Xaml;
using Windows.UI;

namespace Mizon.Windows.Common;

internal static class DiscoveryAccentInk
{
    internal static Color ForTheme(Color accent, ElementTheme theme) => theme == ElementTheme.Light
        ? Color.FromArgb(255, (byte)(accent.R * .42), (byte)(accent.G * .42), (byte)(accent.B * .42))
        : accent;
}
