using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using Windows.Foundation;

namespace Mizon.Windows.PaletteProvider;

internal static partial class Program
{
    private static async Task VerifyDemoFallbacksAsync(NativeCardRenderer renderer)
    {
        // The standalone renderer has no RunUX registrations for Slider,
        // ToggleSwitch, Chart.Line, FontIcon or progress extensions. This test
        // verifies their declared fallbacks and surrounding layout, not the
        // final host-rendered custom controls or their pointer interactions.
        Console.WriteLine("Demo layout check: stock renderer FALLBACKS, not RunUX custom controls.");
        renderer.SetHostPadding(0);
        using var state = new PaletteCapabilityDemoState();
        for (var section = 0; section < 5; section++)
        {
            var (template, _) = ((IPaletteCustomPreview)state).GetPreview();
            using var json = JsonDocument.Parse(template);
            var surfaces = CardElements(json.RootElement).Where(element =>
                element.TryGetProperty("type", out var type) && type.GetString() is "AdaptiveCard" or "Container" or "ColumnSet" or "Column");
            Check(surfaces.All(element => !element.TryGetProperty("backgroundColor", out _) &&
                !element.TryGetProperty("backgroundImage", out _) && !element.TryGetProperty("style", out _)),
                $"demo {section + 1}: card surface inherits the Windows theme");

            using var card = renderer.Render(template);
            Check(card.Errors.Count == 0,
                $"demo {section + 1}: fallback rendering has no native errors ({string.Join("; ", card.Errors)})");
            await Task.Delay(20);
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            foreach (var width in new[] { 360d, 500d })
            {
                const double height = 300;
                card.Element.RequestedTheme = theme;
                Arrange(card.Element, width, height);
                var label = $"demo {section + 1}, {theme}, {width}x{height}";
                var buttons = Descendants(card.Element).OfType<Button>()
                    .Where(button => button.Visibility == Visibility.Visible && button.ActualWidth > 0 && button.ActualHeight > 0).ToArray();
                Check(buttons.Length > 0 && buttons.All(button => IsVisible(card.Element, button, width, height)),
                    $"{label}: all action and section-navigation buttons fit");
                var texts = Descendants(card.Element).OfType<TextBlock>()
                    .Where(text => text.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(text.Text) &&
                        text.ActualWidth > 0 && text.ActualHeight > 0).ToArray();
                var clipped = texts.Where(text => !IsVisible(card.Element, text, width, height)).ToArray();
                if (clipped.Length > 0)
                    foreach (var text in clipped)
                        Console.WriteLine($"  Clipped demo text '{text.Text}' at {text.TransformToVisual(card.Element).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight))}");
                Check(texts.Length > 0 && clipped.Length == 0, $"{label}: fallback text remains inside preview bounds");
                var bounds = texts.Select(text => text.TransformToVisual(card.Element)
                    .TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight))).ToArray();
                Check(bounds.All(rect => rect.Left >= 11.5 && rect.Right <= width - 11.5 && rect.Top >= 9.5),
                    $"{label}: explicit 12px side gutters and 10px top inset survive zero host padding");
                Check(bounds.Max(rect => rect.Bottom) <= card.Element.DesiredSize.Height - 9.5,
                    $"{label}: bottom inset remains visible");
                Check(buttons.All(button => button.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush { Color: { A: 255, R: 255, G: 255, B: 255 } }),
                    $"{label}: buttons do not force an opaque white background");
            }
            state.Invoke("demo-next");
        }
    }
}
