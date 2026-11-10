using Symbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class LatestMusicNewsPanel : UserControl
{
    private readonly UISettings _settings = new();
    private readonly RectangleGeometry _clip = new();
    private MusicNewsItemViewModel[] _items = [];
    private ContentControl _active;
    private ContentControl _inactive;
    private DispatcherQueueTimer? _rotation;
    private Storyboard? _transition;
    private ContentDialog? _dialog;
    private XamlRoot? _root;
    private int _selectedIndex, _requestedIndex, _requestedDirection;
    private bool _loaded, _pointerInside, _focusInside, _inViewport, _hostVisible, _paused;
    private bool _indicatorHandlersAttached;
    private bool _focusCheckQueued;

    public LatestMusicNewsPanel()
    {
        InitializeComponent();
        _active = FirstSlide;
        _inactive = SecondSlide;
        SlideViewport.Clip = _clip;
    }

    private void Panel_Loaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded || _loaded) return;
        _loaded = true;
        if (_items.Length == 0) _items = MusicNewsItemViewModel.CreateSamples();
        _requestedIndex = _selectedIndex = Math.Clamp(_selectedIndex, 0, _items.Length - 1);
        _active.Content = _items[_selectedIndex];
        _active.Visibility = Visibility.Visible;
        if (Indicators.Children.Count == 0) BuildIndicators();
        SetIndicatorHandlersAttached(true);
        UpdateSelection();
        _rotation = DispatcherQueue.CreateTimer();
        _rotation.Interval = TimeSpan.FromSeconds(8);
        _rotation.IsRepeating = false;
        _rotation.Tick += Rotation_Tick;
        _settings.AnimationsEnabledChanged += AnimationsChanged;
        _root = XamlRoot;
        _hostVisible = _root?.IsHostVisible == true;
        if (_root is not null) _root.Changed += Root_Changed;
        UpdateRotationButton();
        CheckFocus();
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || !_loaded) return;
        _loaded = false;
        _pointerInside = _focusInside = _inViewport = _hostVisible = false;
        _settings.AnimationsEnabledChanged -= AnimationsChanged;
        if (_root is not null) _root.Changed -= Root_Changed;
        _root = null;
        if (_rotation is not null)
        {
            _rotation.Stop();
            _rotation.Tick -= Rotation_Tick;
            _rotation = null;
        }
        FinishTransition();
        _dialog?.Hide();
        SetIndicatorHandlersAttached(false);
        // The page can move this same control between rails during resize. Retain
        // owned content/decoded thumbnails, but detach every external subscription.
    }

    private void BuildIndicators()
    {
        Indicators.Children.Clear();
        Indicators.ColumnDefinitions.Clear();
        for (var index = 0; index < _items.Length; index++)
        {
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["DiscoveryWidgetButton"],
                FocusVisualPrimaryBrush = (Brush)Resources["NewsAccent"],
                Height = 24, Tag = index,
                Content = new Border
                {
                    Height = 3, Margin = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(1.5),
                    Background = (Brush)Resources["NewsAccent"], VerticalAlignment = VerticalAlignment.Center,
                    OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(140) }
                }
            };
            AutomationProperties.SetName(button, $"خبر {index + 1}: {_items[index].Title}");
            AutomationProperties.SetPositionInSet(button, index + 1);
            AutomationProperties.SetSizeOfSet(button, _items.Length);
            AutomationProperties.SetHelpText(button, "نمایش این خبر؛ گردش خودکار هنگام کار با صفحه‌کلید متوقف است");
            // First story starts on the right, matching Persian reading order.
            Grid.SetColumn(button, _items.Length - 1 - index);
            Indicators.ColumnDefinitions.Add(new ColumnDefinition());
            Indicators.Children.Add(button);
        }
    }

    private void SetIndicatorHandlersAttached(bool attached)
    {
        if (_indicatorHandlersAttached == attached) return;
        _indicatorHandlersAttached = attached;
        foreach (Button button in Indicators.Children)
        {
            if (attached) button.Click += Indicator_Click;
            else button.Click -= Indicator_Click;
        }
    }

    private void Next_Click(object sender, RoutedEventArgs args) => SelectRelative(1);
    private void Previous_Click(object sender, RoutedEventArgs args) => SelectRelative(-1);
    private void Indicator_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: int index }) Select(index, true);
    }

    private void SelectRelative(int delta)
    {
        if (_items.Length > 0) Select((_requestedIndex + delta + _items.Length) % _items.Length, true, delta);
    }

    private void Select(int index, bool announce, int direction = 0)
    {
        if (!_loaded || index < 0 || index >= _items.Length) return;
        _rotation?.Stop();
        _requestedIndex = index;
        _requestedDirection = direction != 0 ? direction : Math.Sign(index - _selectedIndex);
        if (announce) AccessibilityAnnouncements.Announce(SlideCounter, _items[index].AutomationName, "news-selection");
        // Keep rapid input responsive without stacking animations or moving the controls.
        if (_transition is not null) return;
        if (index == _selectedIndex) { ScheduleRotation(); return; }
        var forward = _requestedDirection > 0;
        _selectedIndex = index;
        var outgoing = _active;
        var incoming = _inactive;
        _active = incoming;
        _inactive = outgoing;
        incoming.Content = _items[index];
        incoming.Visibility = Visibility.Visible;
        incoming.IsHitTestVisible = true;
        outgoing.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(outgoing, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(incoming, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content);
        UpdateSelection();
        if (!_settings.AnimationsEnabled || !_inViewport)
        {
            FinishTransition();
            return;
        }

        var distance = Math.Min(48, Math.Max(20, SlideViewport.ActualWidth * .14)) * (forward ? -1 : 1);
        var storyboard = _transition = new Storyboard();
        Animate(storyboard, (TranslateTransform)incoming.RenderTransform, "X", distance, 0);
        Animate(storyboard, incoming, "Opacity", 0, 1);
        Animate(storyboard, (TranslateTransform)outgoing.RenderTransform, "X", 0, -distance);
        Animate(storyboard, outgoing, "Opacity", 1, 0);
        storyboard.Completed += Transition_Completed;
        storyboard.Begin();
    }

    private static void Animate(Storyboard storyboard, DependencyObject target, string property, double from, double to)
    {
        var animation = new DoubleAnimation
        {
            From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(360)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void Transition_Completed(object? sender, object args)
    {
        FinishTransition();
        if (_loaded && _requestedIndex != _selectedIndex) Select(_requestedIndex, false, _requestedDirection);
    }

    private void StopTransition()
    {
        if (_transition is null) return;
        _transition.Completed -= Transition_Completed;
        _transition.Stop();
        _transition.Children.Clear();
        _transition = null;
    }

    private void FinishTransition()
    {
        StopTransition();
        _active.Opacity = 1;
        ((TranslateTransform)_active.RenderTransform).X = 0;
        _inactive.Opacity = 1;
        ((TranslateTransform)_inactive.RenderTransform).X = 0;
        _inactive.Visibility = Visibility.Collapsed;
        _inactive.Content = null;
        ScheduleRotation();
    }

    private void UpdateSelection()
    {
        SlideCounter.Text = $"{_selectedIndex + 1:00} / {_items.Length:00}";
        AutomationProperties.SetName(SlideCounter, $"خبر {_selectedIndex + 1} از {_items.Length}");
        for (var index = 0; index < Indicators.Children.Count; index++)
        {
            var button = (Button)Indicators.Children[index];
            ((Border)button.Content).Opacity = index == _selectedIndex ? 1 : .2;
            AutomationProperties.SetItemStatus(button, index == _selectedIndex ? "خبر فعلی" : string.Empty);
        }
        AutomationProperties.SetName(ReadButton, $"خواندن خبر نمونه: {_items[_selectedIndex].Title}");
    }

    private void Rotation_Tick(DispatcherQueueTimer sender, object args)
    {
        if (CanRotate) Select((_selectedIndex + 1) % _items.Length, false, 1);
    }

    private bool CanRotate => _loaded && _inViewport && _root?.IsHostVisible == true
        && !_paused && !_pointerInside && !_focusInside && _dialog is null
        && _transition is null && _items.Length > 1 && _settings.AnimationsEnabled;

    private void ScheduleRotation()
    {
        _rotation?.Stop();
        if (CanRotate) _rotation?.Start();
    }

    private void Rotation_Click(object sender, RoutedEventArgs args)
    {
        _paused = !_paused;
        UpdateRotationButton();
        ScheduleRotation();
    }

    private void UpdateRotationButton()
    {
        var motion = _settings.AnimationsEnabled;
        RotationIcon.Symbol = _paused || !motion ? Symbol.Play : Symbol.Pause;
        RotationButton.IsEnabled = motion;
        var label = !motion ? "گردش خودکار با تنظیم کاهش حرکت ویندوز متوقف است"
            : _paused ? "ادامهٔ گردش خودکار اخبار" : "توقف گردش خودکار اخبار";
        AutomationProperties.SetName(RotationButton, label);
        AutomationProperties.SetItemStatus(RotationButton, _paused || !motion ? "گردش خودکار متوقف است" : "گردش خودکار فعال است");
        ToolTipService.SetToolTip(RotationButton, label);
    }

    private void AnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_loaded) return;
        if (!_settings.AnimationsEnabled)
        {
            FinishTransition();
            if (_requestedIndex != _selectedIndex) Select(_requestedIndex, false, _requestedDirection);
        }
        UpdateRotationButton();
        ScheduleRotation();
    });

    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_hostVisible == sender.IsHostVisible) return;
        _hostVisible = sender.IsHostVisible;
        ScheduleRotation();
    }

    private void Panel_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var view = args.EffectiveViewport;
        var visible = view.Width > 0 && view.Height > 0 && view.Right > 0 && view.Bottom > 0
            && view.Left < ActualWidth && view.Top < ActualHeight;
        if (_inViewport == visible) return;
        _inViewport = visible;
        ScheduleRotation();
    }

    private void Panel_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _pointerInside = true;
        ScheduleRotation();
    }

    private void Panel_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < ActualWidth && point.Y < ActualHeight) return;
        _pointerInside = false;
        ScheduleRotation();
    }

    private void Panel_GotFocus(object sender, RoutedEventArgs args) { _focusInside = true; ScheduleRotation(); }
    private void Panel_LostFocus(object sender, RoutedEventArgs args)
    {
        if (_focusCheckQueued) return;
        _focusCheckQueued = DispatcherQueue.TryEnqueue(CheckFocus);
    }
    private void CheckFocus()
    {
        _focusCheckQueued = false;
        if (!_loaded || XamlRoot is null) return;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused is not null && focused != this) focused = VisualTreeHelper.GetParent(focused);
        _focusInside = focused == this;
        ScheduleRotation();
    }

    private void Panel_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled) return;
        if (args.Key == VirtualKey.Left) SelectRelative(1);
        else if (args.Key == VirtualKey.Right) SelectRelative(-1);
        else if (args.Key == VirtualKey.Home) Select(0, true);
        else if (args.Key == VirtualKey.End) Select(_items.Length - 1, true);
        else return;
        args.Handled = true;
    }

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var bounds = new Rect(0, 0, Math.Max(0, args.NewSize.Width), Math.Max(0, args.NewSize.Height));
        if (_clip.Rect != bounds) _clip.Rect = bounds;
    }

    private void Slide_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var width = args.NewSize.Width;
        if (sender is not Grid slide || !double.IsFinite(width) || width <= 0) return;
        var layout = slide.Tag as NewsSlideLayout;
        if (layout is null)
        {
            layout = new NewsSlideLayout((Grid)slide.FindName("NewsArtworkHost"), (Grid)slide.FindName("NewsCopy"));
            slide.Tag = layout;
        }
        // Height changes caused by wrapping/decoding must not schedule another layout.
        if (layout.Width == width) return;
        layout.Width = width;
        var wide = width >= 500;
        if (layout.Wide != wide)
        {
            layout.Wide = wide;
            slide.ColumnDefinitions[1].Width = wide ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(layout.Copy, wide ? 1 : 0);
            Grid.SetRow(layout.Copy, wide ? 0 : 1);
        }
        var height = wide ? 220 : Math.Clamp(width * .58, 112, 174);
        if (layout.Art.Height != height) layout.Art.Height = height;
    }

    private sealed class NewsSlideLayout(Grid art, Grid copy)
    {
        internal Grid Art { get; } = art;
        internal Grid Copy { get; } = copy;
        internal double Width { get; set; } = double.NaN;
        internal bool? Wide { get; set; }
    }

    private async void Read_Click(object sender, RoutedEventArgs args)
    {
        if (!_loaded || _items.Length == 0 || _dialog is not null || XamlRoot is null) return;
        _requestedIndex = _selectedIndex;
        FinishTransition();
        var item = _items[_selectedIndex];
        var content = new StackPanel { Spacing = 16, MaxWidth = 500 };
        content.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), Child = new Image { Source = item.Image, Height = 190, Stretch = Stretch.UniformToFill }
        });
        content.Children.Add(new TextBlock
        {
            Text = "پیش‌نمایش خبر نمونه · " + item.Category, Style = (Style)Application.Current.Resources["DiscoveryWidgetCaption"],
            Foreground = (Brush)Resources["NewsAccent"]
        });
        content.Children.Add(new TextBlock
        {
            Text = item.Body, Style = (Style)Application.Current.Resources["DiscoveryWidgetCaption"],
            FontSize = 13, Opacity = .9, LineHeight = 27, IsTextSelectionEnabled = true
        });
        var dialog = _dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
            Title = new TextBlock
            {
                Text = item.Title, Style = (Style)Application.Current.Resources["DiscoveryWidgetCaption"],
                FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 1
            },
            FontFamily = new FontFamily("/Assets/Fonts/IRANSANS.TTF#IRANSans"),
            FlowDirection = FlowDirection.RightToLeft, CloseButtonText = "بستن", DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 500 }
        };
        ScheduleRotation();
        try { await dialog.ShowAsync(); }
        catch (Exception exception) { AppLog.Error("News preview could not be opened.", exception); }
        finally
        {
            dialog.Content = null;
            dialog.Title = null;
            if (ReferenceEquals(_dialog, dialog)) _dialog = null;
            ScheduleRotation();
        }
    }
}
