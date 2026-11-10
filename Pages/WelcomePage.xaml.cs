using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
using Microsoft.Windows.BadgeNotifications;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Pages;

public sealed partial class WelcomePage : Page
{
    public WelcomePageViewModel ViewModel { get; }
    public LocalizationService Localization => LocalizationService.Current;
    private readonly UISettings _uiSettings = new();
    private CancellationTokenSource? _pageCancellation;
    private bool _initializing;
    private bool _navigationSubscribed;
    private Window? _hostWindow;
    private AppWindow? _hostAppWindow;
    private bool _windowActive = true;
    private bool _orbitsStarted;
    private readonly FrameworkElement[] _orbits;
    private readonly AnimationController?[] _orbitControllers = new AnimationController?[3];

    public WelcomePage()
    {
        ViewModel = ((App)Application.Current).Services.GetRequiredService<WelcomePageViewModel>();
        InitializeComponent();
        _orbits = [OrbitOne, OrbitTwo, OrbitThree];
        DataContext = ViewModel;
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_pageCancellation is not null) return;
        _pageCancellation = new CancellationTokenSource();
        Localization.PropertyChanged += Localization_PropertyChanged;
        ViewModel.RefreshLocalization();
        Bindings.Update();
        if (!_navigationSubscribed)
        {
            ViewModel.NavigationService.MainNavigationChanged += NavigationChanged;
            _navigationSubscribed = true;
        }

