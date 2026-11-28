using System;
using System.Collections.Generic;
using DevWinUI;
using DevCompositionShadow = DevWinUI.CompositionShadow;
using DevColorHelper = DevWinUI.ColorHelper;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;

namespace Mizon.Windows.Controls;

[TemplatePart(Name = nameof(PART_ActionButtonTextBlock), Type = typeof(TextBlock))]
[TemplatePart(Name = nameof(PART_DescriptionBlock), Type = typeof(TextBlock))]
[TemplatePart(Name = nameof(PART_TitleBlock), Type = typeof(TextBlock))]
[TemplatePart(Name = nameof(PART_ActionButton), Type = typeof(Button))]
[TemplatePart(Name = nameof(PART_PreviousBtnRectangle), Type = typeof(Rectangle))]
[TemplatePart(Name = nameof(PART_NextBtnRectangle), Type = typeof(Rectangle))]
[TemplatePart(Name = nameof(PART_TertiaryCompositionShadow), Type = typeof(DevCompositionShadow))]
[TemplatePart(Name = nameof(PART_SecondaryCompositionShadow), Type = typeof(DevCompositionShadow))]
[TemplatePart(Name = nameof(PART_PrimaryCompositionShadow), Type = typeof(DevCompositionShadow))]
[TemplatePart(Name = nameof(PART_FadeRectangle), Type = typeof(Rectangle))]
[TemplatePart(Name = nameof(PART_ActionPanel), Type = typeof(StackPanel))]
[TemplatePart(Name = nameof(PART_NextBtn), Type = typeof(Button))]
[TemplatePart(Name = nameof(PART_PreviousBtn), Type = typeof(Button))]
[TemplatePart(Name = nameof(PART_Arc), Type = typeof(ArcProgress))]
[TemplatePart(Name = nameof(PART_PipsPager), Type = typeof(PipsPager))]
[TemplatePart(Name = nameof(PART_SliderGrid), Type = typeof(Grid))]
[TemplatePart(Name = nameof(PART_Image), Type = typeof(ImageFrame))]
public partial class StoreCarousel : Control
{
    private const string PART_RootGrid = "PART_RootGrid";
    private const string PART_Image = "PART_Image";
    private const string PART_SliderGrid = "PART_SliderGrid";
    private const string PART_PipsPager = "PART_PipsPager";
    private const string PART_Arc = "PART_Arc";
    private const string PART_PreviousBtn = "PART_PreviousBtn";
    private const string PART_NextBtn = "PART_NextBtn";
    private const string PART_ActionPanel = "PART_ActionPanel";
    private const string PART_FadeRectangle = "PART_FadeRectangle";

    private const string PART_PrimaryCompositionShadow = "PART_PrimaryCompositionShadow";
    private const string PART_SecondaryCompositionShadow = "PART_SecondaryCompositionShadow";
    private const string PART_TertiaryCompositionShadow = "PART_TertiaryCompositionShadow";

    private const string PART_NextBtnRectangle = "PART_NextBtnRectangle";
    private const string PART_PreviousBtnRectangle = "PART_PreviousBtnRectangle";

    private const string PART_ActionButton = "PART_ActionButton";

    private const string PART_TitleBlock = "PART_TitleBlock";
    private const string PART_DescriptionBlock = "PART_DescriptionBlock";
    private const string PART_ActionButtonTextBlock = "PART_ActionButtonTextBlock";

    private DevCompositionShadow? primaryBox;
    private DevCompositionShadow? secondaryBox;
    private DevCompositionShadow? tertiaryBox;

    private ImageFrame? image;
    private Grid? rootGrid;
    private Grid? sliderGrid;
    private PipsPager? pipsPager;
    private ArcProgress? arc;
    private Button? nextButton;
    private Button? previousButton;
    private StackPanel? actionPanel;
    private Rectangle? fadeRectangle;

    private Rectangle? nextBtnRectangle;
    private Rectangle? previousBtnRectangle;

    private Button? actionButton;

    private TextBlock? titleTextBlock;
    private TextBlock? descriptionTextBlock;
    private TextBlock? actionButtonTextBlock;

    private readonly List<StoreCarouselItem> imageList = new();
    private int _index = 0;
    private PausableDispatcherTimer? timer;
    private int hoverStateVersion;
    private bool isUpdatingPager;
    private int _previousIndex = -1;
    private bool isCallFromPipsPager = false;
    private string? layoutState;
    private string? typographyState;

