using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;

namespace Mizon.Windows.UserControls;

public sealed partial class MusicDetailsShowcase : UserControl
{
    public static readonly DependencyProperty IsTrendLoadedProperty = DependencyProperty.Register(
        nameof(IsTrendLoaded), typeof(bool), typeof(MusicDetailsShowcase), new PropertyMetadata(false));

    // The non-generic XAML series reads back an IReadOnlyCollection<object>.
    private static readonly IReadOnlyList<object> Samples = Array.AsReadOnly<object>([2d, 1d, 3d, 4d, 3d, 4d, 6d]);
    private bool _isTrendInViewport;
    private bool _trendQueued;
    private int _attachmentVersion;

    public MusicDetailsShowcase() => InitializeComponent();

    public LocalizationService Localization => LocalizationService.Current;
    public IReadOnlyList<object> TrendValues => Samples;
    public Func<double, string> EmptyAxisLabel { get; } = static _ => string.Empty;

    public bool IsTrendLoaded
    {
        get => (bool)GetValue(IsTrendLoadedProperty);
        private set => SetValue(IsTrendLoadedProperty, value);
    }

    private string Text(string key, int revision) => Localization.GetString("MusicDetails/" + key, revision);

    private void TrendHost_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var viewport = args.EffectiveViewport;
        _isTrendInViewport = viewport.Width > 0 && viewport.Height > 0
            && viewport.Right > 0 && viewport.Bottom > 0
            && viewport.Left < sender.ActualWidth && viewport.Top < sender.ActualHeight;
        QueueTrendLoad();
    }

    private void Showcase_Loaded(object sender, RoutedEventArgs e) => QueueTrendLoad();

    private void QueueTrendLoad()
    {
        if (!IsLoaded || !_isTrendInViewport || IsTrendLoaded || _trendQueued) return;

        var attachment = _attachmentVersion;
        _trendQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (attachment != _attachmentVersion) return;
            _trendQueued = false;
            if (IsLoaded && _isTrendInViewport && !IsTrendLoaded)
                IsTrendLoaded = true;
        }))
            _trendQueued = false;
    }

    private void Showcase_Unloaded(object sender, RoutedEventArgs e)
    {
        var attachment = ++_attachmentVersion;
        _trendQueued = false;
        _isTrendInViewport = false;
        if (!IsTrendLoaded) return;

        // Let WinUI and the chart finish detaching before x:Load removes the chart subtree.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (attachment == _attachmentVersion && !IsLoaded)
                IsTrendLoaded = false;
        });
    }
}
