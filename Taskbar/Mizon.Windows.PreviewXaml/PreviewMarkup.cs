using System.Reflection;

namespace Mizon.Windows.PreviewXaml;

internal static class PreviewMarkup
{
    internal static string Create(bool persian, uint row = 0)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Mizon.Windows.PreviewXaml.PreviewPresence.xaml")
            ?? throw new InvalidOperationException("Missing preview XAML resource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("{DIRECTION}", persian ? "RightToLeft" : "LeftToRight")
            .Replace("{ROW}", row.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{GLOBAL}", persian ? "آنلاین میزون" : "Mizon online")
            .Replace("{FOLLOWERS}", persian ? "دنبال‌کننده‌های آنلاین" : "Followers online");
    }
}
