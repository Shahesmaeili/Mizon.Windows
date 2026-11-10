using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Controls;
using Mizon.Windows.UserControls;
using Mizon.Windows.ViewModels.Items;
using System;
using WinRT;

namespace Mizon.Windows.Common;

/// <summary>
/// An anchored profile preview reusable by pages, lists and menus. Create it lazily
/// for the owner, then dispose it when that owner unloads. It never loads data or navigates.
/// </summary>
public sealed class ProfilePreviewPopup : IDisposable
{
    private readonly ProfilePreviewCard _card = new();
    private readonly MenuFlyout _flyout;
    private readonly MenuContentHost _contentHost;
    private readonly MenuFlyoutMotion _motion;
    private FrameworkElement? _anchor;
    private XamlRoot? _root;
    private FrameworkElement? _pendingAnchor;
    private ProfilePreviewViewModel? _pendingProfile;
    private bool _open;
    private bool _closing;
    private bool _disposed;
    private bool _reopenQueued;
    private int _reopenRevision;

    public event EventHandler<ProfilePreviewActionEventArgs>? ActionRequested;
    public event EventHandler? Closed;

    public ProfilePreviewPopup()
    {
        _contentHost = new MenuContentHost
        {
            Tag = _card,
            IsTabStop = false,
            AllowFocusOnInteraction = false,
            Padding = new Thickness(0),
            MinWidth = 0,
            Template = _card.Resources["ProfilePreviewContentTemplate"].As<ControlTemplate>()
        };
        AutomationProperties.SetAccessibilityView(_contentHost, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _flyout = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = _card.Resources["ProfilePreviewPresenterStyle"].As<Style>(),
            SystemBackdrop = new DesktopAcrylicBackdrop(),
            // This SDK rejects Auto for desktop Flyouts. A preferred side still
            // lets the native presenter move the preview inside the work area.
            Placement = FlyoutPlacementMode.Right,
            AreOpenCloseAnimationsEnabled = true
        };
        _flyout.Items.Add(_contentHost);
        // Reuse the account menu's actual material host, not a transparent
        // Flyout surface with an independent window-level backdrop.
        _motion = new MenuFlyoutMotion(_flyout, _contentHost);
        _flyout.Opened += FlyoutOpened;
        _flyout.Closing += FlyoutClosing;
        _flyout.Closed += FlyoutClosed;
        _card.ActionRequested += CardActionRequested;
        _card.DismissRequested += CardDismissRequested;
    }

    /// <summary>
    /// Shows caller-provided public profile data beside a loaded visual. Empty
    /// action labels hide those actions; handle <see cref="ActionRequested"/> to
    /// navigate or run a command. Nothing is downloaded or retained after closing.
    /// Call on the anchor's UI thread and dispose this instance with its owner.
    /// </summary>
    public void ShowAt(FrameworkElement anchor, ProfilePreviewViewModel profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(profile);
        if (!anchor.IsLoaded || anchor.XamlRoot is null) return;
        if (_reopenQueued)
        {
            _pendingAnchor = anchor;
            _pendingProfile = profile;
            return;
        }
        if (_open && !_closing && ReferenceEquals(_anchor, anchor))
        {
            _card.SetProfile(profile);
            UpdateBounds();
            AutomationProperties.SetName(_flyout, $"پروفایل {profile.DisplayName}");
            return;
        }
        if (_open)
        {
            // Wait for the previous native close animation before reusing its presenter.
            _pendingAnchor = anchor;
            _pendingProfile = profile;
            _flyout.Hide();
            return;
        }
        Open(anchor, profile);
    }

