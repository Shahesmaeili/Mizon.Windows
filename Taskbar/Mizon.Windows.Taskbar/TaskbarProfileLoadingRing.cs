using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Mizon.Windows.Controls;
using System.Numerics;

namespace Mizon.Windows.Taskbar;

// A stationary sibling of the animated photo. Native ProgressRing owns its
// animation clock; the presence cutout remains genuinely transparent.
internal sealed partial class TaskbarProfileLoadingRing : Grid, IDisposable
{
    private readonly ProgressRing _ring = new() { Width = 36, Height = 36, IsActive = false, IsHitTestVisible = false };
    private CompositionPathGeometry? _path;
    private CompositionGeometricClip? _clip;
    private CanvasGeometry? _cutout;
    private bool _busy, _hostVisible, _disposed;

    internal TaskbarProfileLoadingRing()
    {
        Width = Height = 36;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        Children.Add(_ring);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(_ring, AccessibilityView.Raw);
        Loaded += Ring_Loaded;
        Unloaded += Ring_Unloaded;
    }

    internal void Update(bool busy, bool hostVisible)
    {
        if (_disposed) return;
        _busy = busy;
        _hostVisible = hostVisible;
        _ring.IsActive = busy && hostVisible && IsLoaded;
        Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Ring_Loaded(object sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        if (_clip is null)
        {
            var metrics = PresenceAvatarMetrics.ForSize(32);
            var device = CanvasDevice.GetSharedDevice();
            using var area = CanvasGeometry.CreateRectangle(device, 0, 0, 36, 36);
            using var gap = CanvasGeometry.CreateCircle(device, (float)(2 + metrics.BadgeCenter),
                (float)(2 + metrics.BadgeCenter), (float)metrics.CutoutRadius);
            _cutout = area.CombineWith(gap, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
            var visual = ElementCompositionPreview.GetElementVisual(_ring);
            _path = visual.Compositor.CreatePathGeometry(new CompositionPath(_cutout));
            _clip = visual.Compositor.CreateGeometricClip(_path);
            visual.Clip = _clip;
        }
        _ring.IsActive = _busy && _hostVisible;
    }

    private void Ring_Unloaded(object sender, RoutedEventArgs args)
    {
        _ring.IsActive = false;
        ReleaseClip();
    }

    private void ReleaseClip()
    {
        if (_clip is not null) ElementCompositionPreview.GetElementVisual(_ring).Clip = null;
        _clip?.Dispose(); _clip = null;
        _path?.Dispose(); _path = null;
        _cutout?.Dispose(); _cutout = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loaded -= Ring_Loaded;
        Unloaded -= Ring_Unloaded;
        _ring.IsActive = false;
        ReleaseClip();
        Children.Clear();
    }
}