        _hostWindow = App.MainMizonWindow;
        _windowActive = true;
        if (_hostWindow is not null)
        {
            _hostWindow.Activated += HostWindow_Activated;
            _hostWindow.Closed += HostWindow_Closed;
            // Window.AppWindow becomes null during close; retain the event source
            // we actually subscribed to so unload can detach from that instance.
            _hostAppWindow = _hostWindow.AppWindow;
            if (_hostAppWindow is not null) _hostAppWindow.Changed += HostWindow_Changed;
        }
        _uiSettings.AnimationsEnabledChanged += MotionPreferenceChanged;
        UpdateMotionActivity();
        PlayEntrance();
        AppLog.MemorySnapshot("Welcome loaded");
        await EnterAsync();
    }

    private async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        if (_initializing || _pageCancellation is null) return;
        _initializing = true;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_pageCancellation.Token, cancellationToken);
        var token = request.Token;
        var started = Stopwatch.GetTimestamp();
        SetActivityBadge(true);
        try
        {
            if (!await ViewModel.InitializeAsync(token) || token.IsCancellationRequested) return;

            // A quick cached login still gets a short reveal, without the old fake version-check delay.
            var remaining = TimeSpan.FromMilliseconds(_uiSettings.AnimationsEnabled ? 900 : 0)
                - Stopwatch.GetElapsedTime(started);
            // Give a late-loading avatar a frame and a short reveal before the
            // connected animation captures the welcome profile.
            if (ViewModel.ProfileImage is not null && _uiSettings.AnimationsEnabled)
                remaining = remaining < TimeSpan.FromMilliseconds(220) ? TimeSpan.FromMilliseconds(220) : remaining;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
            token.ThrowIfCancellationRequested();

            if (ViewModel.Account is { } account)
            {
                var home = ((App)Application.Current).Services.GetRequiredService<HomePageViewModel>();
                home.ApplyProfile(account, ViewModel.ProfileImagePath, ViewModel.ProfileImage, ViewModel.TaskbarProfileSession);
            }

            ViewModel.NavigationService.MainNavigate(typeof(HomePage));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            _initializing = false;
            SetActivityBadge(false);
        }
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await EnterAsync();

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalizationService.Revision)) ViewModel.RefreshLocalization();
    }

    private void Login_Click(object sender, RoutedEventArgs e) => ViewModel.GoToLogin();

    private void NavigationChanged(object? sender, NavigationChangedEventArgs e)
    {
        if (e.IsEnteringPage || e.FromPageType != typeof(WelcomePage) || e.ToPageType != typeof(HomePage)) return;
        if (!_uiSettings.AnimationsEnabled || !ProfileBorder.IsLoaded || ProfileBorder.ActualWidth <= 0) return;

        try
        {
            // Preserve the original logo/profile connection to the app's title-bar profile button.
            var animation = ConnectedAnimationService.GetForCurrentView()
                .PrepareToAnimate("ProfilePictureAnimation", ProfileBorder);
            animation.Configuration = new DirectConnectedAnimationConfiguration();
        }
        catch (Exception exception)
        {
            AppLog.Error("Welcome profile connected animation could not be prepared.", exception);
        }
    }

    public void StartAnimations()
    {
        if (_orbitsStarted || !_uiSettings.AnimationsEnabled) return;
        _orbitsStarted = true;
        RotateOrbit(OrbitOne, 42, false);
        RotateOrbit(OrbitTwo, 62, true);
        RotateOrbit(OrbitThree, 88, false);
        for (var i = 0; i < _orbits.Length; i++)
            _orbitControllers[i] = ElementCompositionPreview.GetElementVisual(_orbits[i]).TryGetAnimationController("RotationAngleInDegrees");
    }

    private void HostWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        UpdateMotionActivity();
    }

    private void HostWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidVisibilityChange) UpdateMotionActivity();
    }

    private void HostWindow_Closed(object sender, WindowEventArgs args)
    {
        _windowActive = false;
        Localization.PropertyChanged -= Localization_PropertyChanged;
        Bindings.StopTracking();
        DetachHostWindow();
        UpdateMotionActivity();
    }

    private void DetachHostWindow()
    {
        var window = _hostWindow;
        var appWindow = _hostAppWindow;
        _hostWindow = null;
        _hostAppWindow = null;
        if (window is not null)
        {
            window.Activated -= HostWindow_Activated;
            window.Closed -= HostWindow_Closed;
        }
        if (appWindow is not null) appWindow.Changed -= HostWindow_Changed;
    }

    private void MotionPreferenceChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_pageCancellation is not null) UpdateMotionActivity();
    });

    private void UpdateMotionActivity()
    {
        if (!_uiSettings.AnimationsEnabled) { StopAnimations(); return; }
        var active = _pageCancellation is not null && _windowActive && _hostAppWindow is { } appWindow
            && appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        if (active) StartAnimations();
        foreach (var controller in _orbitControllers)
        {
            if (active) controller?.Resume(); else controller?.Pause();
        }
    }

    private static void RotateOrbit(FrameworkElement element, double seconds, bool reverse)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3(200, 200, 0);
        using var rotation = visual.Compositor.CreateScalarKeyFrameAnimation();
        using var linear = visual.Compositor.CreateLinearEasingFunction();
        rotation.InsertKeyFrame(0, 0);
        rotation.InsertKeyFrame(1, reverse ? -360 : 360, linear);
        rotation.Duration = TimeSpan.FromSeconds(seconds);
        rotation.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("RotationAngleInDegrees", rotation);
    }

    private void PlayEntrance()
    {
        if (!_uiSettings.AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(WelcomeContent);
        using var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(420);
        visual.StartAnimation("Opacity", fade);
    }

    public void StopAnimations()
    {
        _orbitsStarted = false;
        foreach (var element in _orbits)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("RotationAngleInDegrees");
            visual.RotationAngleInDegrees = 0;
        }
        for (var i = 0; i < _orbitControllers.Length; i++)
        {
            // TryGetAnimationController returns the compositor's active controller.
            // StopAnimation releases its animation; closing that controller while
            // a commit still references it can corrupt the native rebuild list.
            _orbitControllers[i] = null;
        }
        var contentVisual = ElementCompositionPreview.GetElementVisual(WelcomeContent);
        contentVisual.StopAnimation("Opacity");
        contentVisual.Opacity = 1;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Localization.PropertyChanged -= Localization_PropertyChanged;
        Bindings.StopTracking();
        // Home now owns the decoded source; the cached welcome page need not retain it.
        ViewModel.ReleaseProfileImage();
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
        _uiSettings.AnimationsEnabledChanged -= MotionPreferenceChanged;
        DetachHostWindow();
        StopAnimations();
        if (_navigationSubscribed)
        {
            ViewModel.NavigationService.MainNavigationChanged -= NavigationChanged;
            _navigationSubscribed = false;
        }
    }

    private static void SetActivityBadge(bool active)
    {
        try
        {
            if (active) BadgeNotificationManager.Current.SetBadgeAsGlyph(BadgeNotificationGlyph.Activity);
            else BadgeNotificationManager.Current.ClearBadge();
        }
        catch (Exception exception) { AppLog.Error("Welcome activity badge could not be updated.", exception); }
    }
}
