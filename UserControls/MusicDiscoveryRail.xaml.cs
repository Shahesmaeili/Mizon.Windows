using Mizon.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Controls;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class MusicDiscoveryRail : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IReadOnlyList<DiscoveryMusicItemViewModel>), typeof(MusicDiscoveryRail),
        new PropertyMetadata(null, ItemsChanged));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(MusicDiscoveryRail), new PropertyMetadata(false, PresentationChanged));

    private readonly HashSet<DiscoveryCardSurface> _realized = [];
    private ProfilePreviewPopup? _profilePopup;
    private DiscoveryMusicItemViewModel? _profileMusic;
    private static readonly UISettings Settings = new();
    private bool _initialized;
    private bool _loaded;
    private double _cardWidth = 224;
    private long _lastWheel;
    private double _wheelTarget;

    public MusicDiscoveryRail()
    {
        InitializeComponent();
        _initialized = true;
    }

    public IReadOnlyList<DiscoveryMusicItemViewModel>? ItemsSource
    {
        get => (IReadOnlyList<DiscoveryMusicItemViewModel>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public event EventHandler<DiscoveryMusicItemViewModel>? MusicRequested;

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private static void PresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var rail = (MusicDiscoveryRail)sender;
        if (!rail._initialized) return;
        rail.CloseProfile();
        rail.ResetInteraction();
        rail._cardWidth = rail.IsCompact ? 300 : 224;
        rail.MusicItems.ItemTemplate = (DataTemplate)rail.Resources[rail.IsCompact ? "RecentMusicCard" : "MusicCard"];
        rail.UpdateCardWidth(rail.RailScroll.ActualWidth);
    }

    private static void ItemsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var rail = (MusicDiscoveryRail)sender;
        if (!rail._initialized) return;
        rail.CloseProfile();
        rail.ResetInteraction();
        rail.Bindings.Update();
        rail._lastWheel = 0;
    }

    private void Rail_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        Bindings.Update();
        RailScroll.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(Rail_Wheel), true);
        UpdateCardWidth(RailScroll.ActualWidth);
        ResetInteraction();
    }

    private void Rail_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!_loaded || IsLoaded) return;
        _loaded = false;
        Bindings.StopTracking();
        RailScroll.RemoveHandler(PointerWheelChangedEvent, new PointerEventHandler(Rail_Wheel));
        DisposeProfilePopup();
        ResetInteraction();
        _lastWheel = 0;
    }

    private void Music_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not DiscoveryCardSurface button) return;
        _realized.Add(button);
        button.Width = _cardWidth;
        button.ResetInteraction();
    }

    private void Music_Clearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not DiscoveryCardSurface button) return;
        if (ReferenceEquals(button.Tag, _profileMusic)) CloseProfile();
        button.ResetInteraction();
        _realized.Remove(button);
    }

    public void ResetInteraction(bool animate = false)
    {
        foreach (var button in _realized) button.ResetInteraction(animate);
    }

    private void Rail_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Width != args.PreviousSize.Width) UpdateCardWidth(args.NewSize.Width);
    }

    private void UpdateCardWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        // Resizing wider than the card needs no per-item layout changes.
        var next = Math.Min(IsCompact ? 300 : 224, width);
        if (Math.Abs(next - _cardWidth) < .5) return;
        _cardWidth = next;
        _lastWheel = 0;
        foreach (var button in _realized)
        {
            button.ResetInteraction();
            button.Width = next;
        }
    }

    private void Rail_ViewChanging(object sender, ScrollViewerViewChangingEventArgs args)
    {
        CloseProfile();
        ResetInteraction(true);
    }

    private void Rail_Wheel(object sender, PointerRoutedEventArgs args)
    {
        var properties = args.GetCurrentPoint(RailScroll).Properties;
        if (properties.IsHorizontalMouseWheel || properties.MouseWheelDelta == 0 || RailScroll.ScrollableWidth <= 0
            || args.KeyModifiers.HasFlag(global::Windows.System.VirtualKeyModifiers.Control)) return;
        var now = Environment.TickCount64;
        var offset = now - _lastWheel > 350 ? RailScroll.HorizontalOffset : _wheelTarget;
        _lastWheel = now;
        _wheelTarget = Math.Clamp(offset - properties.MouseWheelDelta / 120d * 160, 0, RailScroll.ScrollableWidth);
        if (Math.Abs(RailScroll.HorizontalOffset - _wheelTarget) < .5) return;
        RailScroll.ChangeView(_wheelTarget, null, null, !Settings.AnimationsEnabled);
        args.Handled = true;
    }

    private void Music_Click(object sender, RoutedEventArgs args)
    {
        CloseProfile();
        ResetInteraction();
        if (sender is FeatureButton { Tag: DiscoveryMusicItemViewModel item }) MusicRequested?.Invoke(this, item);
    }

    private void Profile_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not FeatureButton { Tag: DiscoveryContributorItemViewModel person } anchor) return;
        // The group owns the music identity; a portrait action must never bubble
        // into the independent music button or use a person's ID as a client's ID.
        DiscoveryMusicItemViewModel? music = null;
        for (DependencyObject? parent = anchor; parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is DiscoveryCardSurface { Tag: DiscoveryMusicItemViewModel item })
            {
                music = item;
                break;
            }
        }
        if (music is null) return;
        ResetInteraction();
        if (_profilePopup is null)
        {
            _profilePopup = new ProfilePreviewPopup();
            _profilePopup.ActionRequested += Profile_ActionRequested;
            _profilePopup.Closed += Profile_Closed;
        }
        _profileMusic = music;
        _profilePopup.ShowAt(anchor, new ProfilePreviewViewModel
        {
            PublicId = person.PersonPublicId,
            Kind = ProfilePreviewKind.Person,
            DisplayName = person.DisplayName,
            SubtitleResourceKey = "MusicHome/Singer",
            SubtitleSuffix = music.GenreTitle,
            PersianSubtitleSuffix = music.PersianGenreTitle,
            Avatar = person.ProfileImage,
            Banner = music.CoverImage,
            DetailResourceKey = "MusicHome/InTrack",
            DetailArgument = music.Title,
            PersianDetailArgument = music.PersianTitle,
            FooterResourceKey = music.PublicId == Guid.Empty ? "MusicHome/Preview" : null,
            PrimaryActionResourceKey = music.PublicId == Guid.Empty ? null : "MusicHome/ViewTrack",
            PrimaryActionSymbol = FluentIcons.Common.Symbol.MusicNote1
        });
    }

    private void Profile_ActionRequested(object? sender, ProfilePreviewActionEventArgs args)
    {
        if (args.Action == ProfilePreviewAction.Primary && _profileMusic is { PublicId: var id } item && id != Guid.Empty)
            MusicRequested?.Invoke(this, item);
    }

    private void Profile_Closed(object? sender, EventArgs args) => _profileMusic = null;

    private void CloseProfile()
    {
        _profileMusic = null;
        _profilePopup?.Hide();
    }

    private void DisposeProfilePopup()
    {
        _profileMusic = null;
        if (_profilePopup is not { } popup) return;
        _profilePopup = null;
        popup.ActionRequested -= Profile_ActionRequested;
        popup.Closed -= Profile_Closed;
        popup.Dispose();
    }
}