    public event EventHandler<int>? SelectedIndexChanged;
    public event EventHandler<StoreCarouselEventArgs>? ItemClick;
    public StoreCarousel()
    {
        DefaultStyleKey = typeof(StoreCarousel);
    }

    protected override void OnApplyTemplate()
    {
        actionButton?.Click -= OnActionButtonClick;
        sliderGrid?.PointerReleased -= OnSliderPointerReleased;
        rootGrid?.PointerEntered -= OnSliderPointerEntered;
        rootGrid?.PointerExited -= OnSliderPointerExited;
        primaryBox?.PointerReleased -= OnPrimaryBoxPointerReleased;
        secondaryBox?.PointerReleased -= OnSecondaryBoxPointerReleased;
        tertiaryBox?.PointerReleased -= OnTertiaryBoxPointerReleased;
        nextButton?.Click -= OnNextClick;
        previousButton?.Click -= OnPreviousClick;
        pipsPager?.SelectedIndexChanged -= OnPipsPagerSelectedIndexChanged;
        image?.SizeChanged -= OnImageSizeChanged;
        StopTimer();
        hoverStateVersion++;

        base.OnApplyTemplate();

        rootGrid = GetTemplateChild(PART_RootGrid) as Grid;
        layoutState = null;
        typographyState = null;
        image = GetTemplateChild(PART_Image) as ImageFrame;
        sliderGrid = GetTemplateChild(PART_SliderGrid) as Grid;
        pipsPager = GetTemplateChild(PART_PipsPager) as PipsPager;
        arc = GetTemplateChild(PART_Arc) as ArcProgress;
        nextButton = GetTemplateChild(PART_NextBtn) as Button;
        previousButton = GetTemplateChild(PART_PreviousBtn) as Button;
        actionPanel = GetTemplateChild(PART_ActionPanel) as StackPanel;
        fadeRectangle = GetTemplateChild(PART_FadeRectangle) as Rectangle;
        actionButton = GetTemplateChild(PART_ActionButton) as Button;
        nextBtnRectangle = GetTemplateChild(PART_NextBtnRectangle) as Rectangle;
        previousBtnRectangle = GetTemplateChild(PART_PreviousBtnRectangle) as Rectangle;

        primaryBox = GetTemplateChild(PART_PrimaryCompositionShadow) as DevCompositionShadow;
        secondaryBox = GetTemplateChild(PART_SecondaryCompositionShadow) as DevCompositionShadow;
        tertiaryBox = GetTemplateChild(PART_TertiaryCompositionShadow) as DevCompositionShadow;

        titleTextBlock = GetTemplateChild(PART_TitleBlock) as TextBlock;
        descriptionTextBlock = GetTemplateChild(PART_DescriptionBlock) as TextBlock;
        actionButtonTextBlock = GetTemplateChild(PART_ActionButtonTextBlock) as TextBlock;

        actionButton?.Click += OnActionButtonClick;
        sliderGrid?.PointerReleased += OnSliderPointerReleased;
        rootGrid?.PointerEntered += OnSliderPointerEntered;
        rootGrid?.PointerExited += OnSliderPointerExited;

        KeyDown -= OnKeyDown;
        KeyDown += OnKeyDown;

        primaryBox?.PointerReleased += OnPrimaryBoxPointerReleased;
        secondaryBox?.PointerReleased += OnSecondaryBoxPointerReleased;
        tertiaryBox?.PointerReleased += OnTertiaryBoxPointerReleased;
        nextButton?.Click += OnNextClick;
        previousButton?.Click += OnPreviousClick;
        pipsPager?.SelectedIndexChanged += OnPipsPagerSelectedIndexChanged;

        SizeChanged -= OnSizeChanged;
        SizeChanged += OnSizeChanged;
        image?.SizeChanged += OnImageSizeChanged;
        UpdateLayoutState(ActualWidth);

        pipsPager?.NumberOfPages = imageList.Count;
        if (image is not null)
        {
            if ((uint)_index < (uint)imageList.Count) ShowItem(imageList[_index]);
            timer = new PausableDispatcherTimer(ShuffleDuration);
            timer.Tick += OnTimerTick;
            if (AutoShuffle) timer.Start();
        }

        ClearLights(sliderGrid);
        ClearLights(primaryBox);
        ClearLights(secondaryBox);
        ClearLights(tertiaryBox);

        AddLights(sliderGrid);
        AddLights(primaryBox);
        AddLights(secondaryBox);
        AddLights(tertiaryBox);

        Unloaded -= OnUnloaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        KeyDown -= OnKeyDown;
        hoverStateVersion++;
        StopTimer();
    }

