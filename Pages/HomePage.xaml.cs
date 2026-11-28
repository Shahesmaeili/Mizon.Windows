using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;

namespace Mizon.Windows.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class HomePage : Page
    {
        public HomePageViewModel ViewModel { get; }
        private UserControls.ProfileMenu? _profileMenu;
        private Common.MenuFlyoutMotion? _profileMenuMotion;
        private CancellationTokenSource? _searchCancellationTokenSource;
        private InputNonClientPointerSource? _titleBarPointerSource;
        private XamlRoot? _titleBarRoot;
        private AppWindow? _titleBarWindow;
        private RectInt32[] _titleBarInteractiveRects = [];
        private bool _titleBarRegionUpdateQueued;
        private bool _titleBarRegionFailureLogged;
        private AppWindowPresenterKind? _lastTitleBarPresenter;
        private int _titleBarGeneration;
        private ConnectedAnimation? _profileConnectedAnimation;
        private DispatcherQueueTimer? _profileEntranceDeadline;
        private RectInt32? _profileEntranceBounds;
        private bool _internalFrameInitialized;
        private MusicPage? _retainedMusicPage;
        private bool _synchronizingNavigationSelection;
        private bool _resetMusicLanding = true;
        private CancellationTokenSource? _musicActivationCancellation;
        private Task _musicActivationTask = Task.CompletedTask;
        private FollowingActivityStoryPresenter? _activationStoryPresenter;
        private bool _navigatingActivation;
        internal bool CanOpenDestination => IsLoaded && _internalFrameInitialized && !ViewModel.IsSigningOut;
        internal Guid? ActiveStoryPublicId => _activationStoryPresenter?.CurrentStoryPublicId;
        internal string? ActiveSearchQuery => SearchBox.ActiveQuery;

        public HomePage()
        {
            ViewModel = ((App)App.Current).Services.GetRequiredService<HomePageViewModel>();
            this.InitializeComponent();
            NearbyDevicePicker.ViewModel = ViewModel;
            if (App.MainMizonWindow is { } window && window.Content?.XamlRoot is { RasterizationScale: > 0 } root)
                SetRightCaptionInset(GetRightCaptionInset(window.AppWindow, root.RasterizationScale));
            DataContext = ViewModel;

            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;
            NavigationViewMain.ItemInvoked += NavigationViewMain_ItemInvoked;

        }

        private void AccountFlyout_Opening(object sender, object args)
        {
            // Build the popup only on first use; the welcome/login path pays no UI cost.
            if (_profileMenu is null)
            {
                _profileMenu = new UserControls.ProfileMenu { ViewModel = ViewModel };
                _profileMenu.DismissRequested += ProfileMenu_DismissRequested;
                _profileMenu.DismissAsync = DismissProfileMenuAsync;
                _profileMenuMotion = new Common.MenuFlyoutMotion(AccountFlyout, AccountMenuHost);
                AccountMenuHost.Tag = _profileMenu;
            }
            var size = XamlRoot?.Size ?? new Size(ActualWidth, ActualHeight);
            _profileMenu.Width = Math.Clamp(size.Width - 40, 1, 344);
            _profileMenu.MaxHeight = Math.Max(80, size.Height - 80);
            _profileMenuMotion?.PrepareOpen();
        }

        private void ProfileMenu_DismissRequested(object? sender, EventArgs args) => AccountFlyout.Hide();

        private async Task DismissProfileMenuAsync()
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnClosed(object? sender, object args) => closed.TrySetResult();
            AccountFlyout.Closed += OnClosed;
            try
            {
                AccountFlyout.Hide();
                // MenuFlyoutMotion finishes its exit animation before closing.
                // Only then give the foreground to the native pin confirmation.
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally { AccountFlyout.Closed -= OnClosed; }
        }

        private void AccountFlyout_Opened(object sender, object args) => _profileMenu?.FocusFirstAction();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            // A new sign-in starts at discovery; reloading the cached shell keeps its current page.
            _resetMusicLanding = e.NavigationMode == NavigationMode.New;
        }

        private void NavigationViewMain_Loaded(object sender, RoutedEventArgs e)
        {
            NavigationViewMain.ApplyTemplate();
            ApplyNavigationBackButtonTemplate(NavigationViewMain);
        }

        private bool ApplyNavigationBackButtonTemplate(DependencyObject root)
        {
            if (root is Button { Name: "NavigationViewBackButton" } button)
            {
                // Assign the actual button template, not a resource shadowed by
                // WinUI's already-resolved default NavigationView style.
                button.Template = (ControlTemplate)Resources["MizonNavigationBackButtonTemplate"];
                return true;
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                if (ApplyNavigationBackButtonTemplate(VisualTreeHelper.GetChild(root, index))) return true;
            return false;
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeTitleBarPointerRegions();
            QueueTitleBarRegionUpdate();

            StartProfileConnectedAnimation();

            var navigationService = ((App)App.Current).Services.GetRequiredService<NavigationService>();
            if (!_internalFrameInitialized)
            {
                navigationService.InternalInitialize(InternalFrame);
                _internalFrameInitialized = true;
            }
            navigationService.InternalNavigationChanged -= ActivationNavigationChanged;
            navigationService.InternalNavigationChanged += ActivationNavigationChanged;
            _retainedMusicPage ??= InternalFrame.Content as MusicPage;

            if (!App.TryOpenPendingContent() && (_resetMusicLanding || InternalFrame.Content is null)
                && InternalFrame.Content is not MusicPage)
                navigationService.InternalNavigate(typeof(MusicPage));

            SynchronizeNavigationSelection();
            _resetMusicLanding = false;
        }

        private void SynchronizeNavigationSelection()
        {
            var tag = InternalFrame.Content switch
            {
                MusicPage => nameof(MusicPage),
                SectionLandingPage { Destination: "movies" } => "MoviesPage",
                SectionLandingPage { Destination: "apps" } => "AppsPage",
                VPNPage => nameof(VPNPage),
                _ => null
            };
            var selected = tag is null ? null : NavigationViewMain.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => item.Tag as string == tag);
            if (ReferenceEquals(NavigationViewMain.SelectedItem, selected)) return;

            _synchronizingNavigationSelection = true;
            try { NavigationViewMain.SelectedItem = selected; }
            finally { _synchronizingNavigationSelection = false; }
        }

        private void StartProfileConnectedAnimation()
        {
            _profileConnectedAnimation = ConnectedAnimationService.GetForCurrentView()
                .GetAnimation(WelcomePage.ProfileConnectedAnimationKey);
            if (_profileConnectedAnimation is null) return;

            // Keep the destination measured while native caption metrics settle.
            // The prepared animation owns the visual hand-off during this brief gap.
            CurrentProfileAvatar.ConnectedAnimationTarget.Opacity = 0;
            _profileEntranceDeadline = DispatcherQueue.CreateTimer();
            _profileEntranceDeadline.Interval = TimeSpan.FromMilliseconds(300);
            _profileEntranceDeadline.IsRepeating = false;
            _profileEntranceDeadline.Tick += ProfileEntranceDeadline_Tick;
            _profileEntranceDeadline.Start();
            QueueTitleBarRegionUpdate();
        }

        private void ProfileEntranceDeadline_Tick(DispatcherQueueTimer sender, object args) =>
            ReleaseProfileEntrance()?.Cancel();

        private void TryStartProfileConnectedAnimation()
        {
            var target = CurrentProfileAvatar.ConnectedAnimationTarget;
            if (_profileConnectedAnimation is null
                || Math.Abs(RightCaptionInset.ActualWidth - RightCaptionInset.Width.Value) > .75
                || Common.TitleBarRegions.GetPixelBounds(target) is not { } bounds)
                return;

            if (_profileEntranceBounds is not { } previous || !previous.Equals(bounds))
            {
                _profileEntranceBounds = bounds;
                QueueTitleBarRegionUpdate(DispatcherQueuePriority.Low);
                return;
            }

            var animation = ReleaseProfileEntrance()!;
            try
            {
                if (!animation.TryStart(target)) animation.Cancel();
            }
            catch (COMException exception)
            {
                animation.Cancel();
                AppLog.Error("Welcome profile transition could not be started.", exception);
            }
        }

        private ConnectedAnimation? ReleaseProfileEntrance()
        {
            if (_profileEntranceDeadline is { } timer)
            {
                timer.Stop();
                timer.Tick -= ProfileEntranceDeadline_Tick;
                _profileEntranceDeadline = null;
            }

            var animation = _profileConnectedAnimation;
            _profileConnectedAnimation = null;
            _profileEntranceBounds = null;
            if (animation is not null) CurrentProfileAvatar.ConnectedAnimationTarget.Opacity = 1;
            return animation;
        }

        private static double GetRightCaptionInset(AppWindow window, double scale) =>
            window.Presenter.Kind == AppWindowPresenterKind.FullScreen ? 0 : window.TitleBar.RightInset / scale;

        private bool SetRightCaptionInset(double width)
        {
            if (Math.Abs(RightCaptionInset.Width.Value - width) <= .01) return false;
            RightCaptionInset.Width = new GridLength(width);
            _profileEntranceBounds = null;
            return true;
        }

        private void InitializeTitleBarPointerRegions()
        {
            if (!IsLoaded || App.MainMizonWindow is null || _titleBarPointerSource is not null)
                return;

            _titleBarPointerSource = InputNonClientPointerSource.GetForWindowId(App.MainMizonWindow.AppWindow.Id);
            _titleBarRoot = XamlRoot;
            _titleBarWindow = App.MainMizonWindow.AppWindow;
            _titleBarWindow.Changed += TitleBarWindow_Changed;
            if (_titleBarRoot is { RasterizationScale: > 0 } root)
            {
                SetRightCaptionInset(GetRightCaptionInset(_titleBarWindow, root.RasterizationScale));
                root.Changed += TitleBarRoot_Changed;
            }
            DragAreaOverlayGrid.LayoutUpdated += TitleBar_LayoutUpdated;
            QueueTitleBarRegionUpdate();
        }

        private void TitleBarRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            _profileEntranceBounds = null;
            QueueTitleBarRegionUpdate();
        }
        private void TitleBar_LayoutUpdated(object? sender, object args) => QueueTitleBarRegionUpdate();
        private void TitleBarWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (!args.DidPresenterChange && !args.DidSizeChange) return;
            _profileEntranceBounds = null;
            QueueTitleBarRegionUpdate();
        }

        private void QueueTitleBarRegionUpdate(DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
        {
            if (_titleBarRegionUpdateQueued || _titleBarPointerSource is null || !IsLoaded) return;
            var generation = _titleBarGeneration;
            _titleBarRegionUpdateQueued = DispatcherQueue.TryEnqueue(priority, () =>
            {
                // A cached page may unload and attach to the window again before
                // this callback runs. The old lifetime must not update new regions.
                if (generation != _titleBarGeneration) return;
                _titleBarRegionUpdateQueued = false;
                UpdateTitleBarPointerRegions();
            });
        }

        private void UpdateTitleBarPointerRegions()
        {
            try
            {
                UpdateTitleBarPointerRegionsCore();
                _titleBarRegionFailureLogged = false;
            }
            catch (Exception exception) when (exception is COMException or ArgumentException or InvalidOperationException)
            {
                // WinRT dispatch can otherwise turn an escaping callback exception
                // into a stowed CoreMessaging failure without the original stack.
                if (!_titleBarRegionFailureLogged)
                    AppLog.Error($"Title-bar native regions failed, HRESULT=0x{exception.HResult:X8}.", exception);
                _titleBarRegionFailureLogged = true;
            }
        }

        private void UpdateTitleBarPointerRegionsCore()
        {
            if (_titleBarPointerSource is null || !IsLoaded || App.MainMizonWindow is null)
                return;
            if (App.MainMizonWindow is MainWindow { IsAvailable: false }) return;

            var window = App.MainMizonWindow.AppWindow;
            var titleBar = window.TitleBar;
            var presenter = window.Presenter.Kind;
            var fullScreen = presenter == AppWindowPresenterKind.FullScreen;
            var scale = XamlRoot?.RasterizationScale ?? 1;
            if (!double.IsFinite(scale) || scale <= 0) return;
            var rightInset = fullScreen ? 0 : titleBar.RightInset / scale;
            if (_lastTitleBarPresenter != presenter)
                AppLog.Information($"Title-bar native regions: presenter={presenter}, leftInset={titleBar.LeftInset}, rightInset={titleBar.RightInset}, scale={scale}, previousRegions={_titleBarInteractiveRects.Length}.");
            if (SetRightCaptionInset(rightInset))
            {
                // The new inset must be arranged before publishing hit-test bounds
                // or using the avatar as the connected animation destination.
                if (!fullScreen) return;
            }
            else TryStartProfileConnectedAnimation();

            // Fullscreen has no native caption region. Let all header controls and
            // the window's top-edge reveal surface receive normal client input.
            if (fullScreen)
            {
                if (_titleBarInteractiveRects.Length != 0)
                {
                    _titleBarPointerSource.ClearRegionRects(NonClientRegionKind.Passthrough);
                    _titleBarInteractiveRects = [];
                }
                RecordTitleBarPresenter(presenter);
                return;
            }

            // WinUI owns the native full-width Tall caption. Only register the
            // interactive holes: writing Caption here competes with its layout updates.
            var header = Common.TitleBarRegions.GetPixelBounds(DragAreaOverlay);
            if (header is not RectInt32 headerRect)
                return;
            var left = Math.Max(headerRect.X, titleBar.LeftInset);
            var right = Math.Max(left, headerRect.X + headerRect.Width - titleBar.RightInset);
            var captionRect = new RectInt32(left, headerRect.Y, right - left, headerRect.Height);
            var interactiveRects = Common.TitleBarRegions.GetInteractiveRects(NavigationViewMain, captionRect);

            if (!_titleBarInteractiveRects.SequenceEqual(interactiveRects))
            {
                _titleBarPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, interactiveRects);
                _titleBarInteractiveRects = interactiveRects;
            }
            RecordTitleBarPresenter(presenter);
        }

        private void RecordTitleBarPresenter(AppWindowPresenterKind presenter)
        {
            if (_lastTitleBarPresenter == presenter) return;
            _lastTitleBarPresenter = presenter;
            AppLog.Information($"Title-bar native regions: applied presenter={presenter}, regions={_titleBarInteractiveRects.Length}.");
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) return;
            NearbyDevicePicker.CloseFlyout();
            _retainedMusicPage?.EndHomeSession();
            _retainedMusicPage = null;
            _musicActivationCancellation?.Cancel();
            _musicActivationCancellation = null;
            ViewModel.NavigationService.InternalNavigationChanged -= ActivationNavigationChanged;
            _activationStoryPresenter?.Dispose();
            _activationStoryPresenter = null;
            _profileMenuMotion?.Dispose();
            _profileMenuMotion = null;
            AccountFlyout.Hide();
            if (_profileMenu is not null)
            {
                _profileMenu.DismissRequested -= ProfileMenu_DismissRequested;
                _profileMenu.Dispose();
                AccountMenuHost.Tag = null;
                _profileMenu = null;
            }
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource?.Dispose();
            _searchCancellationTokenSource = null;
            SearchBox.ClearSearch();
            ViewModel.MainSearch.Clear();
            ReleaseProfileEntrance()?.Cancel();

            DragAreaOverlayGrid.LayoutUpdated -= TitleBar_LayoutUpdated;
            if (_titleBarWindow is not null)
            {
                _titleBarWindow.Changed -= TitleBarWindow_Changed;
                _titleBarWindow = null;
            }
            if (_titleBarRoot is not null) _titleBarRoot.Changed -= TitleBarRoot_Changed;
            _titleBarRoot = null;
            _titleBarGeneration++;
            _titleBarRegionUpdateQueued = false;
            // Window teardown can close the native pointer source before Page.Unloaded.
            // Clearing the live window's hit-test regions is useful on navigation only.
            try { _titleBarPointerSource?.ClearRegionRects(NonClientRegionKind.Passthrough); }
            catch (ObjectDisposedException) { }
            catch (COMException exception) { AppLog.Error("Title-bar native regions were unavailable during unload.", exception); }
            _titleBarPointerSource = null;
            _titleBarInteractiveRects = [];
            _lastTitleBarPresenter = null;
            _titleBarRegionFailureLogged = false;
        }

        private void SearchShortcut_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
            Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = SearchBox.FocusInput();
        }

        private async void SearchBox_TextChanged(object? sender, string text)
        {
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource?.Dispose();
            var request = new CancellationTokenSource();
            _searchCancellationTokenSource = request;
            var cancellationToken = request.Token;
            var query = text.Trim();
            ViewModel.MainSearch.Clear();
            SearchBox.BeginSearch(query);
            if (query.Length < 2)
                return;

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
                AppLog.Information($"Search started. Query='{query}'.");
                await foreach (var item in ViewModel.SearchAsync(query, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(request, _searchCancellationTokenSource)) return;
                    ViewModel.MainSearch.Add(item);
                }
                if (ReferenceEquals(request, _searchCancellationTokenSource)) SearchBox.CompleteSearch();
                AppLog.Information($"Search completed. Query='{query}', ResultCount={ViewModel.MainSearch.Count}.");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (ReferenceEquals(request, _searchCancellationTokenSource)) SearchBox.CompleteSearch(failed: true);
                AppLog.Error($"Search request failed. Query='{query}'.", exception);
            }
        }

        private void SearchBox_ResultInvoked(object? sender, object item)
        {
            if (item is not MainSearchMusicItemViewModel && item is not MainSearchClientItemViewModel)
                return;

            _searchCancellationTokenSource?.Cancel();
            // Navigation only happens on activation, never on arrow-key selection.
            SearchBox.ClearSearch();
            ViewModel.MainSearch.Clear();
            if (item is MainSearchMusicItemViewModel music)
                ViewModel.NavigationService.InternalNavigate(typeof(MusicsPage), new() { [PageConstants.MUSIC_ID] = music.PublicId });
            else if (item is MainSearchClientItemViewModel client)
                ViewModel.NavigationService.OpenClientProfile(client.PublicId);
        }

        private void NavigationViewMain_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            // Native invocation also handles reselecting a section with keyboard or controller.
            if (!_internalFrameInitialized || _synchronizingNavigationSelection) return;
            if (args.InvokedItemContainer?.Tag is string tag) NavigateSection(tag);
        }

        // Used by protocol activation and the permission-gated MCP adapter alike.
        // No second page/view model is created to service an automation request.
        internal Task<bool> OpenDestinationAsync(AppActivationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanOpenDestination || !request.HasDestination) return Task.FromResult(false);
            _musicActivationCancellation?.Cancel();
            _activationStoryPresenter?.Dispose();
            _activationStoryPresenter = null;
            var previous = _musicActivationTask;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _musicActivationCancellation = cancellation;
            var task = OpenDestinationCoreAsync(request, previous, cancellation);
            _musicActivationTask = task;
            return task;
        }

        internal void CancelDestination()
        {
            _musicActivationCancellation?.Cancel();
            _activationStoryPresenter?.Dispose();
            _activationStoryPresenter = null;
        }

        private void ActivationNavigationChanged(object? sender, NavigationChangedEventArgs args)
        {
            if (args.IsEnteringPage)
            {
                if (InternalFrame.Content is MusicPage music) _retainedMusicPage = music;
                SynchronizeNavigationSelection();
                return;
            }
            if (!_navigatingActivation)
            {
                _musicActivationCancellation?.Cancel();
                _activationStoryPresenter?.Dispose();
                _activationStoryPresenter = null;
            }
        }

        private void NavigateActivation(Action navigate)
        {
            _navigatingActivation = true;
            try { navigate(); }
            finally { _navigatingActivation = false; }
        }

        private async Task<bool> OpenDestinationCoreAsync(AppActivationRequest request, Task previous, CancellationTokenSource cancellation)
        {
            var token = cancellation.Token;
            try
            {
                await previous.WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (!CanOpenDestination) return false;
                var navigation = ViewModel.NavigationService;
                if (request.SearchQuery is { } searchQuery)
                {
                    EnsureActivationLanding();
                    SearchBox.OpenSearch(searchQuery);
                    return true;
                }
                SearchBox.ClearSearch();
                if (request.PlaylistPublicId is Guid playlistId)
                {
                    NavigateActivation(() => navigation.OpenMusicPlaylist(playlistId));
                    return true;
                }
                if (request.PersonPublicId is Guid personId)
                {
                    NavigateActivation(() => navigation.OpenPersonProfile(personId));
                    return true;
                }
                if (request.ClientPublicId is Guid clientId)
                {
                    NavigateActivation(() => navigation.OpenClientProfile(clientId));
                    return true;
                }
                if (request.StoryPublicId is Guid storyId)
                {
                    EnsureActivationLanding();
                    var presenter = _activationStoryPresenter = new FollowingActivityStoryPresenter();
                    await presenter.OpenAsync(storyId, cancellationToken: token);
                    token.ThrowIfCancellationRequested();
                    if (presenter.CurrentStoryPublicId != storyId || presenter.ActiveViewer is not { } viewer) return false;
                    var ready = await viewer.WaitForPresentationAsync(token);
                    token.ThrowIfCancellationRequested();
                    return ready && presenter.CurrentStoryPublicId == storyId;
                }
                if (request.MusicPublicId is Guid musicId)
                {
                    if (request.MusicAction == MusicActivationAction.Video)
                    {
                        NavigateActivation(() => navigation.InternalNavigate(typeof(MusicVideoPage), new()
                        {
                            [PageConstants.MUSIC_ID] = musicId
                        }));
                        return navigation.CurrentInternalPage is MusicVideoPage video && video.MusicPublicId == musicId;
                    }

                    if (request.MusicCommentPublicId is Guid commentId)
                    {
                        NavigateActivation(() => navigation.InternalNavigate(typeof(MusicsPage), new()
                        {
                            [PageConstants.MUSIC_ID] = musicId
                        }));
                        if (navigation.CurrentInternalPage is not MusicsPage comments) return false;
                        var found = await comments.OpenCommentAsync(commentId, token);
                        token.ThrowIfCancellationRequested();
                        return found;
                    }
                    return await OpenMusicActivationAsync(musicId, request.MusicAction, token);
                }
                switch (request.Destination)
                {
                    case "playlists":
                        NavigateActivation(navigation.OpenMusicPlaylists);
                        return true;
                    case "activities":
                        NavigateActivation(navigation.OpenFollowingActivities);
                        return true;
                    case "settings":
                        Task settings = Task.CompletedTask;
                        NavigateActivation(() => settings = navigation.OpenSettingsAsync(request.SettingsSection, token));
                        await settings;
                        token.ThrowIfCancellationRequested();
                        return navigation.CurrentInternalPage is SettingsPage;
                }
                var tag = request.Destination switch
                {
                    "home" or "music" => nameof(MusicPage), "movies" => "MoviesPage",
                    "apps" => "AppsPage", "vpn" => nameof(VPNPage), _ => null
                };
                if (tag is null) return false;
                NavigateActivation(() => NavigateSection(tag));
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception error)
            {
                // Do not log complete URIs or search text supplied by another app.
                AppLog.Error("Deep-link destination could not be opened.", error);
                return false;
            }
            finally
            {
                if (ReferenceEquals(_musicActivationCancellation, cancellation)) _musicActivationCancellation = null;
                cancellation.Dispose();
            }
        }

        private void EnsureActivationLanding()
        {
            if (InternalFrame.Content is not null) return;
            NavigateActivation(() => ViewModel.NavigationService.InternalNavigate(typeof(MusicPage)));
        }

        private async Task<bool> OpenMusicActivationAsync(Guid musicPublicId, MusicActivationAction action, CancellationToken token)
        {
            var services = ((App)App.Current).Services;
            var music = services.GetRequiredService<MusicPageViewModel>();
            if (action == MusicActivationAction.Play) services.GetRequiredService<MusicPlaylistPlaybackService>().Cancel();
            NavigateActivation(() => ViewModel.NavigationService.InternalNavigate(typeof(MusicsPage), new() { [PageConstants.MUSIC_ID] = musicPublicId }));
            if (action == MusicActivationAction.Details) return true;
            var completed = await music.ActivateMusicAsync(musicPublicId, action, token);
            token.ThrowIfCancellationRequested();
            if (completed && action == MusicActivationAction.Download)
                services.GetRequiredService<TeachingTipService>().ShowMessage("دانلود کامل شد", "آهنگ در کش برنامه ذخیره شد و برای پخش دوباره دانلود نمی‌شود.");
            else if (!completed)
                services.GetRequiredService<TeachingTipService>().ShowMessage("آهنگ در دسترس نیست", "فایل این آهنگ دریافت نشد؛ دوباره امتحان کن.");
            return completed;
        }

        private void NavigateSection(string tag)
        {
            if (tag == nameof(MusicPage))
            {
                if (InternalFrame.Content is not MusicPage) ViewModel.NavigationService.InternalNavigate(typeof(MusicPage));
            }
            else if (tag == nameof(VPNPage))
            {
                if (InternalFrame.Content is not VPNPage) ViewModel.NavigationService.InternalNavigate(typeof(VPNPage));
            }
            else if (tag is "MoviesPage" or "AppsPage")
            {
                var destination = tag == "MoviesPage" ? "movies" : "apps";
                if (InternalFrame.Content is not SectionLandingPage page || page.Destination != destination)
                    ViewModel.NavigationService.InternalNavigate(typeof(SectionLandingPage), new() { ["Section"] = destination });
            }
        }

        private void NavigationViewMain_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_synchronizingNavigationSelection || !_internalFrameInitialized)
                return;

            var navigationViewItem = args.SelectedItem as NavigationViewItem;


            if (navigationViewItem is not null)
            {
                if (navigationViewItem.Tag is string tag) NavigateSection(tag);
                ElementSoundPlayer.Play(ElementSoundKind.MoveNext);

            }
        }

        private void NavigationViewMain_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            ViewModel.NavigationService.InternalGoBack();
        }
    }
}
