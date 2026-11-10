using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json;
using Windows.Foundation;

namespace Mizon.Windows.PaletteProvider;

// Explicit local compatibility check against the renderer shipped with Search.
// No package/COM registration, shell activation, backend request or visible window.
internal static partial class Program
{
    private static int _checks;
    private static string? _reselectionImagePath;
    internal static void Activity() { } // This diagnostic does not host a COM server.

    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--native-renderer", StringComparer.Ordinal))
        {
            Console.WriteLine("Opt in with --native-renderer. Requires the installed Windows Search Adaptive Cards runtime.");
            return 2;
        }
        var imageArgument = Array.IndexOf(args, "--image-reselection");
        if (imageArgument >= 0)
        {
            if (imageArgument + 1 >= args.Length || !File.Exists(args[imageArgument + 1]))
                throw new ArgumentException("--image-reselection requires an existing local image.");
            _reselectionImagePath = System.IO.Path.GetFullPath(args[imageArgument + 1]);
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new TestApplication();
        });
        return Environment.ExitCode;
    }

    private sealed class TestApplication : Application
    {
        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            try { await RunAsync(); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { Exit(); }
        }
    }

    private static async Task RunAsync()
    {
        using var renderer = new NativeCardRenderer();
        using (var oldCard = renderer.Render("""
            {"type":"AdaptiveCard","version":"1.5","body":[
              {"type":"TextBlock","text":"MIZON regression header"},
              {"type":"Container","style":"emphasis","items":[
                {"type":"Image","url":"file:///nonexistent.png","style":"Person","width":"156px","height":"156px"},
                {"type":"TextBlock","text":"Legacy profile title","spacing":"Large"},
                {"type":"TextBlock","text":"Profile information","spacing":"Large"}]},
              {"type":"ActionSet","actions":[{"type":"Action.Submit","title":"Legacy action"}]}]}
            """))
        {
            Arrange(oldCard.Element, 500, 250);
            Check(IsTextVisible(oldCard.Element, "MIZON regression header", 500, 250), "legacy tall card leaves its header visible");
            Check(!IsTextVisible(oldCard.Element, "Legacy profile title", 500, 250), "WholeItemsPanel moves overflowing container outside the preview");
            Check(!IsTextVisible(oldCard.Element, "Legacy action", 500, 250), "WholeItemsPanel also hides controls after that container");
            Arrange(oldCard.Element, 500, 700);
            Check(IsTextVisible(oldCard.Element, "Legacy action", 500, 700), "the same legacy action is present when enough height is available");
        }

        // Search can remove the renderer's default 20px padding. Verify the
        // production insets themselves, not accidental stock-host whitespace.
        renderer.SetHostPadding(0);
        var results = new[]
        {
            new PaletteResultData { Kind = PaletteResultKind.Client, PublicId = Guid.NewGuid(), FirstName = "علی", LastName = "اسماعیلی", Username = "renderer_fixture" },
            new PaletteResultData { Kind = PaletteResultKind.Music, PublicId = Guid.NewGuid(), Title = "Renderer fixture", PersianTitle = "آهنگ آزمایشی", LikesCount = 125, ViewsCount = 987, AlbumTitle = "آلبوم", GenreText = "پاپ", DurationText = "03:30" }
        };
        foreach (var result in results)
        {
            foreach (var hasImage in new[] { false, true })
            {
                // The renderer clips the original source with an ellipse;
                // layout must also remain usable before the bitmap decodes.
                var states = result.Kind == PaletteResultKind.Music
                    ? new (bool CanDownload, int? Progress)[] { (false, null), (true, null), (true, 42) }
                    : new (bool CanDownload, int? Progress)[] { (false, null) };
                foreach (var (canDownload, progress) in states)
                {
                    var snapshot = new PaletteResultSnapshot(hasImage ? "file:///nonexistent-renderer-fixture.png" : null, progress.HasValue, progress, progress.HasValue ? "در حال دانلود آهنگ" : null, canDownload);
                    var cardJson = ReadProductionTemplate(result, snapshot);
                    using (var json = JsonDocument.Parse(cardJson))
                    {
                        var surfaces = CardElements(json.RootElement).Where(element =>
                            element.TryGetProperty("type", out var type) && type.GetString() is "AdaptiveCard" or "Container" or "ColumnSet" or "Column");
                        Check(surfaces.All(element => !element.TryGetProperty("style", out _) && !element.TryGetProperty("backgroundImage", out _) && !element.TryGetProperty("backgroundColor", out _)),
                            "preview layout inherits all surface styling from Windows");
                    }
                    if (result.Kind == PaletteResultKind.Music)
                        VerifyMusicActionContract(cardJson, canDownload, progress.HasValue);
                    using var card = renderer.Render(cardJson);
                    Check(card.Errors.Count == 0, $"{result.Kind}: production card renders without native errors ({string.Join("; ", card.Errors)})");
                    if (hasImage)
                        Check(Descendants(card.Element).OfType<Ellipse>().Count() == 1, $"{result.Kind}: native Person renderer clips the original cover/profile with an ellipse");
                    // The headless tree has no XamlRoot, so bitmap decode may wait
                    // for a visible host. Exercise the declared 80px image footprint
                    // explicitly, including the worst case after loading finishes.
                    foreach (var image in Descendants(card.Element).OfType<Image>())
                        image.Width = image.Height = 80;
                    await Task.Delay(40);
                    foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                    {
                        card.Element.RequestedTheme = theme;
                        Arrange(card.Element, 500, 300);
                        VerifyActionTheme(card.Element, result.Kind, theme);
                    }
                    foreach (var width in new[] { 360d, 500d })
                    foreach (var height in new[] { 250d, 300d })
                    {
                        Arrange(card.Element, width, height);
                        var label = $"{result.Kind}, image={hasImage}, canDownload={canDownload}, progress={progress}, size={width}x{height}";
                        Check(IsTextVisible(card.Element, result.DisplayTitle, width, height), $"{label}: title remains inside the preview");
                        Check(IsTextVisible(card.Element, result.Kind == PaletteResultKind.Music ? "پخش" : "مشاهدهٔ پروفایل", width, height), $"{label}: primary action remains visible");
                        Check(IsTextVisible(card.Element, "کپی لینک", width, height), $"{label}: copy action remains visible");
                        VerifyContentInsets(card.Element, width, height, label);
                        if (hasImage)
                        {
                            Check(Descendants(card.Element).OfType<FrameworkElement>().Any(element => element is Image or Ellipse && IsVisible(card.Element, element, width, height)), $"{label}: 80px image footprint remains visible");
                            var portrait = Descendants(card.Element).OfType<Ellipse>().Single();
                            var imageBounds = portrait.TransformToVisual(card.Element).TransformBounds(new Rect(0, 0, portrait.ActualWidth, portrait.ActualHeight));
                            Check(imageBounds.Top >= 12 && imageBounds.Left >= 12 && imageBounds.Right <= width - 12, $"{label}: portrait has real inset padding, including above it");
                        }
                        if (result.Kind == PaletteResultKind.Music)
                        {
                            Check(IsTextVisible(card.Element, "جزئیات", width, height), $"{label}: details action remains visible");
                            if (canDownload)
                                Check(IsTextVisible(card.Element, progress.HasValue ? "لغو" : "دانلود", width, height), $"{label}: download action remains visible");
                            VerifyMusicActionRow(card.Element, canDownload, width, height, label);
                        }
                    }
                }
            }
        }
        await VerifyDemoFallbacksAsync(renderer);
        Console.WriteLine($"Native renderer PASS {_checks} assertions. Windows Search's custom host styling is a separate visual check.");
        if (_reselectionImagePath is not null)
            await ImageReselectionTests.RunAsync(renderer, _reselectionImagePath);
    }

    private static unsafe string ReadProductionTemplate(PaletteResultData data, PaletteResultSnapshot snapshot)
    {
        var baseline = NativeObject.LiveCount;
        nint content = 0, view = 0, template = 0, text = 0;
        try
        {
            content = PalettePreviewData.BuildPreview(new FixtureState(data, snapshot));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(content)[7])(content, &view));
            template = NativeMethods.As(view, PalettePreviewData.TemplateViewId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(template)[6])(template, &text));
            return NativeMethods.ReadString(text);
        }
        finally
        {
            NativeMethods.WindowsDeleteString(text); NativeMethods.Release(template); NativeMethods.Release(view); NativeMethods.Release(content);
            Check(NativeObject.LiveCount == baseline, "production preview fixture releases its native references");
        }
    }

    private sealed class FixtureState(PaletteResultData data, PaletteResultSnapshot snapshot) : IPaletteResultState
    {
        public PaletteResultData Result => data;
        public PaletteResultSnapshot GetSnapshot() => snapshot;
        public event Action? Changed { add { } remove { } }
        public void Invoke(string action) => throw new InvalidOperationException("Rendering must not execute an action.");
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.InvalidateMeasure();
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static bool IsTextVisible(FrameworkElement root, string expected, double width, double height)
    {
        foreach (var node in Descendants(root))
        {
            if (node is not TextBlock text || text.Text != expected || text.Visibility != Visibility.Visible || text.ActualHeight <= 0 || text.ActualWidth <= 0) continue;
            if (IsVisible(root, text, width, height)) return true;
            var bounds = text.TransformToVisual(root).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
            Console.WriteLine($"  Clipped text '{expected}' at {bounds} in {width}x{height}");
        }
        return false;
    }

    private static void VerifyActionTheme(FrameworkElement root, PaletteResultKind kind, ElementTheme theme)
    {
        var buttons = Descendants(root).OfType<Button>().ToArray();
        var primaryTitle = kind == PaletteResultKind.Music ? "پخش" : "مشاهدهٔ پروفایل";
        var primary = buttons.Single(button => Descendants(button).OfType<TextBlock>().Any(text => text.Text == primaryTitle));
        var secondary = buttons.Single(button => Descendants(button).OfType<TextBlock>().Any(text => text.Text == "کپی لینک"));
        Check(BrushKey(primary.Background) == BrushKey(secondary.Background), $"{kind}, {theme}: primary action uses the same themed background as other actions");
        Check(BrushKey(primary.Foreground) == BrushKey(secondary.Foreground), $"{kind}, {theme}: primary action uses the same themed foreground as other actions");
        Check(primary.Background is not SolidColorBrush { Color: { A: 255, R: 255, G: 255, B: 255 } }, $"{kind}, {theme}: primary action has no forced opaque white background");
    }

    private static void VerifyContentInsets(FrameworkElement root, double width, double height, string label)
    {
        var content = Descendants(root).OfType<FrameworkElement>()
            .Where(element => element is TextBlock or Button or Image or Ellipse)
            .Where(element => IsVisible(root, element, width, height))
            .Select(element => element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)))
            .ToArray();
        Check(content.Length > 0 && content.All(bounds => bounds.Left >= 11.5 && bounds.Right <= width - 11.5 && bounds.Top >= 11.5),
            $"{label}: all visible content has top/left/right spacing with zero host padding");
        Check(content.Max(bounds => bounds.Bottom) <= root.DesiredSize.Height - 11.5,
            $"{label}: measured content retains its bottom inset");
    }

    private static void VerifyMusicActionContract(string cardJson, bool canDownload, bool isDownloading)
    {
        using var document = JsonDocument.Parse(cardJson);
        var actionSets = CardElements(document.RootElement)
            .Where(element => element.TryGetProperty("type", out var type) && type.GetString() == "ActionSet").ToArray();
        Check(actionSets.Length == 1, "expanded music preview contains exactly one action row");
        var actions = actionSets[0].GetProperty("actions").EnumerateArray().ToArray();
        var expected = canDownload
            ? new[] { "play", "details", isDownloading ? "cancel-download" : "download", "copy-link" }
            : new[] { "play", "details", "copy-link" };
        Check(actions.Select(action => action.GetProperty("data").GetProperty("action").GetString()).SequenceEqual(expected), "single action row preserves play/details/download-or-cancel/copy handlers");
        Check(actions.All(action => !string.IsNullOrWhiteSpace(action.GetProperty("tooltip").GetString())), "compact music actions retain descriptive tooltips");
    }

    private static IEnumerable<JsonElement> CardElements(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
                foreach (var child in CardElements(property.Value)) yield return child;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray())
                foreach (var child in CardElements(value)) yield return child;
    }

    private static void VerifyMusicActionRow(FrameworkElement root, bool canDownload, double width, double height, string label)
    {
        var buttons = Descendants(root).OfType<Button>().ToArray();
        Check(buttons.Length == (canDownload ? 4 : 3), $"{label}: only the available music actions are rendered");
        var bounds = buttons.Select(button => button.TransformToVisual(root).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight))).ToArray();
        Check(bounds.Max(rect => rect.Top) - bounds.Min(rect => rect.Top) < 1, $"{label}: all music buttons share one row");
        Check(buttons.All(button => IsVisible(root, button, width, height)), $"{label}: every music button stays inside the preview");
        var ordered = bounds.OrderBy(rect => rect.Left).ToArray();
        Check(ordered.Zip(ordered.Skip(1), (left, right) => left.Right <= right.Left + 1).All(separate => separate), $"{label}: music buttons do not overlap horizontally");
        var rowTop = bounds.Min(rect => rect.Top);
        var rowBottom = bounds.Max(rect => rect.Bottom);
        var textBounds = Descendants(root).OfType<TextBlock>()
            .Where(text => IsVisible(root, text, width, height))
            .Select(text => text.TransformToVisual(root).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight))).ToArray();
        var above = textBounds.Where(rect => rect.Bottom <= rowTop + .5).ToArray();
        var below = textBounds.Where(rect => rect.Top >= rowBottom - .5).ToArray();
        Check(above.Length > 0 && rowTop - above.Max(rect => rect.Bottom) >= 7.5, $"{label}: action row has at least 8px above it");
        Check(below.Length == 0 || below.Min(rect => rect.Top) - rowBottom >= 7.5, $"{label}: action row has at least 8px below it");
    }

    private static string BrushKey(Brush? brush) => brush switch
    {
        SolidColorBrush solid => $"{solid.Color}/{solid.Opacity}",
        null => "null",
        _ => brush.GetType().FullName ?? string.Empty
    };

    private static bool IsVisible(FrameworkElement root, FrameworkElement element, double width, double height)
    {
        if (element.Visibility != Visibility.Visible || element.ActualHeight <= 0 || element.ActualWidth <= 0) return false;
        var bounds = element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= width + 1 && bounds.Bottom <= height + 1;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var node in Descendants(VisualTreeHelper.GetChild(root, i))) yield return node;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }
}