    private void StopTimer()
    {
        timer?.Tick -= OnTimerTick;
        timer?.Stop();
        timer = null;
    }
    private void ClearLights(FrameworkElement? element)
    {
        element?.Lights.Clear();
    }
    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        if ((uint)_index >= (uint)imageList.Count) return;
        var item = imageList[_index];
        item.RaiseAction(this, new StoreCarouselEventArgs(item.Title, item.Description, item.ImageSource, false, item.Parameter));
    }

    private void AddLights(FrameworkElement? element)
    {
        if (element is null) return;
        element.Lights.Add(new AmbLight());
        element.Lights.Add(new RippleLight());
        element.Lights.Add(new HoverLight());
    }
    private void OnSecondaryBoxPointerReleased(object sender, PointerRoutedEventArgs e)
        => RaiseItemClick(e, null, null, SecondaryImageSource, true);

    private void OnTertiaryBoxPointerReleased(object sender, PointerRoutedEventArgs e)
        => RaiseItemClick(e, null, null, TertiaryImageSource, true);

    private void OnPrimaryBoxPointerReleased(object sender, PointerRoutedEventArgs e)
        => RaiseItemClick(e, null, null, PrimaryImageSource, true);

    private void OnSliderPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (IsPointerWithinRoot(e))
            return;

        var exitVersion = ++hoverStateVersion;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (exitVersion != hoverStateVersion)
                return;

            arc?.ResumeFillAnimation();
            timer?.Resume();

            nextButton?.Visibility = Visibility.Collapsed;
            previousButton?.Visibility = Visibility.Collapsed;
        });
    }

    private void OnSliderPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        hoverStateVersion++;
        arc?.PauseFillAnimation();
        timer?.Pause();

        nextButton?.Visibility = Visibility.Visible;
        previousButton?.Visibility = Visibility.Visible;
    }

    private bool IsPointerWithinRoot(PointerRoutedEventArgs e)
    {
        if (rootGrid == null || rootGrid.ActualWidth <= 0 || rootGrid.ActualHeight <= 0)
            return false;

        var position = e.GetCurrentPoint(rootGrid).Position;
        return position.X >= 0
            && position.X < rootGrid.ActualWidth
            && position.Y >= 0
            && position.Y < rootGrid.ActualHeight;
    }
    private void OnSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var index = pipsPager?.SelectedPageIndex ?? _index;
        if ((uint)index >= (uint)imageList.Count) return;
        var item = imageList[index];
        RaiseItemClick(e, item.Title, item.Description, item.ImageSource, false, item.Parameter);
    }
    private void RaiseItemClick(PointerRoutedEventArgs e, string? title, string? description, object? imageSource, bool isThumbnail, object? parameter = null)
    {
        var properties = e.GetCurrentPoint(null).Properties;
        if (properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased)
        {
            ItemClick?.Invoke(this, new StoreCarouselEventArgs(title, description, imageSource?.ToString() ?? "", isThumbnail, parameter));
        }
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayoutState(e.NewSize.Width);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Give the artwork a stable 16:9 viewport rather than dividing an arbitrary
        // fixed height between it and the previews. A vertical ScrollViewer offers
        // infinite height here, so the carousel must derive its height from width.
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth;
        if (rootGrid is { RowDefinitions.Count: > 2 } layoutRoot && width > 0)
        {
            UpdateLayoutState(width);
            const double gap = 12;
            var isWide = width >= 760;
            var artworkWidth = isWide ? Math.Max(0, width - gap) * 1.6 / 2.6 : width;
            layoutRoot.RowDefinitions[0].Height = new GridLength(artworkWidth * 9 / 16);
            layoutRoot.RowDefinitions[2].Height = new GridLength(isWide
                ? 0
                : Math.Max(0, width - gap * 2) / 3 * 9 / 16 + gap);
        }
        return base.MeasureOverride(availableSize);
    }

    private void UpdateLayoutState(double width)
    {
        // This control also lives in narrow columns of a wide window.
        var nextLayout = width >= 760 ? "LargeState" : "SmallState";
        if (layoutState != nextLayout && VisualStateManager.GoToState(this, nextLayout, false))
            layoutState = nextLayout;
        var nextTypography = width < 420 ? "CompactTextState" : "DefaultTextState";
        if (typographyState != nextTypography && VisualStateManager.GoToState(this, nextTypography, false))
            typographyState = nextTypography;
    }

    private void OnImageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (fadeRectangle != null)
            fadeRectangle.Width = e.NewSize.Width * 0.4;
        if (actionPanel != null)
            actionPanel.MaxWidth = Math.Max(0, e.NewSize.Width - actionPanel.Margin.Left - 24);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Left:
                GoToNextPrev(false);
                e.Handled = true;
                break;

            case global::Windows.System.VirtualKey.Right:
                GoToNextPrev(true);
                e.Handled = true;
                break;
        }
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        GoToNextPrev(false);
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        GoToNextPrev(true);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        StartTransition();
    }
    private void OnPipsPagerSelectedIndexChanged(PipsPager sender, PipsPagerSelectedIndexChangedEventArgs args)
    {
        if (!ReferenceEquals(sender, pipsPager) || isUpdatingPager)
            return;

        SelectedIndexChanged?.Invoke(this, sender.SelectedPageIndex);

        isCallFromPipsPager = true;
        GoToIndex(sender.SelectedPageIndex);
    }

    private void StartTransition()
    {
        if (imageList.Count < 2 || image is not { } artwork)
            return;

        timer?.Stop();

        if (pipsPager != null)
        {
            isUpdatingPager = true;
            pipsPager.SelectedPageIndex = _index;
            isUpdatingPager = false;
        }

        int previousIndex = _previousIndex >= 0 ? _previousIndex : _index;
        _previousIndex = _index;

        if (AutoShuffle)
            _index = (_index + 1) % imageList.Count;

        ImageFrameTransitionMode effectiveDirection = ImageFrameTransitionMode.SlideLeft;

        if (AutoShuffle && !isCallFromPipsPager)
            effectiveDirection = ImageFrameTransitionMode.SlideLeft;
        else
        {
            if (_index > previousIndex || pipsPager?.SelectedPageIndex > _index)
                effectiveDirection = ImageFrameTransitionMode.SlideLeft;
            else
                effectiveDirection = ImageFrameTransitionMode.SlideRight;

            isCallFromPipsPager = false;
        }

        artwork.TransitionMode = effectiveDirection;

        var item = imageList[_index];
        if (artwork.Source?.ToString() != item.ImageSource)
        {
            if (arc is { } target) FadeOutInArc(target);
            ShowItem(item);
        }

        if (AutoShuffle)
        {
            timer?.Start();
        }
    }
    private void GoToNextPrev(bool isNext)
    {
        if (pipsPager is not { NumberOfPages: > 0 } pager)
            return;

        int newIndex = pager.SelectedPageIndex;

        if (isNext)
        {
            newIndex++;
            if (newIndex >= pager.NumberOfPages)
                newIndex = 0;
        }
        else
        {
            newIndex--;
            if (newIndex < 0)
                newIndex = pager.NumberOfPages - 1;
        }

        pager.SelectedPageIndex = newIndex;
    }

    public void GoToNext()
    {
        GoToNextPrev(true);
    }
    public void GoToPrevious()
    {
        GoToNextPrev(false);
    }

    public void GoToIndex(int index)
    {
        if (imageList.Count < 1) return;
        if (index < 0 || index >= imageList.Count) return;

        _index = index;
        StartTransition();
    }

    private void OnItemsSourceChanged(IList<StoreCarouselItem>? newList)
    {
        imageList.Clear();

        if (newList != null)
            imageList.AddRange(newList);

        _index = 0;
        _previousIndex = -1;
        if (imageList.Count > 0) ShowItem(imageList[0]);

        pipsPager?.NumberOfPages = imageList.Count;
    }

    private void ShowItem(StoreCarouselItem item)
    {
        if (image is not { } artwork) return;
        artwork.Source = Uri.TryCreate(item.ImageSource, UriKind.RelativeOrAbsolute, out var source) ? source : null;
        titleTextBlock?.Text = item.Title ?? string.Empty;
        descriptionTextBlock?.Text = item.Description ?? string.Empty;
        actionButtonTextBlock?.Text = item.ActionButtonText;
        actionButton?.Visibility = item.ShowActionButton ? Visibility.Visible : Visibility.Collapsed;
        UpdateVisuals(artwork);
        RunTextAnimation(artwork);
    }

    private void UpdateVisuals(ImageFrame artwork)
    {
        var color = artwork.ShadowColor;
        if (fadeRectangle is { } fade)
        {
            fade.Width = artwork.ActualWidth * 0.4;
            fade.Fill = new SolidColorBrush(color) { Opacity = 0.5 };
        }
        if (actionButton is { } button)
        {
            button.Background = new SolidColorBrush(DevColorHelper.DarkenColor(color, 0.5f)) { Opacity = 0.9 };
            button.BorderBrush = CreateLinearGradientBrush(color);
            button.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(color);
            button.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(Colors.White);
        }

        nextBtnRectangle?.Fill = new SolidColorBrush(DevColorHelper.DarkenColor(color, 0.5f)) { Opacity = 0.9 };
        previousBtnRectangle?.Fill = new SolidColorBrush(DevColorHelper.DarkenColor(color, 0.5f)) { Opacity = 0.9 };
    }

    private LinearGradientBrush CreateLinearGradientBrush(Color color)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 2),
            RelativeTransform = new ScaleTransform
            {
                ScaleY = -1,
                CenterY = 0.5,
            }
        };

        brush.GradientStops.Add(new GradientStop
        {
            Offset = 1.0,
            Color = color
        });
        return brush;
    }

    private void RunTextAnimation(ImageFrame artwork)
    {
        if (actionPanel is not { } panel) return;
        if (panel.RenderTransform is not CompositeTransform transform)
        {
            transform = new CompositeTransform();
            panel.RenderTransform = transform;
            panel.RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5);
        }

        var storyboard = new Storyboard();

        var slideAnimation = new DoubleAnimation
        {
            From = 100,
            To = 0,
            Duration = artwork.TransitionDuration,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(slideAnimation, transform);
        Storyboard.SetTargetProperty(slideAnimation, "TranslateX");
        storyboard.Children.Add(slideAnimation);

        var fadeAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = artwork.TransitionDuration,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(fadeAnimation, panel);
        Storyboard.SetTargetProperty(fadeAnimation, "Opacity");
        storyboard.Children.Add(fadeAnimation);

        var scaleXAnimation = new DoubleAnimation
        {
            From = 0.8,
            To = 1,
            Duration = artwork.TransitionDuration,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(scaleXAnimation, transform);
        Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");
        storyboard.Children.Add(scaleXAnimation);

        var scaleYAnimation = new DoubleAnimation
        {
            From = 0.8,
            To = 1,
            Duration = artwork.TransitionDuration,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(scaleYAnimation, transform);
        Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");
        storyboard.Children.Add(scaleYAnimation);

        storyboard.Begin();
    }
    private async void FadeOutInArc(ArcProgress target)
    {
        if (!AutoShuffle) return;

        var fadeOutDuration = TimeSpan.FromMilliseconds(200);
        var fadeInDuration = TimeSpan.FromMilliseconds(200);
        var postImageDelay = TimeSpan.FromMilliseconds(900);

        var totalInterval = ShuffleDuration;

        var arcFillDuration = totalInterval - fadeOutDuration - fadeInDuration - postImageDelay;

        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = fadeOutDuration,
            EnableDependentAnimation = true
        };
        var storyboardOut = new Storyboard();
        storyboardOut.Children.Add(fadeOut);
        Storyboard.SetTarget(fadeOut, target);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        storyboardOut.Begin();

        await Task.Delay(fadeOutDuration + postImageDelay);
        if (!IsLoaded || !ReferenceEquals(target, arc)) return;

        var fadeIn = new DoubleAnimation
        {
            To = 1,
            Duration = fadeInDuration,
            EnableDependentAnimation = true
        };
        var storyboardIn = new Storyboard();
        storyboardIn.Children.Add(fadeIn);
        Storyboard.SetTarget(fadeIn, target);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        storyboardIn.Begin();

        if (arcFillDuration.TotalMilliseconds > 0)
            target.StartFillAnimation(arcFillDuration);
    }
}
