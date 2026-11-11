using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Numerics;
using Windows.UI;

namespace Mizon.Windows.Controls;

/// <summary>A circular avatar with a genuine transparent cutout around its status badge.</summary>
public sealed partial class PresenceAvatar : Grid
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(PresenceAvatar), new PropertyMetadata(null, PresentationChanged));
    public static readonly DependencyProperty IsOnlineProperty = DependencyProperty.Register(
        nameof(IsOnline), typeof(bool), typeof(PresenceAvatar), new PropertyMetadata(false, PresentationChanged));
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText), typeof(string), typeof(PresenceAvatar), new PropertyMetadata(string.Empty, PresentationChanged));
    public static readonly DependencyProperty DisplayNameProperty = DependencyProperty.Register(
        nameof(DisplayName), typeof(string), typeof(PresenceAvatar), new PropertyMetadata(string.Empty, PresentationChanged));

    private readonly PersonPicture _picture = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas _badgeHost = new() { IsHitTestVisible = false };
    private readonly Ellipse _badge = new() { IsHitTestVisible = false };
    private readonly SolidColorBrush _online = new(Color.FromArgb(255, 48, 201, 127));
    private readonly SolidColorBrush _offline = new(Color.FromArgb(255, 141, 150, 168));
    private CompositionPathGeometry? _path;
    private CompositionGeometricClip? _clip;
    private CanvasGeometry? _cutout;
    private double _size;
    private bool _loaded;

    public PresenceAvatar()
    {
        // A parent's RTL reading order must not mirror the physical badge corner.
        FlowDirection = FlowDirection.LeftToRight;
        IsHitTestVisible = false;
        _badgeHost.Children.Add(_badge);
        Children.Add(_picture);
        Children.Add(_badgeHost);
        AutomationProperties.SetAccessibilityView(_picture, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(_badgeHost, AccessibilityView.Raw);
        Loaded += Avatar_Loaded;
        Unloaded += Avatar_Unloaded;
        SizeChanged += Avatar_SizeChanged;
        UpdatePresentation();
    }

    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public bool IsOnline { get => (bool)GetValue(IsOnlineProperty); set => SetValue(IsOnlineProperty, value); }
    public string StatusText { get => (string?)GetValue(StatusTextProperty) ?? string.Empty; set => SetValue(StatusTextProperty, value); }
    public string DisplayName { get => (string?)GetValue(DisplayNameProperty) ?? string.Empty; set => SetValue(DisplayNameProperty, value); }

    private static void PresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((PresenceAvatar)sender).UpdatePresentation();
    private void UpdatePresentation()
    {
        if (_picture is null) return;
        if (!ReferenceEquals(_picture.ProfilePicture, Source)) _picture.ProfilePicture = Source;
        _picture.DisplayName = DisplayName;
        _badge.Fill = IsOnline ? _online : _offline;
        AutomationProperties.SetName(this, string.IsNullOrWhiteSpace(DisplayName) ? StatusText
            : string.IsNullOrWhiteSpace(StatusText) ? DisplayName : DisplayName + ", " + StatusText);
        AutomationProperties.SetItemStatus(this, StatusText);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PresenceAvatarPeer(this);
    private sealed partial class PresenceAvatarPeer(PresenceAvatar owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(PresenceAvatar);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
    }

    private void Avatar_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded || !IsLoaded) return;
        _loaded = true;
        UpdateGeometry();
    }
    private void Avatar_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        _loaded = false;
        ReleaseClip();
    }
    private void Avatar_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateGeometry();

    private void UpdateGeometry()
    {
        if (!_loaded) return;
        var metrics = PresenceAvatarMetrics.ForSize(Math.Min(ActualWidth, ActualHeight));
        if (metrics.Size <= 0) { ReleaseClip(); return; }
        var left = (ActualWidth - metrics.Size) / 2;
        var top = (ActualHeight - metrics.Size) / 2;
        _picture.Width = _picture.Height = metrics.Size;
        _badge.Width = _badge.Height = metrics.BadgeDiameter;
        Canvas.SetLeft(_badge, left + metrics.BadgeCenter - metrics.BadgeDiameter / 2);
        Canvas.SetTop(_badge, top + metrics.BadgeCenter - metrics.BadgeDiameter / 2);
        if (_clip is not null && _size == metrics.Size) return;

        var visual = ElementCompositionPreview.GetElementVisual(_picture);
        var compositor = visual.Compositor;
        using var circle = CanvasGeometry.CreateCircle(CanvasDevice.GetSharedDevice(),
            (float)(metrics.Size / 2), (float)(metrics.Size / 2), (float)(metrics.Size / 2));
        using var gap = CanvasGeometry.CreateCircle(CanvasDevice.GetSharedDevice(),
            (float)metrics.BadgeCenter, (float)metrics.BadgeCenter, (float)metrics.CutoutRadius);
        var cutout = circle.CombineWith(gap, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
        var path = compositor.CreatePathGeometry(new CompositionPath(cutout));
        var clip = compositor.CreateGeometricClip(path);
        ReleaseClip();
        _cutout = cutout;
        _path = path;
        _clip = clip;
        _size = metrics.Size;
        visual.Clip = clip;
    }

    private void ReleaseClip()
    {
        if (_clip is null) return;
        ElementCompositionPreview.GetElementVisual(_picture).Clip = null;
        _clip.Dispose(); _clip = null;
        _path?.Dispose(); _path = null;
        _cutout?.Dispose(); _cutout = null;
        _size = 0;
    }
}
