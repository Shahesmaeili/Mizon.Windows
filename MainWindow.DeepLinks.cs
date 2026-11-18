using Microsoft.UI.Xaml;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System;
using System.Threading.Tasks;

namespace Mizon.Windows;

public sealed partial class MainWindow
{
    private AppDeepLinkResponseSession? _trackedDeepLink;

    internal void TrackDeepLinkResponse(AppDeepLinkResponseSession response)
    {
        if (response.IsCompleted || ReferenceEquals(_trackedDeepLink, response)) return;
        _trackedDeepLink = response;
        _ = ObserveDeepLinkResponseAsync(response);
    }

    private async Task ObserveDeepLinkResponseAsync(AppDeepLinkResponseSession response)
    {
        // Keep cached destinations from flashing a ring. A visible ring belongs to
        // the latest request, even while the older operation is unwinding.
        var completion = response.Completion;
        if (!completion.IsCompleted && DeepLinkStatus.Visibility != Visibility.Visible)
            await Task.WhenAny(completion, Task.Delay(TimeSpan.FromMilliseconds(150)));

        if (!ReferenceEquals(_trackedDeepLink, response)) return;
        if (!response.IsCompleted && DeepLinkStatus.Visibility != Visibility.Visible)
        {
            UpdateDeepLinkStatusLanguage();
            SetDeepLinkStatusVisible(true);
            AccessibilityAnnouncements.Announce(DeepLinkStatus, DeepLinkStatusLabel.Text, "DeepLinkOpening");
        }

        await completion;
        if (!ReferenceEquals(_trackedDeepLink, response)) return;
        _trackedDeepLink = null;
        SetDeepLinkStatusVisible(false);
    }

    private void SetDeepLinkStatusVisible(bool visible)
    {
        DeepLinkRing.IsActive = visible;
        DeepLinkStatus.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDeepLinkStatusLanguage()
    {
        var localization = LocalizationService.Current;
        DeepLinkStatus.FlowDirection = localization.FlowDirection;
        DeepLinkStatusLabel.FontFamily = localization.TextFontFamily;
        DeepLinkStatusLabel.Text = localization.GetString("Window/DeepLinkOpening");
    }
}