    private void Open(FrameworkElement anchor, ProfilePreviewViewModel profile)
    {
        _anchor = anchor;
        _root = anchor.XamlRoot;
        anchor.Unloaded += AnchorUnloaded;
        anchor.ActualThemeChanged += AnchorThemeChanged;
        _root.Changed += RootChanged;
        _card.RequestedTheme = anchor.ActualTheme;
        _card.SetProfile(profile);
        UpdateBounds();
        AutomationProperties.SetName(_flyout, $"پروفایل {profile.DisplayName}");
        _open = true;
        _closing = false;
        try
        {
            _motion.PrepareOpen();
            _flyout.ShowAt(anchor, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Standard });
        }
        catch
        {
            _open = false;
            ReleaseContent();
            throw;
        }
    }

    public void Hide()
    {
        _pendingAnchor = null;
        _pendingProfile = null;
        if (_reopenQueued)
        {
            _reopenQueued = false;
            _reopenRevision++;
            Closed?.Invoke(this, EventArgs.Empty);
        }
        if (!_disposed && _open) _flyout.Hide();
    }

    private void UpdateBounds()
    {
        if (_root is null) return;
        _card.Width = Math.Clamp(_root.Size.Width - 24, 1, 320);
        _card.MaxHeight = Math.Max(1, _root.Size.Height - 24);
    }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (!sender.IsHostVisible) Hide();
        else UpdateBounds();
    }
    private void AnchorThemeChanged(FrameworkElement sender, object args) => _card.RequestedTheme = sender.ActualTheme;
    private void AnchorUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { IsLoaded: false }) Hide();
    }

    private void FlyoutClosing(FlyoutBase sender, FlyoutBaseClosingEventArgs args) => _closing = true;
    private void FlyoutOpened(object? sender, object args) => _card.FocusFirstAction();

    private void FlyoutClosed(object? sender, object args)
    {
        _open = false;
        _closing = false;
        ReleaseContent();
        if (!_disposed && _pendingAnchor is { IsLoaded: true, XamlRoot: not null } anchor && _pendingProfile is not null)
        {
            // Let the old presenter unload its material before attaching it again.
            // Rapid selections replace the pending snapshot; Hide cancels the queue.
            _reopenQueued = true;
            var revision = ++_reopenRevision;
            if (anchor.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => OpenPending(revision))) return;
            _reopenQueued = false;
        }
        _pendingAnchor = null;
        _pendingProfile = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OpenPending(int revision)
    {
        if (_disposed || !_reopenQueued || revision != _reopenRevision) return;
        _reopenQueued = false;
        var anchor = _pendingAnchor;
        var profile = _pendingProfile;
        _pendingAnchor = null;
        _pendingProfile = null;
        if (anchor is { IsLoaded: true, XamlRoot: not null } && profile is not null) Open(anchor, profile);
        else Closed?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseContent()
    {
        if (_anchor is not null)
        {
            _anchor.Unloaded -= AnchorUnloaded;
            _anchor.ActualThemeChanged -= AnchorThemeChanged;
        }
        if (_root is not null) _root.Changed -= RootChanged;
        _anchor = null;
        _root = null;
        _card.SetProfile(null);
    }

    private void CardDismissRequested(object? sender, EventArgs args) => Hide();

    private void CardActionRequested(object? sender, ProfilePreviewActionEventArgs args)
    {
        if (_disposed || !_open || _closing) return;
        _closing = true;
        // Give the owner the action while its contextual data is still available;
        // native Hide can synchronously raise Closed (for example with animations off).
        try { ActionRequested?.Invoke(this, args); }
        finally { Hide(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        var wasOpen = _open || _reopenQueued;
        _disposed = true;
        _reopenQueued = false;
        _reopenRevision++;
        _pendingAnchor = null;
        _pendingProfile = null;
        _flyout.Opened -= FlyoutOpened;
        _flyout.Closing -= FlyoutClosing;
        _flyout.Closed -= FlyoutClosed;
        _card.ActionRequested -= CardActionRequested;
        _card.DismissRequested -= CardDismissRequested;
        _motion.Dispose();
        _flyout.AreOpenCloseAnimationsEnabled = false;
        if (_open) _flyout.Hide();
        _open = false;
        ReleaseContent();
        _contentHost.Tag = null;
        _contentHost.Dispose();
        _flyout.Items.Clear();
        _flyout.SystemBackdrop = null;
        if (wasOpen) Closed?.Invoke(this, EventArgs.Empty);
        ActionRequested = null;
        Closed = null;
    }
}
