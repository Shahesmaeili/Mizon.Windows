using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

namespace Mizon.Windows.UserControls;

public sealed partial class ArtistSpotlight : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IReadOnlyList<ArtistSpotlightItemViewModel>), typeof(ArtistSpotlight),
        new PropertyMetadata(null, ItemsChanged));

    private readonly global::Windows.UI.ViewManagement.UISettings _settings = new();
    private readonly RectangleGeometry _clip = new();
    private SpriteVisual? _modelGlowVisual;
    private CompositionRadialGradientBrush? _modelGlowBrush;
    private readonly List<CompositionColorGradientStop> _modelGlowStops = [];
    private ArtistSpotlightItemViewModel? _current;
    private int _index;
    private bool _loaded;
    private string? _layout;

    public ArtistSpotlight()
    {
        InitializeComponent();
        CardClip.Clip = _clip;
    }

    public IReadOnlyList<ArtistSpotlightItemViewModel>? ItemsSource
    {
        get => (IReadOnlyList<ArtistSpotlightItemViewModel>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public event EventHandler<TopArtistItemViewModel>? ProfileRequested;
    public event EventHandler<DiscoveryMusicItemViewModel>? MusicRequested;

    private static void ItemsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (ArtistSpotlight)sender;
        control.ResetContentMotion();
        control._index = 0;
        control.RenderSelection();
    }

    private void Spotlight_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        UpdateLayout(ActualWidth, ActualHeight);
        RenderSelection();
        AnimateEntrance(1);
    }

    private void Spotlight_Unloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        ResetContentMotion();
        if (_current is not null) _current.Artist.PropertyChanged -= ArtistChanged;
        ReleaseModelGlow();
    }

    private void Spotlight_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateLayout(e.NewSize.Width, e.NewSize.Height);

    private void UpdateLayout(double width, double height)
    {
        _clip.Rect = new global::Windows.Foundation.Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
        if (width <= 0) return;
        var state = width < 640 ? "Compact" : "Wide";
        if (_layout != state && VisualStateManager.GoToState(this, state, false))
        {
            _layout = state;
            // StackLayout is not a visual element; set its orientation directly.
            TracksLayout.Orientation = state == "Compact" ? Orientation.Horizontal : Orientation.Vertical;
            TracksScroll.ChangeView(0, 0, null, true);
        }
    }

    private void RenderSelection()
    {
        if (_current is not null) _current.Artist.PropertyChanged -= ArtistChanged;
        _current = null;
        if (ItemsSource is not { Count: > 0 } items)
        {
            Visibility = Visibility.Collapsed;
            Tracks.ItemsSource = null;
            ArtistModel.ModelPath = string.Empty;
            return;
        }

        Visibility = Visibility.Visible;
        _index = (_index % items.Count + items.Count) % items.Count;
        _current = items[_index];
        var artist = _current.Artist;
        if (_loaded) artist.PropertyChanged += ArtistChanged;
        ArtistName.Text = artist.DisplayName;
        GenreText.Text = artist.GenreTitle;
        ToolTipService.SetToolTip(ArtistName, _current.Description);
        ToolTipService.SetToolTip(ArtistModel, _current.ModelAttribution);
        ArtistModel.InitialYawInDegrees = _current.ModelYawInDegrees;
        ArtistModel.ViewScale = _current.ModelViewScale;
        ArtistModel.VerticalFocus = _current.ModelVerticalFocus;
        ArtistModel.ModelPath = _current.ModelPath;
        Tracks.ItemsSource = _current.LatestMusics;
        TracksScroll.ChangeView(0, 0, null, true);
        SongsText.Text = _current.SongCountText;
        RatingText.Text = _current.RatingText;
        ListenerText.Text = artist.MonthlyListenersFullText;
        ListeningTrend.Data = artist.CreateActivityPath();
        PageNumber.Text = $"{_index + 1:00} / {items.Count:00}";
        ((SolidColorBrush)Resources["SpotlightAccent"]).Color = artist.AccentColor;
        UpdateModelGlow();
        PreviousButton.IsEnabled = NextButton.IsEnabled = items.Count > 1;
        UpdateFollow();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ProfileButton, $"پروفایل {artist.DisplayName}");
    }

    private void ArtistChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!ReferenceEquals(sender, _current?.Artist)) return;
        // IsFollowed also raises several derived-property notifications. Update
        // the persistent controls once instead of replacing their content repeatedly.
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(TopArtistItemViewModel.IsFollowed))
            UpdateFollow();
    }

    private void UpdateModelGlow()
    {
        if (!_loaded || _current is null) return;
        if (_modelGlowVisual is null)
        {
            var compositor = ElementCompositionPreview.GetElementVisual(ModelGlow).Compositor;
            _modelGlowBrush = compositor.CreateRadialGradientBrush();
            _modelGlowBrush.EllipseCenter = new Vector2(.5f, .72f);
            _modelGlowBrush.EllipseRadius = new Vector2(.48f, .25f);
            // A soft floor light stays inside the portrait column, behind the model.
            foreach (var offset in new[] { 0f, .45f, .78f, 1f })
            {
                var stop = compositor.CreateColorGradientStop();
                stop.Offset = offset;
                _modelGlowStops.Add(stop);
                _modelGlowBrush.ColorStops.Add(stop);
            }
            _modelGlowVisual = compositor.CreateSpriteVisual();
            _modelGlowVisual.RelativeSizeAdjustment = Vector2.One;
            _modelGlowVisual.Brush = _modelGlowBrush;
            ElementCompositionPreview.SetElementChildVisual(ModelGlow, _modelGlowVisual);
        }

        var color = _current.Artist.AccentColor;
        ReadOnlySpan<byte> alpha = [86, 43, 12, 0];
        for (var index = 0; index < _modelGlowStops.Count; index++)
            _modelGlowStops[index].Color = global::Windows.UI.Color.FromArgb(alpha[index], color.R, color.G, color.B);
    }

    private void ReleaseModelGlow()
    {
        ElementCompositionPreview.SetElementChildVisual(ModelGlow, null);
        if (_modelGlowVisual is not null) _modelGlowVisual.Brush = null;
        _modelGlowVisual?.Dispose();
        _modelGlowVisual = null;
        _modelGlowBrush?.ColorStops.Clear();
        foreach (var stop in _modelGlowStops) stop.Dispose();
        _modelGlowStops.Clear();
        _modelGlowBrush?.Dispose();
        _modelGlowBrush = null;
    }

    private void UpdateFollow()
    {
        if (_current is null) return;
        var artist = _current.Artist;
        if (!Equals(FollowButton.Content, artist.FollowButtonText))
            FollowButton.Content = artist.FollowButtonText;
        FollowersText.Text = artist.FollowersText;
        ToolTipService.SetToolTip(FollowersText, artist.FollowersFullText);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FollowButton,
            $"{artist.FollowButtonText} · {artist.DisplayName}");
    }

    private void Follow_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) _current.Artist.IsFollowed = !_current.Artist.IsFollowed;
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) ProfileRequested?.Invoke(this, _current.Artist);
    }

    private void Track_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiscoveryMusicItemViewModel track }) MusicRequested?.Invoke(this, track);
    }

    private void TracksScroll_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // Wide layouts keep the ScrollViewer's native vertical wheel handling.
        if (_layout != "Compact") return;
        if (TracksScroll.ScrollableWidth <= 0) return;
        var delta = e.GetCurrentPoint(TracksScroll).Properties.MouseWheelDelta;
        if (delta == 0) return;
        var offset = Math.Clamp(TracksScroll.HorizontalOffset - delta * .65, 0, TracksScroll.ScrollableWidth);
        if (Math.Abs(offset - TracksScroll.HorizontalOffset) < .5) return;
        TracksScroll.ChangeView(offset, null, null, false);
        e.Handled = true;
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => SelectRelative(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => SelectRelative(1);

    private void SelectRelative(int direction)
    {
        if (!_loaded || ItemsSource is not { Count: > 1 } items) return;
        _index = (_index + direction + items.Count) % items.Count;
        // Commit data and action targets together. The action bar is a persistent
        // sibling of Information and never participates in the content transition.
        RenderSelection();
        AnimateEntrance(direction);
    }

    private void ResetContentMotion()
    {
        ResetMotion(ArtistModel);
        ResetMotion(Information);
        ResetMotion(TrackArea);
        ResetMotion(PortraitOrbit);
    }

    private void AnimateEntrance(int direction)
    {
        ResetMotion(ArtistModel);
        ResetMotion(Information);
        ResetMotion(TrackArea);
        if (!_loaded || !_settings.AnimationsEnabled) return;
        Reveal(ArtistModel, new Vector3(direction * 24, 0, 0), 420);
        Reveal(Information, new Vector3(direction * 10, 8, 0), 330);
        Reveal(TrackArea, new Vector3(direction * 12, 4, 0), 360);
    }

    private static Visual TranslationVisual(FrameworkElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (visual.Properties.TryGetVector3("Translation", out _) != CompositionGetValueStatus.Succeeded)
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        return visual;
    }

    private static void Reveal(FrameworkElement element, Vector3 from, int milliseconds)
    {
        var visual = TranslationVisual(element);
        using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 0.85f), Vector2.One);
        using var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, from);
        slide.InsertKeyFrame(1, Vector3.Zero, easing);
        slide.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.Properties.StartAnimation("Translation", slide);
        visual.Opacity = 0;
        Fade(element, 1, milliseconds);
    }

    private static void Fade(FrameworkElement element, float opacity, int milliseconds)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        using var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, opacity);
        fade.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation("Opacity", fade);
    }

    private void ResetModel_Click(object sender, RoutedEventArgs e) => ArtistModel.ResetView();

    private static void ResetMotion(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
        if (visual.Properties.TryGetVector3("Translation", out _) != CompositionGetValueStatus.Succeeded) return;
        visual.Properties.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }
}
