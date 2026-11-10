using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;

namespace Mizon.Windows.Pages;

// The two existing navigation entries have no catalog yet. Give them honest, distinct destinations.
public sealed partial class SectionLandingPage : Page
{
    private readonly Image _icon = new() { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _title = new() { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Left };
    public string Destination { get; private set; } = string.Empty;

    public SectionLandingPage()
    {
        FontFamily = (FontFamily)Application.Current.Resources["IranSansFont"];
        var content = new StackPanel { Spacing = 16, MaxWidth = 480, Padding = new Thickness(28), FlowDirection = FlowDirection.RightToLeft };
        content.Children.Add(_icon);
        content.Children.Add(_title);
        content.Children.Add(new TextBlock
        {
            Text = "هنوز محتوایی برای نمایش در این بخش وجود ندارد.", TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left, Opacity = .65, FontSize = 13
        });
        Content = new Border
        {
            Child = content, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(16, 255, 255, 255))
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        Destination = args.Parameter is Dictionary<string, object> parameters && parameters.TryGetValue("Section", out var section) && section is "apps" ? "apps" : "movies";
        _title.Text = Destination == "apps" ? "برنامه‌ها" : "فیلم و سریال";
        _icon.Source = new BitmapImage(new Uri($"ms-appx:///Assets/JumpList/{(Destination == "apps" ? "Apps" : "Video")}.png"));
    }
}
