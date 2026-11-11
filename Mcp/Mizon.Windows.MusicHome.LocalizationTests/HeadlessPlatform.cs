using System.Globalization;
using System.Xml.Linq;

// Only platform/resource boundaries are doubled. The view models and response
// contracts compiled by this executable are the production files.
namespace Microsoft.UI.Xaml.Media
{
    public class ImageSource;
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage : Media.ImageSource
    {
        public int DecodePixelWidth { get; set; }
        public Uri? UriSource { get; set; }
    }
}

namespace Windows.UI
{
    public readonly record struct Color(byte A, byte R, byte G, byte B)
    {
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    }
}

namespace FluentIcons.Common
{
    public enum Symbol { Comment, Heart, Emoji, Headphones, MusicNote2, Image, People }
}

namespace Mizon.Windows.Common
{
    public static class ImageSourceFactory
    {
        public static int Calls { get; private set; }
        public static Microsoft.UI.Xaml.Media.ImageSource Create(string path, int width)
        {
            Calls++;
            return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = width, UriSource = new Uri(path) };
        }
    }
}

namespace Mizon.Windows.Services
{
    public sealed class LocalizationService
    {
        public static LocalizationService Current { get; } = new();
        public string LanguageTag { get; private set; } = "fa-IR";
        public bool IsPersian => LanguageTag == "fa-IR";
        public static IReadOnlyDictionary<string, string> ReadResources(string language) =>
            XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Resources", language + ".resw"))
                .Root!.Elements("data").ToDictionary(node => node.Attribute("name")!.Value, node => node.Element("value")!.Value);
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _resources = new()
        {
            ["fa-IR"] = ReadResources("fa-IR"), ["en-US"] = ReadResources("en-US")
        };
        public void SetLanguage(string language) => LanguageTag = language;
        public string GetString(string key) => _resources[LanguageTag][key["MusicWidgets/".Length..]];
        public string Format(string key, params object?[] arguments) =>
            string.Format(CultureInfo.GetCultureInfo(LanguageTag), GetString(key), arguments);
    }
}
