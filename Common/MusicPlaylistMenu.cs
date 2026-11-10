using Symbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Mizon.Windows.Common;

// Built on demand, shared by the compact widget and the paged library browser.
internal static class MusicPlaylistMenu
{
    internal static string Link(Guid id) => $"mizon://playlist/{id:D}";

    internal static MenuFlyout Show(FrameworkElement anchor, ContextRequestedEventArgs args, Guid id, bool canPlay,
        Func<Task> play, Func<Task> open, Action<string> status)
    {
        var menu = Create(id, canPlay, play, open, status);
        args.Handled = true;
        if (args.TryGetPosition(anchor, out var point)) menu.ShowAt(anchor, PointerPlacement(point));
        else menu.ShowAt(anchor);
        return menu;
    }

    internal static FlyoutShowOptions PointerPlacement(Point point) => new()
    {
        // The native popup's transparent shadow can cover the stationary pointer before its
        // input site is ready (microsoft/microsoft-ui-xaml#8829). Keep both its initial position and shadow clear;
        // setting ProtectedCursor on an item cannot fix that shadow window's stale cursor.
        Position = new Point(point.X, point.Y + 8),
        ExclusionRect = new Rect(point.X - 24, point.Y - 24, 48, 32),
        Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
    };

    internal static MenuFlyout Create(Guid id, bool canPlay, Func<Task> play, Func<Task> open, Action<string> status)
    {
        var menu = new MenuFlyout
        {
            SystemBackdrop = new DesktopAcrylicBackdrop(),
            MenuFlyoutPresenterStyle = new Style(typeof(MenuFlyoutPresenter))
            {
                Setters = { new Setter(FrameworkElement.FlowDirectionProperty, FlowDirection.RightToLeft),
                    new Setter(Control.FontFamilyProperty, new FontFamily("/Assets/Fonts/IRANSANS.TTF#IRANSans")) }
            }
        };
        Add("پخش", Symbol.Play, canPlay, play);
        Add("نمایش آهنگ‌ها", Symbol.MusicNote1, true, open);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("کپی کردن لینک پلی‌لیست", Symbol.Link, true, () =>
        {
            var package = new DataPackage();
            package.SetText(Link(id));
            Clipboard.SetContent(package);
            Clipboard.Flush();
            status("لینک پلی‌لیست کپی شد.");
            return Task.CompletedTask;
        });
        return menu;

        void Add(string title, Symbol symbol, bool enabled, Func<Task> action)
        {
            var item = new PlaylistMenuItem
            {
                Text = title, IsEnabled = enabled, FlowDirection = FlowDirection.RightToLeft,
                Icon = new FluentIcons.WinUI.SymbolIcon { Symbol = symbol },
                FontFamily = new FontFamily("/Assets/Fonts/IRANSANS.TTF#IRANSans")
            };
            item.Click += async (_, _) =>
            {
                try { await action(); }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    Services.AppLog.Error("Playlist menu action failed.", exception);
                    status("انجام این عملیات ممکن نشد. دوباره امتحان کن.");
                }
            };
            menu.Items.Add(item);
        }
    }
}

internal sealed partial class PlaylistMenuItem : MenuFlyoutItem
{
    private InputSystemCursor? _cursor;
    public PlaylistMenuItem()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded) return;
        ProtectedCursor = _cursor ??= InputSystemCursor.Create(
            IsEnabled ? InputSystemCursorShape.Hand : InputSystemCursorShape.Arrow);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
    }
}
