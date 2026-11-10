using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Services;

namespace Mizon.Windows.UserControls;

/// <summary>The same user-owned consent surface is available in settings and before sign-in.</summary>
public sealed partial class McpAccessPanel : UserControl
{
    public McpAccessService Access => McpAccessService.Current;
    public LocalizationService Localization => LocalizationService.Current;

    public McpAccessPanel() => InitializeComponent();

    public string Text(string key, int revision) => Localization.GetString("Mcp/" + key, revision);
    public Visibility EmptyVisibility(bool hasConnections) => hasConnections ? Visibility.Collapsed : Visibility.Visible;

    private void Panel_Loaded(object sender, RoutedEventArgs args) => Bindings.Update();
    private void Panel_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded) Bindings.StopTracking();
    }

    // Consent is never exposed as an MCP operation. Only an interaction in this UI can grant access.
    private async void RevokeAll_Click(object sender, RoutedEventArgs args) => await Access.RevokeAllAsync();
}
