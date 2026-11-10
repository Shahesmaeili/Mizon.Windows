using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Mizon.Windows.Common;

internal static class AccessibilityAnnouncements
{
    internal static void Announce(FrameworkElement source, string message, string activity, bool important = false)
    {
        if (!source.IsLoaded || string.IsNullOrWhiteSpace(message)) return;
        // UI Automation is enabled for Narrator and other screen readers, not only Narrator.exe.
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        var peer = FrameworkElementAutomationPeer.FromElement(source) ?? FrameworkElementAutomationPeer.CreatePeerForElement(source);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other,
            important ? AutomationNotificationProcessing.ImportantMostRecent : AutomationNotificationProcessing.MostRecent,
            message, activity);
    }
}
