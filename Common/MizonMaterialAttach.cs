using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace Mizon.Windows.Common;

/// <summary>
/// Project-owned implementation of DevWinUI's AutoSuggestBox acrylic attachment.
/// Keeping it here makes its popup lifecycle observable and fixable without a NuGet rebuild.
/// </summary>
public static class MizonMaterialAttach
{
    private static readonly ConditionalWeakTable<AutoSuggestBox, AutoSuggestBoxState> AutoSuggestBoxStates = new();

    public static readonly DependencyProperty UseAcrylicBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "UseAcrylicBackground",
            typeof(bool),
            typeof(MizonMaterialAttach),
            new PropertyMetadata(false, OnUseAcrylicBackgroundChanged));

    public static bool GetUseAcrylicBackground(DependencyObject obj) =>
        (bool)obj.GetValue(UseAcrylicBackgroundProperty);

    public static void SetUseAcrylicBackground(DependencyObject obj, bool value) =>
        obj.SetValue(UseAcrylicBackgroundProperty, value);

    private static void OnUseAcrylicBackgroundChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not AutoSuggestBox autoSuggestBox)
            return;

        var state = AutoSuggestBoxStates.GetValue(autoSuggestBox, static _ => new AutoSuggestBoxState());

        if ((bool)args.NewValue)
        {
            autoSuggestBox.Loaded -= state.LoadedHandler;
            autoSuggestBox.Loaded += state.LoadedHandler;

            if (autoSuggestBox.IsLoaded)
                AttachToPopup(autoSuggestBox, state);

            return;
        }

        autoSuggestBox.Loaded -= state.LoadedHandler;
        DetachFromPopup(state);
    }

    private static void AttachToPopup(AutoSuggestBox autoSuggestBox, AutoSuggestBoxState state)
    {
        var popup = FindDescendant<Popup>(autoSuggestBox);
        if (popup is null)
            return;

        if (ReferenceEquals(state.Popup, popup))
            return;

        DetachFromPopup(state);

        state.Popup = popup;
        state.PopupOpenedHandler = (_, _) => ApplyAcrylic(autoSuggestBox, state);
        popup.Opened += state.PopupOpenedHandler;
    }

    private static void ApplyAcrylic(AutoSuggestBox autoSuggestBox, AutoSuggestBoxState state)
    {
        if (!GetUseAcrylicBackground(autoSuggestBox))
            return;

        if (state.Popup?.Child is not Border border)
            return;

        if (border.Child is not ListView listView)
            return;

        if (!state.HasOriginalPopupPresentation)
        {
            state.OriginalPopupBackground = border.Background;
            state.OriginalPopupPadding = border.Padding;
            state.OriginalPopupCornerRadius = border.CornerRadius;
            state.HasOriginalPopupPresentation = true;
        }

        border.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        border.CornerRadius = new CornerRadius(0, 0, 8, 8);
        // The template keeps a small bottom padding on this outer Border. The
        // backdrop lives inside its child Grid, so leaving that padding in
        // place creates an opaque strip below the acrylic surface.
        border.Padding = new Thickness();

        // DevWinUI adds SystemBackdropElement while its host Grid is detached.
        // Recent Windows App SDK builds reject that component activation. Attach
        // the Grid first so it has a XamlRoot, then insert the backdrop element.
        var grid = new Grid();
        border.Child = null;
        grid.Children.Add(listView);
        border.Child = grid;

        try
        {
            grid.Children.Insert(0, new SystemBackdropElement
            {
                SystemBackdrop = new DesktopAcrylicBackdrop()
            });
        }
        catch (Exception)
        {
            // A transparent-but-readable fallback for systems where Desktop
            // Acrylic itself is unavailable.
            border.Background = Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"] as Brush;
        }

        state.OriginalListView = listView;
        state.AcrylicHost = grid;
    }

    private static void DetachFromPopup(AutoSuggestBoxState state)
    {
        if (state.Popup is not null && state.PopupOpenedHandler is not null)
            state.Popup.Opened -= state.PopupOpenedHandler;

        if (state.Popup?.Child is Border border)
        {
            if (state.HasOriginalPopupPresentation)
            {
                border.Background = state.OriginalPopupBackground;
                border.Padding = state.OriginalPopupPadding;
                border.CornerRadius = state.OriginalPopupCornerRadius;
            }

            if (ReferenceEquals(border.Child, state.AcrylicHost) && state.OriginalListView is not null)
            {
                border.Child = null;
                border.Child = state.OriginalListView;
            }
        }

        state.Popup = null;
        state.PopupOpenedHandler = null;
        state.OriginalListView = null;
        state.AcrylicHost = null;
        state.OriginalPopupBackground = null;
        state.OriginalPopupPadding = new Thickness();
        state.OriginalPopupCornerRadius = new CornerRadius();
        state.HasOriginalPopupPresentation = false;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
                return typedChild;

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    private sealed class AutoSuggestBoxState
    {
        public AutoSuggestBoxState()
        {
            LoadedHandler = (sender, _) => AttachToPopup((AutoSuggestBox)sender, this);
        }

        public RoutedEventHandler LoadedHandler { get; }
        public Popup? Popup { get; set; }
        public EventHandler<object>? PopupOpenedHandler { get; set; }
        public ListView? OriginalListView { get; set; }
        public Grid? AcrylicHost { get; set; }
        public Brush? OriginalPopupBackground { get; set; }
        public Thickness OriginalPopupPadding { get; set; }
        public CornerRadius OriginalPopupCornerRadius { get; set; }
        public bool HasOriginalPopupPresentation { get; set; }
    }
}
