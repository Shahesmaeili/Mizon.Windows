using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Windowing;
using Mizon.Windows.ViewModels;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System.ComponentModel;
using System;
using System.Numerics;
using System.Threading;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Pages;

public sealed partial class LoginPage : Page
{
    public LoginPageViewModel ViewModel { get; }
    public LocalizationService Localization => LocalizationService.Current;
    private readonly UISettings _uiSettings = new();
    private CancellationTokenSource? _pageCancellation;
    private string? _layoutState;
    private bool _entrancePlayed;
    private Window? _hostWindow;
    private AppWindow? _hostAppWindow;
    private XamlRoot? _hostXamlRoot;
    private bool _windowActive = true;
    private int? _normalBorderColor;

    public LoginPage()
    {
        ViewModel = ((App)Application.Current).Services.GetRequiredService<LoginPageViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
        SizeChanged += Page_SizeChanged;
    }

    private void Page_Loaded(object sender, RoutedEventArgs args)
    {
        _pageCancellation ??= new CancellationTokenSource();
        ViewModel.PropertyChanged -= AccountStateChanged;
        ViewModel.PropertyChanged += AccountStateChanged;
        ViewModel.SubmissionCompleted -= AuthenticationSubmissionCompleted;
        ViewModel.SubmissionCompleted += AuthenticationSubmissionCompleted;
        Localization.PropertyChanged -= LocalizationChanged;
        Localization.PropertyChanged += LocalizationChanged;
        ViewModel.RefreshLocalization();
        Bindings.Update();
        UpdateErrorBorder();
        if (_hostWindow is null && App.MainMizonWindow is { } window)
        {
            _hostWindow = window;
            window.Activated += HostWindow_Activated;
            window.Closed += HostWindow_Closed;
            _hostAppWindow = window.AppWindow;
            if (_hostAppWindow is not null) _hostAppWindow.Changed += HostWindow_Changed;
            _hostXamlRoot = XamlRoot;
            if (_hostXamlRoot is not null) _hostXamlRoot.Changed += HostRootChanged;
            AttachLanguagePicker();
        }
        InitializeGamepad();
        UpdateLayout(ActualWidth, ActualHeight);
        UpdateShowcase();
        PlayEntrance();
        Services.AppLog.MemorySnapshot("Login loaded");

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_pageCancellation is not null && IsLoaded)
            {
                OpenPendingAccountAction();
                AuthForms.FocusInitialField();
            }
        });
    }

    internal async void OpenPendingAccountAction()
    {
        if (App.PendingDestination is not ("login" or "register") || !IsLoaded || !ViewModel.ControlsIsEnabled) return;
        if (!ReferenceEquals(ViewModel.NavigationService.CurrentMainPage, this)) return;
        var registration = App.PendingDestination == "register";
        App.PendingDestination = null;
        await AuthForms.SelectModeAsync(registration);
    }

    private void LocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision) || !IsLoaded) return;
        ViewModel.RefreshLocalization();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs args) =>
        UpdateLayout(args.NewSize.Width, args.NewSize.Height);

    private void UpdateLayout(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        var captionHeight = GetCaptionHeight();
        if (CaptionInset.Height.Value != captionHeight) CaptionInset.Height = new GridLength(captionHeight);
        UpdateLanguagePickerPlacement(width, captionHeight);

        var bodyHeight = height - captionHeight;
        var state = width >= 900 && bodyHeight >= 550 ? "WideLogin"
            : width >= 680 && bodyHeight >= 420 ? "MediumLogin" : "CompactLogin";
        if (_layoutState == state) return;
        if (!VisualStateManager.GoToState(this, state, false)) return;
        _layoutState = state;
        UpdateShowcase();
    }

    private void UpdateShowcase() =>
        ArtPanel.IsActive = _pageCancellation is not null && _layoutState is ("WideLogin" or "MediumLogin") && _windowActive
            && _hostAppWindow?.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private void HostWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        UpdateGamepadActivity();
        UpdateShowcase();
    }

    private void HostWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange) _gamepadNavigation?.ClearCaptionSelection();
        if (args.DidPresenterChange) UpdateLayout(ActualWidth, ActualHeight);
        if (!args.DidPresenterChange && !args.DidVisibilityChange) return;
        UpdateGamepadActivity();
        UpdateShowcase();
    }

    private double GetCaptionHeight()
    {
        if (_hostAppWindow?.Presenter.Kind == AppWindowPresenterKind.FullScreen) return 0;
        if (_hostAppWindow?.TitleBar is not { Height: > 0 } titleBar || XamlRoot is not { RasterizationScale: > 0 } root)
            return 48;
        return Math.Ceiling(titleBar.Height / root.RasterizationScale);
    }

    private void HostRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (IsLoaded && ReferenceEquals(sender, _hostXamlRoot))
        {
            _gamepadNavigation?.ClearCaptionSelection();
            UpdateLayout(ActualWidth, ActualHeight);
        }
    }

    private void DetachHostWindow()
    {
        DetachLanguagePicker();
        if (_hostXamlRoot is not null) _hostXamlRoot.Changed -= HostRootChanged;
        if (_hostAppWindow is not null) _hostAppWindow.Changed -= HostWindow_Changed;
        if (_hostWindow is not null)
        {
            _hostWindow.Activated -= HostWindow_Activated;
            _hostWindow.Closed -= HostWindow_Closed;
        }
        _hostXamlRoot = null;
        _hostAppWindow = null;
        _hostWindow = null;
    }

    private async void AuthForms_SubmitRequested(object? sender, EventArgs args)
    {
        if (_pageCancellation is { } lifetime)
        {
            await ViewModel.SubmitAsync(lifetime.Token);
            // A URI received while submitting stays queued until the request finishes,
            // including when a successful sign-in has already opened Welcome.
            App.ResumePendingActivation();
        }
    }

    private void AuthForms_ModeChanged(object? sender, EventArgs args)
    {
        _gamepadNavigation?.ResetSurface();
        if (_gamepadNavigation?.IsGamepadMode == true)
            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded && _gamepadNavigation?.IsGamepadMode == true)
                    AuthForms.FocusInitialGamepadField();
            });
        (App.MainMizonWindow as MainWindow)?.RefreshTitle();
        // Returning from the taller registration form should not leave the login heading offscreen.
        LoginScroller.ChangeView(null, 0, null, disableAnimation: true);
        AccessibilityAnnouncements.Announce(this, Localization.GetString(ViewModel.IsRegistrationMode ? "AuthModeRegister" : "AuthModeLogin"), "Mizon.Account.Mode");
    }

    private void AccountStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModel.HasError)) UpdateErrorBorder();
        if (args.PropertyName == nameof(ViewModel.ControlsIsEnabled) && ViewModel.ControlsIsEnabled)
            App.ResumePendingActivation();
        if (args.PropertyName == nameof(ViewModel.ShowProgressBar) && ViewModel.ShowProgressBar)
            AccessibilityAnnouncements.Announce(this, Localization.GetString(ViewModel.IsRegistrationMode ? "AuthSubmittingRegister" : "AuthSubmittingLogin"), "Mizon.Account.Submit");
    }

    private void UpdateErrorBorder()
    {
        if (App.MainMizonWindow is not { } window) return;
        if (ViewModel.HasError)
        {
            _normalBorderColor ??= WindowNativeInterop.GetWindowBorderColorValue(window);
            _ = WindowNativeInterop.AnimateWindowBorderColor(window, global::Windows.UI.Color.FromArgb(255, 239, 106, 122));
        }
        else RestoreBorder();
    }

    private void RestoreBorder(bool animate = true)
    {
        if (_normalBorderColor is int color && App.MainMizonWindow is { } window)
        {
            if (animate) _ = WindowNativeInterop.AnimateWindowBorderColor(window, color);
            else WindowNativeInterop.RestoreWindowBorderColor(window, color);
        }
        // Keep the original color while returning to blue, so a new error during that
        // transition cannot accidentally capture an intermediate reddish color.
        if (!animate) _normalBorderColor = null;
    }

    private void PlayEntrance()
    {
        if (_entrancePlayed) return;
        _entrancePlayed = true;
        if (!_uiSettings.AnimationsEnabled) return;
        ElementCompositionPreview.SetIsTranslationEnabled(LoginContent, true);
        var visual = ElementCompositionPreview.GetElementVisual(LoginContent);
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 0.8f), Vector2.One);
        using var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, easing);
        fade.Duration = TimeSpan.FromMilliseconds(360);
        visual.StartAnimation("Opacity", fade);
        using var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new Vector3(0, 12, 0));
        slide.InsertKeyFrame(1, Vector3.Zero, easing);
        slide.Duration = TimeSpan.FromMilliseconds(420);
        visual.Properties.StartAnimation("Translation", slide);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs args)
    {
        ViewModel.SubmissionCompleted -= AuthenticationSubmissionCompleted;
        DisposeGamepad();
        ViewModel.PropertyChanged -= AccountStateChanged;
        Localization.PropertyChanged -= LocalizationChanged;
        Bindings.StopTracking();
        RestoreBorder(animate: false);
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
        ArtPanel.IsActive = false;
        DetachHostWindow();
        AuthForms.CancelTransitions();
        ViewModel.ClearSensitiveInputs();

        var visual = ElementCompositionPreview.GetElementVisual(LoginContent);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
        if (visual.Properties.TryGetVector3("Translation", out _) == CompositionGetValueStatus.Succeeded)
        {
            visual.Properties.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }
}
