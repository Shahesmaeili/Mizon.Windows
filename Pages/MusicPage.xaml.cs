using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class MusicPage : Page
{
    public LocalizationService Localization => LocalizationService.Current;

    public MusicPageViewModel ViewModel { get; }
    public MusicDiscoveryViewModel Discovery { get; } = new();
    public MusicLibraryViewModel Library { get; }
    public MusicFeaturesViewModel? Features { get; private set; }
    public FollowingActivitiesViewModel? FollowingActivities { get; private set; }
    private UserControls.StoryViewer? _storyViewer;
    private MainWindow? _storyWindow;
    private Button? _storySource;
    private FollowingActivityStoryPresenter? _followingStoryPresenter;
    private string? _layoutState;
    private bool _dialogOpen;
    private bool _homeWidgetsActive;
    private readonly Microsoft.UI.Xaml.Media.RectangleGeometry _pageViewportClip = new();
    private readonly global::Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private sealed class RailState { public long LastWheelTime; public double Target; }
    private readonly ConditionalWeakTable<ScrollViewer, RailState> _rails = new();
    private readonly HashSet<UserControls.TopArtistCard> _artistCards = [];
    private double _artistCardWidth = 224;

    public MusicPage()
    {
        ViewModel = ((App)Application.Current).Services.GetRequiredService<MusicPageViewModel>();
        var services = ((App)Application.Current).Services;
        // The cached page owns its bounded library state; the root DI scope must not retain it.
        Library = new(services.GetRequiredService<Mizon.API.MizonApi>(),
            services.GetRequiredService<Mizon.API.Client.Operations.Media.MediaDownloader>());
        FollowingActivities = new(services.GetRequiredService<Mizon.API.MizonApi>(),
            services.GetRequiredService<Mizon.API.Client.Operations.Media.MediaDownloader>(), "music", 4);
        Features = new(services.GetRequiredService<Mizon.API.MizonApi>(),
            services.GetRequiredService<Mizon.API.Client.Operations.Media.MediaDownloader>());
        InitializeComponent();
        // Let the hero shadow cross column boundaries, while keeping scrolled content inside the page.
        LayoutRoot.Clip = _pageViewportClip;
        DataContext = ViewModel;
        FeaturedArtist.ItemsSource = Discovery.ArtistSpotlights;
    }

    private async void MusicPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateViewportClip(ActualWidth, ActualHeight);
        UpdateLayout(ActualWidth);
        if (_homeWidgetsActive) return;
        _homeWidgetsActive = true;
        ActivateMcpHome();
        Localization.PropertyChanged -= MusicHomeLocalizationChanged;
        Localization.PropertyChanged += MusicHomeLocalizationChanged;
        RefreshHomeLanguage();
        Bindings.Update();
        if (Features is null)
        {
            var services = ((App)Application.Current).Services;
            Features = new(services.GetRequiredService<Mizon.API.MizonApi>(),
                services.GetRequiredService<Mizon.API.Client.Operations.Media.MediaDownloader>());
            Bindings.Update();
            FeaturedMediaHost.ResetPresentation();
        }
        if (FollowingActivities is null)
        {
            var services = ((App)Application.Current).Services;
            FollowingActivities = new(services.GetRequiredService<Mizon.API.MizonApi>(),
                services.GetRequiredService<Mizon.API.Client.Operations.Media.MediaDownloader>(), "music", 4);
            Bindings.Update();
            FollowingActivitiesHost.ResetPresentation();
        }
        await Task.WhenAll(ViewModel.RefreshHomeWidgetsAsync(), Library.RefreshAsync(), FollowingActivities.RefreshAsync(), Features.RefreshAsync());
    }

    private void MusicPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) return;
        _homeWidgetsActive = false;
        DeactivateMcpHome();
        Localization.PropertyChanged -= MusicHomeLocalizationChanged;
        CloseStory();
        _followingStoryPresenter?.Dispose();
        _followingStoryPresenter = null;
        ViewModel.CancelHomeWidgetLoading();
        Discovery.CancelLoading();
        Library.CancelLoading();
        FollowingActivities?.Dispose();
        FollowingActivities = null;
        Features?.Dispose();
        Features = null;
        // This page is cached. Release account data and compiled-binding subscriptions
        // on departure; reconstruct the small feed only when this page returns.
        Bindings.Update();
        Bindings.StopTracking();
    }

    private void MusicHomeLocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_homeWidgetsActive && args.PropertyName == nameof(LocalizationService.Revision)) RefreshHomeLanguage();
    }

    private void RefreshHomeLanguage()
    {
        Discovery.RefreshLocalization();
        ViewModel.RefreshHomeLocalization();
        Features?.RefreshLocalization();
        _refreshPreviewDialog?.Invoke();
    }

    private async void Library_RefreshRequested(object? sender, EventArgs e) => await Library.RefreshAsync();

    private void Library_RecentRequested(object? sender, EventArgs e) => ScrollTo(RecentSection);

    private void MusicPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportClip(e.NewSize.Width, e.NewSize.Height);
        if (e.NewSize.Width != e.PreviousSize.Width) UpdateLayout(e.NewSize.Width);
    }

    private void UpdateViewportClip(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;
        var bounds = new global::Windows.Foundation.Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
        if (_pageViewportClip.Rect != bounds) _pageViewportClip.Rect = bounds;
    }

    private void UpdateLayout(double width)
    {
        if (LayoutRoot is null || !double.IsFinite(width) || width <= 0) return;
        // Use the page's actual width, including when hosted in a narrower navigation frame.
        var state = width >= 1440 ? "WideLayout" : width >= 1040 ? "MediumLayout" : "NarrowLayout";
        if (_layoutState == state) return;
        if (!VisualStateManager.GoToState(this, state, false)) return;

        // Only visible columns own a viewport; collapsed columns join the main feed.
        MoveRail(EditorialRail, EditorialScroll, InlineEditorialHost, state == "WideLayout");
        MoveRail(CommunityRail, CommunityScroll, InlineCommunityHost, state != "NarrowLayout");
        _layoutState = state;
    }

    private static void MoveRail(StackPanel rail, ScrollViewer column, Border inlineHost, bool separateColumn)
    {
        inlineHost.Visibility = separateColumn ? Visibility.Collapsed : Visibility.Visible;
        if (separateColumn)
        {
            if (ReferenceEquals(column.Content, rail)) return;
            inlineHost.Child = null;
            column.Content = rail;
        }
        else
        {
            if (ReferenceEquals(inlineHost.Child, rail)) return;
            column.Content = null;
            inlineHost.Child = rail;
        }
    }

    private void MainFeed_ViewChanging(object sender, ScrollViewerViewChangingEventArgs args)
    {
        RecommendedRail?.ResetInteraction(animate: true);
        RecentRail?.ResetInteraction(animate: true);
    }

    private void HorizontalRail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer rail || _rails.TryGetValue(rail, out _)) return;
        _rails.Add(rail, new RailState());
        rail.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(HorizontalRail_Wheel), true);
    }

    private void HorizontalRail_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer rail || rail.IsLoaded || !_rails.TryGetValue(rail, out _)) return;
        rail.RemoveHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(HorizontalRail_Wheel));
        _rails.Remove(rail);
    }

    private void HorizontalRail_Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer rail || !_rails.TryGetValue(rail, out var state)) return;
        var properties = e.GetCurrentPoint(rail).Properties;
        if (properties.IsHorizontalMouseWheel || properties.MouseWheelDelta == 0 ||
            e.KeyModifiers.HasFlag(global::Windows.System.VirtualKeyModifiers.Control) || rail.ScrollableWidth <= 0) return;
        var now = Environment.TickCount64;
        var offset = now - state.LastWheelTime > 350 ? rail.HorizontalOffset : state.Target;
        state.Target = Math.Clamp(offset - properties.MouseWheelDelta / 120d * 160, 0, rail.ScrollableWidth);
        state.LastWheelTime = now;
        if (Math.Abs(rail.HorizontalOffset - state.Target) < 0.5) return;
        var animations = _uiSettings.AnimationsEnabled;
        rail.ChangeView(state.Target, null, null, !animations);
        e.Handled = true;
    }

    private async void Stories_RefreshRequested(object? sender, EventArgs args) => await ViewModel.RefreshHomeWidgetsAsync();

    private async void Stories_AddRequested(object? sender, EventArgs args) => await ShowPreviewDialogAsync(Localization.GetString("MusicHome/NewStory"),
        new TextBlock { Text = Localization.GetString("MusicHome/StoryPreviewExplanation"), TextWrapping = TextWrapping.Wrap, FlowDirection = Localization.FlowDirection },
        dialog =>
        {
            dialog.Title = Localization.GetString("MusicHome/NewStory");
            if (dialog.Content is TextBlock text) { text.Text = Localization.GetString("MusicHome/StoryPreviewExplanation"); text.FlowDirection = Localization.FlowDirection; }
        });

    private async void Stories_StoryRequested(object? sender, UserControls.StoryRequestedEventArgs args)
    {
        if (!_homeWidgetsActive || App.MainMizonWindow is not MainWindow window) return;
        var stories = new List<StoryItemViewModel>(ViewModel.HomeStories);
        var index = stories.IndexOf(args.Story);
        if (index < 0) return;
        CloseStory();
        var preview = (args.Source.Content as FrameworkElement)?.FindName("CardArtwork") as Image;
        var transition = MusicCoverTransition.Prepare(preview);
        var transitionHandedOff = false;
        var viewer = new UserControls.StoryViewer();
        _storyViewer = viewer;
        _storyWindow = window;
        _storySource = args.Source;
        viewer.CloseRequested += Story_Closed;
        try
        {
            // Capture the live thumbnail before the full-window overlay disables
            // the page. The viewer owns decoding and the destination animation.
            window.ShowWindowOverlay(viewer);
            transitionHandedOff = true;
            await viewer.OpenWithTransitionAsync(ViewModel, stories, index, transition, System.Threading.CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("Home story could not be opened.", exception);
            if (ReferenceEquals(_storyViewer, viewer)) CloseStory(restoreFocus: true);
        }
        finally
        {
            if (!transitionHandedOff) transition?.Dispose();
        }
    }

    private void Story_Closed(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, _storyViewer)) CloseStory(restoreFocus: true);
    }

    private void CloseStory(bool restoreFocus = false)
    {
        var viewer = _storyViewer;
        var window = _storyWindow;
        var source = _storySource;
        _storyViewer = null;
        _storyWindow = null;
        _storySource = null;
        if (viewer is null) return;
        viewer.CloseRequested -= Story_Closed;
        try { viewer.Close(); }
        finally { window?.HideWindowOverlay(viewer); }
        if (restoreFocus && _homeWidgetsActive && source is { IsLoaded: true }) source.Focus(FocusState.Programmatic);
    }

    private async void Discovery_MusicRequested(object? sender, DiscoveryMusicItemViewModel item) =>
        await OpenMusicAsync(item);

    private async void FeatureHero_OpenMusicRequested(object? sender, DiscoveryMusicItemViewModel item) =>
        await OpenMusicAsync(item);

    private void MainFeature_OpenMusicRequested(object? sender, MusicFeatureItemViewModel item) =>
        ViewModel.NavigationService.InternalNavigate(typeof(MusicsPage), new()
        {
            [PageConstants.MUSIC_ID] = item.MusicPublicId
        });

    private async void MainFeature_PlayMusicRequested(object? sender, MusicFeatureItemViewModel item)
    {
        MainFeature_OpenMusicRequested(sender, item);
        try
        {
            // Explicit play survives leaving this home page. The existing music player
            // owns cancellation when a newer music activation supersedes this request.
            await ViewModel.ActivateMusicAsync(item.MusicPublicId, MusicActivationAction.Play, System.Threading.CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Featured music playback could not be started.", exception); }
    }

    private async void TrendingMusic_OpenMusicRequested(object? sender, DiscoveryMusicItemViewModel item) =>
        await OpenMusicAsync(item);

    private async void FriendActivity_OpenRequested(object? sender, FollowingActivityRequestedEventArgs args) =>
        await FollowingActivityNavigation.OpenAsync(args.Activity, ViewModel.NavigationService,
            () => _followingStoryPresenter ??= new(), args.CoverSource);

    private void FriendActivity_MoreRequested(object? sender, EventArgs args) =>
        ViewModel.NavigationService.OpenFollowingActivities();

    private async void FriendActivity_RefreshRequested(object? sender, EventArgs args)
    {
        if (FollowingActivities is { IsLoading: false } activities) await activities.RefreshAsync();
    }
    private async Task OpenMusicAsync(DiscoveryMusicItemViewModel item)
    {
        if (item.PublicId != Guid.Empty)
        {
            ViewModel.NavigationService.InternalNavigate(typeof(MusicsPage), new()
            {
                [PageConstants.MUSIC_ID] = item.PublicId
            });
            return;
        }
        var panel = new StackPanel { Spacing = 12, MaxWidth = 360 };
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12),
            Child = new Image { Source = item.CoverImage, Height = 240, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill }
        });
        panel.Children.Add(new TextBlock { Text = item.DisplayTitle, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = item.Artist, Opacity = 0.65 });
        panel.Children.Add(new TextBlock { Text = Localization.Format("MusicHome/PreviewDurationLikes", item.DurationText, item.LikesCountText), Opacity = 0.6 });
        panel.Children.Add(new TextBlock { Text = Localization.GetString("MusicHome/TrackPreviewExplanation"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.55, FlowDirection = Localization.FlowDirection });
        await ShowPreviewDialogAsync(item.DisplayTitle, panel, dialog =>
        {
            dialog.Title = item.DisplayTitle;
            ((TextBlock)panel.Children[1]).Text = item.DisplayTitle;
            ((TextBlock)panel.Children[3]).Text = Localization.Format("MusicHome/PreviewDurationLikes", item.DurationText, item.LikesCountText);
            var description = (TextBlock)panel.Children[4];
            description.Text = Localization.GetString("MusicHome/TrackPreviewExplanation");
            description.FlowDirection = Localization.FlowDirection;
        });
    }

    private Action? _refreshPreviewDialog;
    private async Task ShowPreviewDialogAsync(string title, UIElement content, Action<ContentDialog>? refresh = null)
    {
        if (_dialogOpen || LatestNews.McpDialogOpen || XamlRoot is null) return;
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
                FlowDirection = Localization.FlowDirection, FontFamily = Localization.TextFontFamily, Language = Localization.LanguageTag,
                Title = title, Content = content, CloseButtonText = Localization.GetString("MusicHome/Close")
            };
            _mcpPreviewDialog = dialog;
            _refreshPreviewDialog = () =>
            {
                dialog.FlowDirection = Localization.FlowDirection;
                dialog.FontFamily = Localization.TextFontFamily;
                dialog.Language = Localization.LanguageTag;
                dialog.CloseButtonText = Localization.GetString("MusicHome/Close");
                refresh?.Invoke(dialog);
            };
            _refreshPreviewDialog();
            await dialog.ShowAsync();
        }
        catch (Exception exception) { AppLog.Error("Home preview could not be opened.", exception); }
        finally { _refreshPreviewDialog = null; _mcpPreviewDialog = null; _dialogOpen = false; }
    }

    private async void TopArtist_ProfileRequested(object? sender, TopArtistItemViewModel artist)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 360 };
        panel.Children.Add(new PersonPicture
        {
            DisplayName = artist.DisplayName, ProfilePicture = artist.AvatarImage,
            Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock { Text = artist.GenreTitle, TextAlignment = TextAlignment.Center, Opacity = 0.6 });
        panel.Children.Add(new TextBlock
        {
            Text = artist.FollowersFullText + "  ·  " + artist.MonthlyListenersFullText,
            FlowDirection = Localization.FlowDirection, TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        panel.Children.Add(new TextBlock { Text = Localization.GetString("MusicHome/ArtistTracks"), FlowDirection = Localization.FlowDirection, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var tracks = Discovery.GetArtistMusics(artist).ToArray();
        foreach (var music in tracks)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new Grid
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(6),
                Children = { new Image { Source = music.CoverImage, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill } }
            });
            row.Children.Add(new TextBlock { Text = music.Title, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            panel.Children.Add(row);
        }
        panel.Children.Add(new TextBlock
        {
            Text = Localization.GetString("MusicHome/ArtistPreviewExplanation"),
            TextWrapping = TextWrapping.Wrap, FlowDirection = Localization.FlowDirection, FontSize = 10, Opacity = 0.55
        });
        await ShowPreviewDialogAsync(artist.DisplayName, panel, dialog =>
        {
            ((TextBlock)panel.Children[1]).Text = artist.GenreTitle;
            var counts = (TextBlock)panel.Children[2];
            counts.Text = artist.FollowersFullText + "  ·  " + artist.MonthlyListenersFullText;
            counts.FlowDirection = Localization.FlowDirection;
            var heading = (TextBlock)panel.Children[3];
            heading.Text = Localization.GetString("MusicHome/ArtistTracks");
            heading.FlowDirection = Localization.FlowDirection;
            for (var index = 0; index < tracks.Length; index++)
                ((TextBlock)((StackPanel)panel.Children[4 + index]).Children[1]).Text = tracks[index].DisplayTitle;
            var description = (TextBlock)panel.Children[panel.Children.Count - 1];
            description.Text = Localization.GetString("MusicHome/ArtistPreviewExplanation");
            description.FlowDirection = Localization.FlowDirection;
        });
    }

    private void TopArtistsScroll_Loaded(object sender, RoutedEventArgs e)
    {
        HorizontalRail_Loaded(sender, e);
        UpdateArtistCardWidth(TopArtistsScroll.ActualWidth);
        UpdateArtistNavigation();
    }

    private void TopArtistsScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width) UpdateArtistCardWidth(e.NewSize.Width);
        UpdateArtistNavigation();
    }

    private void UpdateArtistCardWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var next = Math.Min(224, width);
        if (Math.Abs(next - _artistCardWidth) < .5) return;
        _artistCardWidth = next;
        foreach (var card in _artistCards) card.Width = next;
    }

    private void Artist_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not UserControls.TopArtistCard card) return;
        _artistCards.Add(card);
        card.Width = _artistCardWidth;
    }

    private void Artist_Clearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is UserControls.TopArtistCard card) _artistCards.Remove(card);
    }
    private void TopArtistsScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateArtistNavigation();
    private void PreviousArtists_Click(object sender, RoutedEventArgs e) => ScrollArtists(-1);
    private void NextArtists_Click(object sender, RoutedEventArgs e) => ScrollArtists(1);

    private void ScrollArtists(int direction)
    {
        var cardStep = _artistCardWidth + 12;
        var step = cardStep * Math.Max(1, Math.Floor(TopArtistsScroll.ViewportWidth / cardStep));
        var now = Environment.TickCount64;
        var current = TopArtistsScroll.HorizontalOffset;
        if (_rails.TryGetValue(TopArtistsScroll, out var state))
        {
            if (now - state.LastWheelTime <= 350) current = state.Target;
            state.Target = Math.Clamp(current + direction * step, 0, TopArtistsScroll.ScrollableWidth);
            state.LastWheelTime = now;
        }
        var offset = Math.Clamp(current + direction * step, 0, TopArtistsScroll.ScrollableWidth);
        TopArtistsScroll.ChangeView(offset, null, null, !_uiSettings.AnimationsEnabled);
    }

    private void UpdateArtistNavigation()
    {
        if (TopArtistsScroll is null || PreviousArtistsButton is null || NextArtistsButton is null) return;
        PreviousArtistsButton.IsEnabled = TopArtistsScroll.HorizontalOffset > 0.5;
        NextArtistsButton.IsEnabled = TopArtistsScroll.HorizontalOffset < TopArtistsScroll.ScrollableWidth - 0.5;
    }

    private async void Genres_RefreshRequested(object? sender, EventArgs args) => await ViewModel.RefreshHomeWidgetsAsync();

    private void GoRecent_Click(object sender, RoutedEventArgs e) => ScrollTo(RecentSection);
    private void ScrollTo(FrameworkElement section)
    {
        section.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = _uiSettings.AnimationsEnabled,
            VerticalAlignmentRatio = 0, VerticalOffset = -12
        });
    }
}

