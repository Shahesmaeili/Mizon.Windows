using Symbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Mizon.Windows.UserControls;

public sealed partial class DiscoverySectionHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DiscoverySectionHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Symbol), typeof(DiscoverySectionHeader), new PropertyMetadata(Symbol.MusicNote1));
    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
        nameof(AccentColor), typeof(Color), typeof(DiscoverySectionHeader),
        new PropertyMetadata(Color.FromArgb(255, 126, 204, 246)));
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(DiscoverySectionHeader), new PropertyMetadata(false, CompactChanged));

    public DiscoverySectionHeader() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public Symbol Icon
    {
        get => (Symbol)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private static void CompactChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var header = (DiscoverySectionHeader)sender;
        var compact = (bool)args.NewValue;
        header.TitleText.FontSize = compact ? 14 : 20;
        header.TitleText.FontWeight = compact ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Bold;
        header.TitleText.LineHeight = compact ? 24 : 32;
        header.IconFrame.Width = header.IconFrame.Height = compact ? 30 : 38;
        header.IconFrame.CornerRadius = new CornerRadius(compact ? 10 : 12);
        header.HeaderIcon.FontSize = compact ? 15 : 18;
    }
}
