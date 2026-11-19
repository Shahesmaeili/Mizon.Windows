using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.Numerics;

namespace Mizon.Windows.UserControls;

public sealed partial class MusicTransportControl : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(MusicPageViewModel), typeof(MusicTransportControl), new PropertyMetadata(null));

    public MusicPageViewModel? Model
    {
        get => (MusicPageViewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public LocalizationService Localization => LocalizationService.Current;
    private CompositionGeometricClip? _clip;
    private CompositionPathGeometry? _path;
    private CanvasGeometry? _notchedSurface;

    public MusicTransportControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private string PlaybackLabel(bool playing, int revision) =>
        Localization.GetString(playing ? "MusicDetails/Pause" : "MusicDetails/Play", revision);

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_clip is not null) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(PlaybackButton);
            var device = CanvasDevice.GetSharedDevice();
            // Match the fixed control template so resize never recreates the clip.
            using var face = CanvasGeometry.CreateRectangle(device, 0, 0, 60, 60);
            using var notch = CanvasGeometry.CreateCircle(device, 51, 57, 18);
            _notchedSurface = face.CombineWith(notch, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
            _path = visual.Compositor.CreatePathGeometry();
            _path.Path = new CompositionPath(_notchedSurface);
            var clip = visual.Compositor.CreateGeometricClip(_path);
            try { visual.Clip = clip; }
            catch
            {
                clip.Dispose();
                throw;
            }
            _clip = clip;
        }
        catch (Exception error)
        {
            ReleaseNotch();
            AppLog.Error("The music transport notch could not be rendered.", error);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleaseNotch();

    private void ReleaseNotch()
    {
        if (_clip is not null)
        {
            ElementCompositionPreview.GetElementVisual(PlaybackButton).Clip = null;
            _clip.Dispose();
        }
        _path?.Dispose();
        _notchedSurface?.Dispose();
        _clip = null;
        _path = null;
        _notchedSurface = null;
    }
}
