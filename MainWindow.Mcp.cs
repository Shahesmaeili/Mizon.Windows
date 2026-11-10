using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Services;
using Mizon.Windows.UserControls;
using System;
using System.ComponentModel;

namespace Mizon.Windows;

public sealed partial class MainWindow
{
    private ContentDialog? _mcpAccessDialog;
    private bool _mcpAccessClosed;
    private int _mcpPendingCount;

    private void InitializeMcpAccess()
    {
        McpAccessService.Current.PropertyChanged += McpAccessChanged;
        LocalizationService.Current.PropertyChanged += McpLocalizationChanged;
        UpdateMcpNotice();
    }

    private void McpAccessChanged(object? sender, PropertyChangedEventArgs args) => UpdateMcpNotice();
    private void McpLocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.Revision)) UpdateMcpNotice();
    }

    private void UpdateMcpNotice()
    {
        if (_mcpAccessClosed) return;
        var access = McpAccessService.Current;
        var localization = LocalizationService.Current;
        McpAccessNotice.Visibility = access.HasConnections ? Visibility.Visible : Visibility.Collapsed;
        McpAccessNotice.FlowDirection = localization.FlowDirection;
        McpManageButton.FontFamily = McpPendingInfo.FontFamily = localization.TextFontFamily;
        McpManageLabel.Text = localization.GetString("Mcp/ManageConnections");
        AutomationProperties.SetName(McpManageButton, McpManageLabel.Text);
        AutomationProperties.SetHelpText(McpManageButton, localization.GetString("Mcp/AccessRequestedMessage"));
        McpPendingInfo.Title = localization.GetString("Mcp/AccessRequestedTitle");
        McpPendingInfo.Message = localization.GetString("Mcp/AccessRequestedMessage");
        if (access.PendingCount > _mcpPendingCount) McpPendingInfo.IsOpen = true;
        if (!access.HasPendingRequests) McpPendingInfo.IsOpen = false;
        _mcpPendingCount = access.PendingCount;
        if (_mcpAccessDialog is { } dialog)
        {
            dialog.Title = McpManageLabel.Text;
            dialog.CloseButtonText = localization.GetString("Mcp/Close");
            dialog.FlowDirection = localization.FlowDirection;
            dialog.FontFamily = localization.TextFontFamily;
        }
    }

    private async void ManageMcpAccess_Click(object sender, RoutedEventArgs args)
    {
        if (_mcpAccessDialog is not null || _mcpAccessClosed || RootGrid.XamlRoot is null) return;
        var panel = new McpAccessPanel();
        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(1, RootGrid.ActualHeight - 180)
        };
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Content = scroll,
            DefaultButton = ContentDialogButton.Close,
            FullSizeDesired = true
        };
        _mcpAccessDialog = dialog;
        UpdateMcpNotice();
        void ResizeDialogContent(object sizeSender, SizeChangedEventArgs sizeArgs) =>
            scroll.MaxHeight = Math.Max(1, sizeArgs.NewSize.Height - 180);
        RootGrid.SizeChanged += ResizeDialogContent;
        try { await dialog.ShowAsync(); }
        catch (Exception error)
        {
            // Another app-owned modal may already be open. Keep the request pending.
            AppLog.Information($"MCP access panel could not open ({error.GetType().Name}).");
        }
        finally
        {
            RootGrid.SizeChanged -= ResizeDialogContent;
            scroll.Content = null;
            dialog.Content = null;
            _mcpAccessDialog = null;
        }
    }

    private void DisposeMcpAccess()
    {
        _mcpAccessClosed = true;
        McpAccessService.Current.PropertyChanged -= McpAccessChanged;
        LocalizationService.Current.PropertyChanged -= McpLocalizationChanged;
        _mcpAccessDialog?.Hide();
        _mcpAccessDialog = null;
    }
}
