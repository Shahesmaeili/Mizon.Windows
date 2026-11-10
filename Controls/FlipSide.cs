// Adapted from DevWinUI FlipSide, Copyright (c) 2023 Mahdi Hosseini (MIT).
// https://github.com/ghost1372/DevWinUI/blob/main/dev/DevWinUI/Controls/Native/FlipSide/FlipSide.cs
// See FlipSide.LICENSE.txt. Keeps its spring, Y axis, face visibility and perspective.
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

public sealed partial class FlipSide : UserControl
{
    public static readonly DependencyProperty Side1Property = DependencyProperty.Register(
        nameof(Side1), typeof(object), typeof(FlipSide), new PropertyMetadata(null, ContentChanged));
    public static readonly DependencyProperty Side2Property = DependencyProperty.Register(
        nameof(Side2), typeof(object), typeof(FlipSide), new PropertyMetadata(null, ContentChanged));
    public static readonly DependencyProperty IsFlippedProperty = DependencyProperty.Register(
        nameof(IsFlipped), typeof(bool), typeof(FlipSide), new PropertyMetadata(false, FlippedChanged));

    public object? Side1 { get => GetValue(Side1Property); set => SetValue(Side1Property, value); }
    public object? Side2 { get => GetValue(Side2Property); set => SetValue(Side2Property, value); }
    public bool IsFlipped { get => (bool)GetValue(IsFlippedProperty); set => SetValue(IsFlippedProperty, value); }

    private readonly UISettings _settings = new();
    private readonly ContentControl _front = CreateFace();
    private readonly ContentControl _back = CreateFace();
    private readonly FacePanel _surface;
    private readonly Visual _frontVisual;
    private readonly Visual _backVisual;
    private CompositionScopedBatch? _batch;
    private TypedEventHandler<object, CompositionBatchCompletedEventArgs>? _batchCompleted;
    private TaskCompletionSource? _completion;
    private bool _loaded;

    public FlipSide()
    {
        FlowDirection = FlowDirection.LeftToRight;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = false;
        _surface = new FacePanel();
        _surface.Children.Add(_front);
        _surface.Children.Add(_back);
        Content = _surface;
        _frontVisual = ElementCompositionPreview.GetElementVisual(_front);
        _backVisual = ElementCompositionPreview.GetElementVisual(_back);
        _surface.SizeChanged += Surface_SizeChanged;
        _front.SizeChanged += Face_SizeChanged;
        _back.SizeChanged += Face_SizeChanged;
        Loaded += Control_Loaded;
        Unloaded += Control_Unloaded;
        Settle();
    }

    private static ContentControl CreateFace() => new()
    {
        IsTabStop = false,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
        FlowDirection = FlowDirection.LeftToRight
    };

