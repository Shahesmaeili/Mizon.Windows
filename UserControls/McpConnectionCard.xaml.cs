using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;

namespace Mizon.Windows.UserControls;

public sealed partial class McpConnectionCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(McpConnectionItemViewModel), typeof(McpConnectionCard),
        new PropertyMetadata(null, ViewModelChanged));

    private bool _initialized;
    public McpConnectionItemViewModel ViewModel
    {
        get => (McpConnectionItemViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
    public LocalizationService Localization => LocalizationService.Current;
    public McpAccessService Access => McpAccessService.Current;

    public McpConnectionCard()
    {
        InitializeComponent();
        _initialized = true;
    }

    private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (McpConnectionCard)sender;
        if (card._initialized && card.IsLoaded)
            card.Bindings.Update();
    }

    public string Text(string key, int revision) => Localization.GetString("Mcp/" + key, revision);
    public string VersionText(string? version, int revision) => string.IsNullOrWhiteSpace(version)
        ? Text("VersionNotProvided", revision) : Localization.Format("Mcp/VersionFormat", version);
    public string ActionName(string action, string? name, int revision) =>
        Localization.Format("Mcp/ActionNameFormat", Text(action, revision), name ?? string.Empty);
    public Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public bool CanEditSubmit(bool canApprove, bool allowControl) => canApprove && allowControl;

    public ImageSource Logo(string? path)
    {
        // Client metadata never supplies network/file image URLs to XAML. These assets are bundled with Mizon.
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.Scheme != "ms-appx"
            || uri.Host.Length != 0 || !uri.AbsolutePath.StartsWith("/Assets/", StringComparison.Ordinal))
            uri = new Uri("ms-appx:///Assets/AppIcon/Square150x150Logo.png");
        return uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            ? new SvgImageSource(uri) { RasterizePixelWidth = 64, RasterizePixelHeight = 64 }
            : new BitmapImage(uri) { DecodePixelWidth = 64 };
    }

    private void Card_Loaded(object sender, RoutedEventArgs args) => Bindings.Update();
    private void Card_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        Bindings.StopTracking();
        // The connection belongs to the service and survives closing this view; decoded logos do not.
        LogoImage.Source = null;
    }

    private void LogoImage_Opened(object sender, RoutedEventArgs args) => FallbackIcon.Visibility = Visibility.Collapsed;
    private void LogoImage_Failed(object sender, ExceptionRoutedEventArgs args) => FallbackIcon.Visibility = Visibility.Visible;

    private async void Approve_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel is { CanApprove: true } item) await McpAccessService.Current.ApproveAsync(item);
    }
    private void Deny_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel is { CanApprove: true } item) McpAccessService.Current.Deny(item);
    }
    private async void Revoke_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel is { CanRevoke: true } item) await McpAccessService.Current.RevokeAsync(item);
    }
}
