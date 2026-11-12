using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using Mizon.Windows.Controls;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class AuthShowcase : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(AuthShowcase), new PropertyMetadata(false, ActivityChanged));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private readonly UISettings _uiSettings = new();
    private readonly DispatcherTimer _rotationTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private CancellationTokenSource? _transitionCancellation;
    private bool _initialized;
    private bool _loaded;
    private bool _pointerInside;
    private bool _focusInside;
    private bool _transitioning;
    private int _sceneIndex;
    private int _transitionVersion;
    private int? _queuedScene;
    private Window? _hostWindow;

    private bool CanAnimate => _loaded && IsActive && Visibility == Visibility.Visible && _uiSettings.AnimationsEnabled;

    public AuthShowcase()
    {
        InitializeComponent();
        _initialized = true;
        Loaded += Showcase_Loaded;
        Unloaded += Showcase_Unloaded;
        PointerEntered += Showcase_PointerEntered;
        PointerExited += Showcase_PointerExited;
        GotFocus += Showcase_GotFocus;
        LostFocus += Showcase_LostFocus;
        _rotationTimer.Tick += RotationTimer_Tick;
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => ApplyActivity());
    }

    private static void ActivityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var showcase = (AuthShowcase)sender;
        if (showcase._initialized) showcase.ApplyActivity();
    }

    private void Showcase_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        Localization.PropertyChanged += Localization_PropertyChanged;
        Bindings.Update();
        _hostWindow = App.MainMizonWindow;
        if (_hostWindow is not null) _hostWindow.Closed += HostWindow_Closed;
        _uiSettings.AnimationsEnabledChanged += AnimationsEnabledChanged;

        ApplyActivity();
    }

    private void Showcase_Unloaded(object sender, RoutedEventArgs e)
        => Deactivate();

    private void HostWindow_Closed(object sender, WindowEventArgs e)
        => Deactivate();

    private void Deactivate()
    {
        if (!_loaded) return;
        _loaded = false;
        Localization.PropertyChanged -= Localization_PropertyChanged;
        Bindings.StopTracking();
        if (_hostWindow is not null) _hostWindow.Closed -= HostWindow_Closed;
        _hostWindow = null;
        _pointerInside = false;
        _focusInside = false;
        _uiSettings.AnimationsEnabledChanged -= AnimationsEnabledChanged;
        ApplyActivity();
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keep the selected scene, timer and in-flight composition animations.
        if (e.PropertyName == nameof(LocalizationService.Revision)) UpdateCaption();
    }

    private void AnimationsEnabledChanged(UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_loaded)
            {
        
                ApplyActivity();
            }
        });
    }

    private void ApplyActivity()
    {
        if (!_initialized) return;
        _rotationTimer.Stop();
        if (!_loaded || !IsActive || Visibility != Visibility.Visible)
        {
            _pointerInside = false;
            _focusInside = false;
        }
        _transitionVersion++;
        _transitionCancellation?.Cancel();
        _transitionCancellation = null;
        _transitioning = false;
        _queuedScene = null;
        StopSceneMotion();
        SettleScene();
        if (CanAnimate) StartSceneMotion();
        ScheduleRotation();
    }

    private void ScheduleRotation()
    {
        _rotationTimer.Stop();
        if (CanAnimate && !_pointerInside && !_focusInside && !_transitioning)
            _rotationTimer.Start();
    }

    private async void RotationTimer_Tick(object? sender, object e)
    {
        _rotationTimer.Stop();
        await ChangeSceneAsync((_sceneIndex + 1) % 3);
    }

    private void Scene_GamepadInvoked(object? sender, EventArgs args)
    {
        if (sender is AuthSceneSelector selector) Scene_Click(selector, new RoutedEventArgs());
    }

    private async void Scene_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && int.TryParse(tag, out var index))
            await ChangeSceneAsync(index);
    }

    private async Task ChangeSceneAsync(int index)
    {
        if (index < 0 || index > 2) return;
        if (_transitioning)
        {
            _queuedScene = index;
            UpdateSelection();
            return;
        }

        if (index == _sceneIndex)
        {
            UpdateSelection();
            ScheduleRotation();
            return;
        }

        _rotationTimer.Stop();
        var previousIndex = _sceneIndex;
        _sceneIndex = index;
        UpdateSelection();
        StopSceneMotion();
        if (!CanAnimate)
        {
            SettleScene();
            return;
        }

        _transitioning = true;
        var version = ++_transitionVersion;
        using var cancellation = new CancellationTokenSource();
        _transitionCancellation = cancellation;
        try
        {
            var previous = GetScene(previousIndex);
            var next = GetScene(index);
            next.Visibility = Visibility.Visible;
            var direction = index > previousIndex ? -1f : 1f;
            TransitionScene(previous, false, direction);
            TransitionScene(next, true, direction);
            FadeCaption(false);
            StartSceneMotion();
            await Task.Delay(150, cancellation.Token);
            UpdateCaption();
            FadeCaption(true);
            await Task.Delay(300, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("Unable to animate the authentication showcase.", exception);
        }
        finally
        {
            if (version == _transitionVersion)
            {
                _transitionCancellation = null;
                _transitioning = false;
                SettleScene();
            }
        }

        if (version != _transitionVersion) return;
        var queued = _queuedScene;
        _queuedScene = null;
        if (queued.HasValue && queued.Value != _sceneIndex)
            await ChangeSceneAsync(queued.Value);
        else
            ScheduleRotation();
    }

    private Grid GetScene(int index) => index switch { 1 => CinemaScene, 2 => VpnScene, _ => MusicScene };

    private void SettleScene()
    {
        for (var i = 0; i < 3; i++)
        {
            var scene = GetScene(i);
            var visual = GetVisual(scene, true);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.Properties.StopAnimation("Translation");
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            scene.Visibility = i == _sceneIndex ? Visibility.Visible : Visibility.Collapsed;
        }
        var caption = GetVisual(CaptionHost);
        caption.StopAnimation("Opacity");
        caption.Opacity = 1;
        UpdateSelection();
        UpdateCaption();
    }

    private void UpdateSelection()
    {
        MusicSelector.IsChecked = _sceneIndex == 0;
        CinemaSelector.IsChecked = _sceneIndex == 1;
        VpnSelector.IsChecked = _sceneIndex == 2;
    }

    private void UpdateCaption()
    {
        (SceneTitle.Text, SceneDescription.Text) = _sceneIndex switch
        {
            1 => (Localization.GetString("ShowcaseCinemaTitle"), Localization.GetString("ShowcaseCinemaDescription")),
            2 => (Localization.GetString("ShowcaseVpnTitle"), Localization.GetString("ShowcaseVpnDescription")),
            _ => (Localization.GetString("ShowcaseMusicTitle"), Localization.GetString("ShowcaseMusicDescription"))
        };
    }

    private static Visual GetVisual(UIElement element, bool translation = false)
    {
        if (translation) ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        return ElementCompositionPreview.GetElementVisual(element);
    }

    private static void TransitionScene(Grid scene, bool entering, float direction)
    {
        var visual = GetVisual(scene, true);
        var compositor = visual.Compositor;
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .7f), new Vector2(.2f, 1));
        visual.CenterPoint = new Vector3(200, 200, 0);
        using var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(450);
        fade.InsertKeyFrame(0, entering ? 0 : 1);
        fade.InsertKeyFrame(1, entering ? 1 : 0, easing);
        visual.StartAnimation("Opacity", fade);
        using var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = fade.Duration;
        scale.InsertKeyFrame(0, entering ? new Vector3(.965f, .965f, 1) : Vector3.One);
        scale.InsertKeyFrame(1, entering ? Vector3.One : new Vector3(.98f, .98f, 1), easing);
        visual.StartAnimation("Scale", scale);
        using var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.Duration = fade.Duration;
        slide.InsertKeyFrame(0, entering ? new Vector3(-18 * direction, 0, 0) : Vector3.Zero);
        slide.InsertKeyFrame(1, entering ? Vector3.Zero : new Vector3(12 * direction, 0, 0), easing);
        visual.Properties.StartAnimation("Translation", slide);
    }

    private void FadeCaption(bool entering)
    {
        var visual = GetVisual(CaptionHost);
        using var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(entering ? 240 : 150);
        fade.InsertKeyFrame(0, entering ? 0 : 1);
        fade.InsertKeyFrame(1, entering ? 1 : 0);
        visual.StartAnimation("Opacity", fade);
    }

    private void StartSceneMotion()
    {
        if (!CanAnimate) return;
        if (_sceneIndex == 0)
        {
            Rotate(MusicOrbit, 36);
            Breathe(MusicCore, 132, 132, 1.025f, 6);
            for (var i = 0; i < MusicBars.Children.Count; i++)
            {
                var bar = (Border)MusicBars.Children[i];
                var visual = GetVisual(bar);
                visual.CenterPoint = new Vector3(1.5f, (float)bar.Height / 2, 0);
                using var pulse = visual.Compositor.CreateVector3KeyFrameAnimation();
                pulse.Duration = TimeSpan.FromSeconds(2.2 + i % 4 * .25);
                pulse.DelayTime = TimeSpan.FromMilliseconds(i * 80);
                pulse.IterationBehavior = AnimationIterationBehavior.Forever;
                pulse.InsertKeyFrame(0, new Vector3(1, .6f, 1));
                pulse.InsertKeyFrame(.5f, Vector3.One);
                pulse.InsertKeyFrame(1, new Vector3(1, .6f, 1));
                visual.StartAnimation("Scale", pulse);
            }
        }
        else if (_sceneIndex == 1)
        {
            Breathe(CinemaScreen, 282, 184, 1.018f, 7);
            var visual = GetVisual(CinemaGlint, true);
            using var shimmer = visual.Compositor.CreateVector3KeyFrameAnimation();
            shimmer.Duration = TimeSpan.FromSeconds(6);
            shimmer.IterationBehavior = AnimationIterationBehavior.Forever;
            shimmer.InsertKeyFrame(0, new Vector3(-100, 0, 0));
            shimmer.InsertKeyFrame(1, new Vector3(340, 0, 0), visual.Compositor.CreateLinearEasingFunction());
            visual.Properties.StartAnimation("Translation", shimmer);
        }
        else
        {
            Rotate(VpnOrbit, 38);
            Breathe(VpnPulse, 290, 290, 1.08f, 3.5);
            var visual = GetVisual(VpnPulse);
            using var pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
            pulse.Duration = TimeSpan.FromSeconds(3.5);
            pulse.IterationBehavior = AnimationIterationBehavior.Forever;
            pulse.InsertKeyFrame(0, .3f);
            pulse.InsertKeyFrame(.5f, 1);
            pulse.InsertKeyFrame(1, .3f);
            visual.StartAnimation("Opacity", pulse);
        }
    }

    private static void Rotate(UIElement element, double seconds)
    {
        var visual = GetVisual(element);
        visual.CenterPoint = new Vector3(200, 200, 0);
        using var rotation = visual.Compositor.CreateScalarKeyFrameAnimation();
        rotation.Duration = TimeSpan.FromSeconds(seconds);
        rotation.IterationBehavior = AnimationIterationBehavior.Forever;
        rotation.InsertKeyFrame(0, 0);
        rotation.InsertKeyFrame(1, 360, visual.Compositor.CreateLinearEasingFunction());
        visual.StartAnimation("RotationAngleInDegrees", rotation);
    }

    private static void Breathe(UIElement element, float width, float height, float peak, double seconds)
    {
        var visual = GetVisual(element);
        visual.CenterPoint = new Vector3(width / 2, height / 2, 0);
        using var breathe = visual.Compositor.CreateVector3KeyFrameAnimation();
        breathe.Duration = TimeSpan.FromSeconds(seconds);
        breathe.IterationBehavior = AnimationIterationBehavior.Forever;
        breathe.InsertKeyFrame(0, Vector3.One);
        breathe.InsertKeyFrame(.5f, new Vector3(peak, peak, 1));
        breathe.InsertKeyFrame(1, Vector3.One);
        visual.StartAnimation("Scale", breathe);
    }

    private void StopSceneMotion()
    {
        foreach (var element in new UIElement[] { MusicOrbit, MusicCore, CinemaScreen, CinemaGlint, VpnOrbit, VpnPulse })
            ResetMotion(element);
        foreach (var bar in MusicBars.Children) ResetMotion(bar);
    }

    private static void ResetMotion(UIElement element)
    {
        var visual = GetVisual(element);
        visual.StopAnimation("RotationAngleInDegrees");
        visual.StopAnimation("Scale");
        visual.StopAnimation("Opacity");
        visual.RotationAngleInDegrees = 0;
        visual.Scale = Vector3.One;
        visual.Opacity = 1;
        // Translation is an opt-in composition property, not a built-in Visual property.
        if (visual.Properties.TryGetVector3("Translation", out _) == CompositionGetValueStatus.Succeeded)
        {
            visual.Properties.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }

    private void Showcase_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        _rotationTimer.Stop();
    }

    private void Showcase_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < ActualWidth && point.Y < ActualHeight) return;
        _pointerInside = false;
        ScheduleRotation();
    }

    private void Showcase_GotFocus(object sender, RoutedEventArgs e)
    {
        _focusInside = true;
        _rotationTimer.Stop();
    }

    private void Showcase_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_loaded || XamlRoot is null) return;
            var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            while (focused is not null && focused != this) focused = VisualTreeHelper.GetParent(focused);
            _focusInside = focused == this;
            ScheduleRotation();
        });
    }
}