    private static void ContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (FlipSide)sender;
        control._front.Content = control.Side1;
        control._back.Content = control.Side2;
    }

    private static void FlippedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((FlipSide)sender).StartFlip();

    public async Task FlipAsync(bool flipped, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsFlipped = flipped;
        var completion = _completion;
        var pending = completion?.Task;
        if (pending is null) return;
        try { await pending.WaitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            // A superseded waiter must never stop the newer spring.
            if (ReferenceEquals(_completion, completion)) CancelAnimation();
            throw;
        }
    }

    private void StartFlip()
    {
        // A resize or unload can cancel a spring without leaving an edge-on card behind.
        StopBatch();
        _surface.ShowBack = IsFlipped;
        if (!_loaded || !_settings.AnimationsEnabled)
        {
            Settle();
            return;
        }

        _front.Visibility = _back.Visibility = Visibility.Visible;
        _front.IsEnabled = _back.IsEnabled = true;
        // The arriving face accepts focus while the spring is still settling.
        _front.IsHitTestVisible = !IsFlipped;
        _back.IsHitTestVisible = IsFlipped;
        AutomationProperties.SetAccessibilityView(_front, IsFlipped ? AccessibilityView.Raw : AccessibilityView.Content);
        AutomationProperties.SetAccessibilityView(_back, IsFlipped ? AccessibilityView.Content : AccessibilityView.Raw);
        _surface.InvalidateMeasure();
        _surface.UpdateLayout();
        UpdateGeometry();
        var compositor = _frontVisual.Compositor;
        // The back starts at 180 and ends at 360; it is never displayed mirror-written.
        using var frontOpacity = compositor.CreateExpressionAnimation("this.Target.RotationAngleInDegrees > 90 ? 0f : 1f");
        using var backOpacity = compositor.CreateExpressionAnimation("(this.Target.RotationAngleInDegrees - 180) > 90 ? 1f : 0f");
        _frontVisual.StartAnimation("Opacity", frontOpacity);
        _backVisual.StartAnimation("Opacity", backOpacity);
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var batch = _batch;
        _batchCompleted = (_, args) => Flip_Completed(batch, args);
        _batch.Completed += _batchCompleted;
        using var frontSpring = compositor.CreateSpringScalarAnimation();
        using var backSpring = compositor.CreateSpringScalarAnimation();
        frontSpring.DampingRatio = backSpring.DampingRatio = 0.5f;
        frontSpring.Period = backSpring.Period = TimeSpan.FromMilliseconds(200);
        frontSpring.FinalValue = IsFlipped ? 180f : 0f;
        backSpring.FinalValue = IsFlipped ? 360f : 180f;
        _frontVisual.StartAnimation("RotationAngleInDegrees", frontSpring);
        _backVisual.StartAnimation("RotationAngleInDegrees", backSpring);
        _batch.End();
    }

    private void Flip_Completed(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (!ReferenceEquals(sender, _batch)) return;
        var completion = _completion;
        _completion = null;
        DisposeBatch();
        // Let Composition retain the spring's final value, just as DevWinUI does.
        // Stopping it or resetting angles here can expose the old base value for a frame.
        _front.IsHitTestVisible = !IsFlipped;
        _back.IsHitTestVisible = IsFlipped;
        _front.IsEnabled = !IsFlipped;
        _back.IsEnabled = IsFlipped;
        AutomationProperties.SetAccessibilityView(_front, IsFlipped ? AccessibilityView.Raw : AccessibilityView.Content);
        AutomationProperties.SetAccessibilityView(_back, IsFlipped ? AccessibilityView.Content : AccessibilityView.Raw);
        completion?.TrySetResult();
    }

    public void CancelAnimation()
    {
        StopBatch();
        Settle();
    }

    private void StopBatch()
    {
        DisposeBatch();
        _completion?.TrySetCanceled();
        _completion = null;
    }

    private void DisposeBatch()
    {
        if (_batch is null) return;
        if (_batchCompleted is not null) _batch.Completed -= _batchCompleted;
        _batch.Dispose();
        _batch = null;
        _batchCompleted = null;
    }

    private void Settle()
    {
        _frontVisual.StopAnimation("RotationAngleInDegrees");
        _backVisual.StopAnimation("RotationAngleInDegrees");
        _frontVisual.StopAnimation("Opacity");
        _backVisual.StopAnimation("Opacity");
        _frontVisual.RotationAngleInDegrees = IsFlipped ? 180 : 0;
        _backVisual.RotationAngleInDegrees = IsFlipped ? 360 : 180;
        _frontVisual.Opacity = IsFlipped ? 0 : 1;
        _backVisual.Opacity = IsFlipped ? 1 : 0;
        _front.Visibility = IsFlipped ? Visibility.Collapsed : Visibility.Visible;
        _back.Visibility = IsFlipped ? Visibility.Visible : Visibility.Collapsed;
        _front.IsHitTestVisible = !IsFlipped;
        _back.IsHitTestVisible = IsFlipped;
        _front.IsEnabled = !IsFlipped;
        _back.IsEnabled = IsFlipped;
        AutomationProperties.SetAccessibilityView(_front, IsFlipped ? AccessibilityView.Raw : AccessibilityView.Content);
        AutomationProperties.SetAccessibilityView(_back, IsFlipped ? AccessibilityView.Content : AccessibilityView.Raw);
        _surface.ShowBack = IsFlipped;
        _surface.InvalidateMeasure();
    }

    private void Control_Loaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        _settings.AnimationsEnabledChanged += AnimationsEnabledChanged;
        Settle();
        UpdateGeometry();
    }

    private void Control_Unloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        _settings.AnimationsEnabledChanged -= AnimationsEnabledChanged;
        CancelAnimation();
        ElementCompositionPreview.GetElementVisual(_surface).TransformMatrix = Matrix4x4.Identity;
    }

    private void AnimationsEnabledChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_loaded && !_settings.AnimationsEnabled) CancelAnimation();
    });

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateGeometry();
    private void Face_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var face = (FrameworkElement)sender;
        var visual = ElementCompositionPreview.GetElementVisual(face);
        visual.CenterPoint = new Vector3((float)face.ActualWidth / 2, (float)face.ActualHeight / 2, 0);
        visual.RotationAxis = Vector3.UnitY;
    }

    private void UpdateGeometry()
    {
        var width = (float)_surface.ActualWidth;
        var height = (float)_surface.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var perspective = Matrix4x4.Identity;
        perspective.M34 = -1f / width;
        ElementCompositionPreview.GetElementVisual(_surface).TransformMatrix =
            Matrix4x4.CreateTranslation(-width / 2, -height / 2, 0) * perspective *
            Matrix4x4.CreateTranslation(width / 2, height / 2, 0);
        Face_SizeChanged(_front, null!);
        Face_SizeChanged(_back, null!);
    }

    // The hidden, longer registration face must not enlarge the login form's scroll extent.
    private sealed partial class FacePanel : Panel
    {
        internal bool ShowBack;
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            return Children[ShowBack ? 1 : 0].DesiredSize;
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in Children)
            {
                var height = child.DesiredSize.Height;
                child.Arrange(new Rect(0, (finalSize.Height - height) / 2, finalSize.Width, height));
            }
            return finalSize;
        }
    }
}
