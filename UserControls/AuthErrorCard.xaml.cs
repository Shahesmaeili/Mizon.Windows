using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Mizon.Windows.Services;
using System;
using System.ComponentModel;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class AuthErrorCard : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(AuthErrorCard), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(AuthErrorCard), new PropertyMetadata(string.Empty, Changed));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(AuthErrorCard), new PropertyMetadata(string.Empty, Changed));
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    private bool _showing;
    private readonly UISettings _settings = new();
    public AuthErrorCard()
    {
        InitializeComponent();
        Loaded += Card_Loaded;
        Unloaded += Card_Unloaded;
    }
    public string LocalizedTitle(string title, int revision) =>
        string.IsNullOrWhiteSpace(title) ? Localization.GetString("AuthRetryTitle") : title;

    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        Bindings.Update();
        Localization.PropertyChanged -= Localization_PropertyChanged;
        Localization.PropertyChanged += Localization_PropertyChanged;
        Update();
    }

    private void Card_Unloaded(object sender, RoutedEventArgs e)
    {
        Localization.PropertyChanged -= Localization_PropertyChanged;
        Bindings.StopTracking();
        Stop();
        _showing = false;
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.Revision)) Update();
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((AuthErrorCard)sender).Update();
    private void Update()
    {
        Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, $"{LocalizedTitle(Title, Localization.Revision)}. {Message}");
        if (!IsOpen) { Stop(); _showing = false; return; }
        if (!IsLoaded || _showing) return;
        _showing = true;
        if (_settings.AnimationsEnabled)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(ErrorSurface, true);
            var visual = ElementCompositionPreview.GetElementVisual(ErrorSurface);
            using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, .8f), Vector2.One);
            using var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1, easing); fade.Duration = TimeSpan.FromMilliseconds(220);
            visual.StartAnimation("Opacity", fade);
            using var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0, new Vector3(0, 6, 0)); slide.InsertKeyFrame(1, Vector3.Zero, easing); slide.Duration = fade.Duration;
            visual.Properties.StartAnimation("Translation", slide);
        }
        FrameworkElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void Stop()
    {
        if (ErrorSurface is null) return;
        var visual = ElementCompositionPreview.GetElementVisual(ErrorSurface);
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
        if (visual.Properties.TryGetVector3("Translation", out _) == CompositionGetValueStatus.Succeeded)
        { visual.Properties.StopAnimation("Translation"); visual.Properties.InsertVector3("Translation", Vector3.Zero); }
    }
}
