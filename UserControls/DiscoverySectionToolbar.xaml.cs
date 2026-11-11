using Mizon.Windows.Services;
using Symbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System;
using Windows.UI;

namespace Mizon.Windows.UserControls;

[ContentProperty(Name = nameof(Actions))]
public sealed partial class DiscoverySectionToolbar : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DiscoverySectionToolbar), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Symbol), typeof(DiscoverySectionToolbar), new PropertyMetadata(Symbol.MusicNote1));
    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
        nameof(AccentColor), typeof(Color), typeof(DiscoverySectionToolbar), new PropertyMetadata(Color.FromArgb(255, 169, 184, 255)));
    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(UIElement), typeof(DiscoverySectionToolbar), new PropertyMetadata(null));

    private bool? _compact;

    public DiscoverySectionToolbar()
    {
        InitializeComponent();
        Loaded += LocalizationLoaded;
        Unloaded += LocalizationUnloaded;
    }

    private void LocalizationLoaded(object sender, RoutedEventArgs args) => Bindings.Update();
    private void LocalizationUnloaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded) Bindings.StopTracking();
    }

    // These headers place the title/icon on the visual end; invert only the header
    // grid in English, leaving the page's independent columns and media untouched.
    public FlowDirection HeaderLayoutDirection(int revision) =>
        Localization.IsPersian ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public Symbol Icon { get => (Symbol)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public UIElement? Actions { get => (UIElement?)GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    private void Toolbar_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (Heading is null || ActionHost is null || !double.IsFinite(args.NewSize.Width) || args.NewSize.Width <= 0) return;
        var compact = args.NewSize.Width < 520;
        if (_compact == compact) return;
        _compact = compact;
        Grid.SetColumn(Heading, compact ? 0 : 1);
        Grid.SetColumnSpan(Heading, compact ? 2 : 1);
        Grid.SetRow(ActionHost, compact ? 1 : 0);
        Grid.SetColumnSpan(ActionHost, compact ? 2 : 1);
        ToolbarGrid.RowSpacing = compact ? 8 : 0;
    }
}
