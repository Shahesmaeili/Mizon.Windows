using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System;
using System.ComponentModel;

namespace Mizon.Windows.Controls;

/// <summary>Keeps a widget's layout and existing content stable while its data refreshes.</summary>
[ContentProperty(Name = nameof(Widget))]
public sealed partial class WidgetLoadHost : Grid
{
    public static readonly DependencyProperty WidgetProperty = DependencyProperty.Register(
        nameof(Widget), typeof(UIElement), typeof(WidgetLoadHost), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading), typeof(bool), typeof(WidgetLoadHost), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty SkeletonHeightProperty = DependencyProperty.Register(
        nameof(SkeletonHeight), typeof(double), typeof(WidgetLoadHost), new PropertyMetadata(180d, Changed));
    public static readonly DependencyProperty LoadingTextProperty = DependencyProperty.Register(
        nameof(LoadingText), typeof(string), typeof(WidgetLoadHost), new PropertyMetadata(string.Empty, Changed));
    public static readonly DependencyProperty WidgetNameProperty = DependencyProperty.Register(
        nameof(WidgetName), typeof(string), typeof(WidgetLoadHost), new PropertyMetadata(string.Empty, Changed));
    public static readonly DependencyProperty ReadyTextProperty = DependencyProperty.Register(
        nameof(ReadyText), typeof(string), typeof(WidgetLoadHost), new PropertyMetadata(string.Empty, Changed));
    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(
        nameof(HasError), typeof(bool), typeof(WidgetLoadHost), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText), typeof(string), typeof(WidgetLoadHost), new PropertyMetadata(string.Empty, Changed));

    private readonly ContentPresenter _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Border _skeleton;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress = new() { Height = 2, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private static readonly global::Windows.UI.ViewManagement.UISettings AccessibilitySettings = new();
    private bool _hasPresented;
    private bool _wasLoading;
    private bool _loaded;
    private bool _inViewport;
    private bool _hostVisible;
    private bool _entrancePending;
    private bool _announcementQueued;
    private bool? _pendingLoadingAnnouncement;
    private XamlRoot? _root;
    private ScalarKeyFrameAnimation? _entrance;
    private CompositionScopedBatch? _entranceBatch;

    public WidgetLoadHost()
    {
        _status = new TextBlock
        {
            FontSize = 11, TextAlignment = TextAlignment.Start, Opacity = .7,
            TextReadingOrder = TextReadingOrder.UseFlowDirection, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        // The progress is decorative; its arbitrary fallback value is not a download percentage.
        AutomationProperties.SetAccessibilityView(_progress, AccessibilityView.Raw);
        _progress.Value = 35;
        _skeleton = new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(16),
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(7, 255, 255, 255))
        };
        Children.Add(_body);
        Children.Add(_skeleton);
        Children.Add(_progress);
        Loaded += Host_Loaded;
        Unloaded += Host_Unloaded;
        EffectiveViewportChanged += Host_EffectiveViewportChanged;
        UpdateLanguage();
        ApplyState();
    }

    private void EnsureSkeleton()
    {
        // Local-data widgets never allocate the placeholder rows.
        if (_skeleton.Child is not null) return;
        var lines = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(_status);
        for (var i = 0; i < 3; i++)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Block(36, 36, 8));
            var text = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Block(double.NaN, 9, 4));
            var shortLine = Block(68, 6, 3);
            shortLine.HorizontalAlignment = HorizontalAlignment.Left;
            text.Children.Add(shortLine);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            AutomationProperties.SetAccessibilityView(row, AccessibilityView.Raw);
            lines.Children.Add(row);
        }
        _skeleton.Child = lines;
    }

    public UIElement? Widget { get => (UIElement?)GetValue(WidgetProperty); set => SetValue(WidgetProperty, value); }
    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public double SkeletonHeight { get => (double)GetValue(SkeletonHeightProperty); set => SetValue(SkeletonHeightProperty, value); }
    public string LoadingText { get => (string)GetValue(LoadingTextProperty); set => SetValue(LoadingTextProperty, value); }
    public string WidgetName { get => (string)GetValue(WidgetNameProperty); set => SetValue(WidgetNameProperty, value); }
    public string ReadyText { get => (string)GetValue(ReadyTextProperty); set => SetValue(ReadyTextProperty, value); }
    public bool HasError { get => (bool)GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }
    public string ErrorText { get => (string)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }

    protected override AutomationPeer OnCreateAutomationPeer() => new WidgetAutomationPeer(this);

    private sealed partial class WidgetAutomationPeer(WidgetLoadHost owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(WidgetLoadHost);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => string.IsNullOrWhiteSpace(owner.WidgetName) ? base.GetNameCore() : owner.WidgetName;
    }

    private static Border Block(double width, double height, double radius) => new()
    {
        Width = width, Height = height, CornerRadius = new CornerRadius(radius),
        Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(22, 255, 255, 255))
    };

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((WidgetLoadHost)sender).ApplyState();
    private void Host_Loaded(object sender, RoutedEventArgs args)
    {
        if (!_loaded)
        {
            _loaded = true;
            AccessibilitySettings.AnimationsEnabledChanged += AnimationsChanged;
            LocalizationService.Current.PropertyChanged += LanguageChanged;
        }
        if (!ReferenceEquals(_root, XamlRoot))
        {
            if (_root is not null) _root.Changed -= Root_Changed;
            _root = XamlRoot;
            if (_root is not null) _root.Changed += Root_Changed;
        }
        _hostVisible = _root?.IsHostVisible == true;
        UpdateLanguage();
        ApplyState();
    }

    private void Host_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return; // Responsive column reparenting can deliver a deferred Unloaded event.
        if (_loaded)
        {
            AccessibilitySettings.AnimationsEnabledChanged -= AnimationsChanged;
            LocalizationService.Current.PropertyChanged -= LanguageChanged;
        }
        _loaded = false;
        _inViewport = false;
        _hostVisible = false;
        if (_root is not null) _root.Changed -= Root_Changed;
        _root = null;
        ReleaseEntrance();
        _progress.IsIndeterminate = false;
    }

    private string EffectiveLoadingText => string.IsNullOrWhiteSpace(LoadingText)
        ? LocalizationService.Current.GetString("MusicSurface/WidgetLoading") : LoadingText;
    private string EffectiveErrorText => string.IsNullOrWhiteSpace(ErrorText)
        ? LocalizationService.Current.GetString("MusicSurface/WidgetLoadFailed") : ErrorText;

    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_loaded && args.PropertyName == nameof(LocalizationService.Revision)) UpdateLanguage();
    }

    private void UpdateLanguage()
    {
        var localization = LocalizationService.Current;
        _status.FontFamily = localization.TextFontFamily;
        _status.FlowDirection = localization.FlowDirection;
        // Only the placeholder follows reading order; the widget keeps its own layout.
        // Existing rows inherit this value without rebuilding or replaying the entrance.
        _skeleton.FlowDirection = localization.FlowDirection;
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        var loadingText = EffectiveLoadingText;
        if (_status.Text != loadingText) _status.Text = loadingText;
        var itemStatus = IsLoading ? loadingText : HasError ? EffectiveErrorText : ReadyText;
        if (AutomationProperties.GetItemStatus(this) != itemStatus) AutomationProperties.SetItemStatus(this, itemStatus);
    }

    /// <summary>Use after setting IsLoading=true when replacing the widget's identity, not on refresh.</summary>
    public void ResetPresentation()
    {
        ReleaseEntrance();
        _hasPresented = false;
        _wasLoading = false;
        _entrancePending = false;
        _pendingLoadingAnnouncement = null;
        ApplyState();
    }

    private void ReleaseEntrance()
    {
        if (_entranceBatch is not null)
        {
            _entranceBatch.Completed -= Entrance_Completed;
            _entranceBatch.Dispose();
            _entranceBatch = null;
        }
        if (_entrance is not null)
        {
            _body.StopAnimation(_entrance);
            _entrance.Dispose();
            _entrance = null;
        }
        if (_body.Opacity != 1) _body.Opacity = 1;
    }

    private void ApplyState()
    {
        if (_skeleton is null) return;
        // Refresh changes state, not the content's visual tree or focus.
        if (!ReferenceEquals(_body.Content, Widget)) _body.Content = Widget;
        UpdateStatusText();
        var skeletonHeight = double.IsFinite(SkeletonHeight) ? Math.Max(0, SkeletonHeight) : 180;
        if (_skeleton.MinHeight != skeletonHeight) _skeleton.MinHeight = skeletonHeight;
        var initial = IsLoading && !_hasPresented;
        if (initial) EnsureSkeleton();
        SetVisible(_skeleton, initial);
        SetVisible(_body, !initial);
        SetVisible(_progress, IsLoading);
        if (!_loaded) return;

        if (IsLoading != _wasLoading) _pendingLoadingAnnouncement = IsLoading;
        _wasLoading = IsLoading;
        if (!IsLoading && !_hasPresented && Widget is not null)
        {
            _hasPresented = true;
            _entrancePending = true;
        }
        UpdateMotion();
        QueueAnnouncement();
    }

    private static void SetVisible(UIElement element, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (element.Visibility != visibility) element.Visibility = visibility;
    }

    private bool IsVisibleToUser => _loaded && _hostVisible && _inViewport;

    private void UpdateMotion()
    {
        var animationsEnabled = AccessibilitySettings.AnimationsEnabled;
        var canAnimate = IsVisibleToUser && animationsEnabled;
        var indeterminate = IsLoading && canAnimate;
        if (_progress.IsIndeterminate != indeterminate) _progress.IsIndeterminate = indeterminate;
        if (!canAnimate)
        {
            ReleaseEntrance();
            if (!animationsEnabled) _entrancePending = false;
            return;
        }
        if (!_entrancePending || IsLoading) return;
        _entrancePending = false;
        ReleaseEntrance();
        var compositor = ElementCompositionPreview.GetElementVisual(_body).Compositor;
        _entranceBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _entranceBatch.Completed += Entrance_Completed;
        // Animate the XAML facade, not the hand-out Visual used by input hit testing.
        var animation = _entrance = compositor.CreateScalarKeyFrameAnimation();
        animation.Target = "Opacity";
        animation.InsertKeyFrame(0, 0);
        animation.InsertKeyFrame(1, 1);
        animation.Duration = TimeSpan.FromMilliseconds(240);
        _body.StartAnimation(animation);
        _entranceBatch.End();
    }

    private void Entrance_Completed(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (ReferenceEquals(sender, _entranceBatch)) ReleaseEntrance();
    }

    private void Host_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var viewport = args.EffectiveViewport;
        var visible = viewport.Width > 0 && viewport.Height > 0 && viewport.Right > 0 && viewport.Bottom > 0
            && viewport.Left < ActualWidth && viewport.Top < ActualHeight;
        if (_inViewport == visible) return;
        _inViewport = visible;
        UpdateMotion();
        QueueAnnouncement();
    }

    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (!_loaded || !ReferenceEquals(sender, _root)) return;
        if (_hostVisible == sender.IsHostVisible) return;
        _hostVisible = sender.IsHostVisible;
        UpdateMotion();
        QueueAnnouncement();
    }

    private void AnimationsChanged(global::Windows.UI.ViewManagement.UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_loaded) UpdateMotion();
    });

    private void QueueAnnouncement()
    {
        if (_announcementQueued || _pendingLoadingAnnouncement is null || !IsVisibleToUser) return;
        _announcementQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _announcementQueued = false;
            if (!IsVisibleToUser || _pendingLoadingAnnouncement is not { } loading) return;
            _pendingLoadingAnnouncement = null;
            // Wait until the current batch of bindings includes HasError. The host remains
            // visible when the skeleton disappears and when existing data is refreshed.
            var message = loading ? EffectiveLoadingText : HasError ? EffectiveErrorText : ReadyText;
            AccessibilityAnnouncements.Announce(this, message, $"Widget:{WidgetName}");
        });
    }
}
