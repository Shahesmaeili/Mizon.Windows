using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Mizon.Windows.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MusicsPage : Page
    {
        private MusicPageViewModel ViewModel => (MusicPageViewModel)DataContext;
        private async void PictureInPicture_Click(object sender, RoutedEventArgs args)
        {
            try { await ((App)App.Current).Services.GetRequiredService<PictureInPictureService>().ShowMusicAsync(); }
            catch (Exception error)
            {
                AppLog.Error("Unable to open the picture-in-picture player.", error);
                ((App)App.Current).Services.GetRequiredService<TeachingTipService>()
                    .ShowMessage("پلیر شناور", "باز کردن پنجرهٔ شناور ممکن نشد.", "باشه");
            }
        }
        private Compositor _compositor = App.MainMizonWindow!.Compositor;
        private SpringVector3NaturalMotionAnimation? _musicSpringVector3NaturalMotionAnimation;
        private Visual? _musicVisual;
        private bool _pageActive;
        public Guid MusicPublicId { get; private set; }
        private MusicCoverTransition? _coverTransition;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _coverTransitionTimeout;
        private bool _coverTransitionStarted, _coverTransitionCompleted, _destinationCoverReady;
        private CancellationTokenSource? _coverPreviewCancellation;
        private bool _navigationPreviewReady;
        private bool _insightsCreated, _insightsQueued;
        private UserControls.StoryViewer? _storyViewer;
        private Button? _storySource;
        private CompositionGeometricClip? _playClip, _likesClip;
        private CompositionPathGeometry? _playClipGeometry, _likesClipGeometry;
        private CanvasGeometry? _playClipSource, _likesClipSource;
    public MusicsPage()
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            this.InitializeComponent();
            DataContext = ((App)App.Current).Services.GetRequiredService<MusicPageViewModel>();
            System.Diagnostics.Debug.WriteLine($"[MusicNavigation] Page construction: {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);
            CancelCommentNavigation();
            ClearCoverTransition();
            if (args.Parameter is not Dictionary<string, object> parameters) return;
            MusicPublicId = parameters.TryGetValue(PageConstants.MUSIC_ID, out var value)
                && Guid.TryParse(value?.ToString(), out var id) ? id : Guid.Empty;
            _pendingCommentPublicId = parameters.TryGetValue(PageConstants.MUSIC_COMMENT_ID, out var comment)
                && Guid.TryParse(comment?.ToString(), out var commentId) ? commentId : Guid.Empty;
            CommentNavigationNotice.IsOpen = false;
            CoverImage.Opacity = HasCurrentDestinationCover() ? 1 : 0;
            if (!parameters.Remove(MusicCoverTransition.ParameterKey, out var payload)
                || payload is not MusicCoverTransition transition) return;

            _coverTransition = transition;
            _coverNavigationSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            // Hide both cached and newly-arriving destination content before the first
            // frame. Only the connected animation may reveal the incoming artwork.
            CoverImage.Opacity = 0;
            NavigationCoverImage.Opacity = 0;
            _musicVisual?.StopAnimation("Scale");
            if (_musicVisual is not null) _musicVisual.Scale = Vector3.One;
            _destinationCoverReady = HasCurrentDestinationCover();
            NavigationCoverImage.Visibility = Visibility.Visible;
            transition.Animation.Completed += CoverTransition_Completed;
            _coverTransitionTimeout = DispatcherQueue.CreateTimer();
            _coverTransitionTimeout.Interval = TimeSpan.FromSeconds(3);
            _coverTransitionTimeout.IsRepeating = false;
            _coverTransitionTimeout.Tick += CoverTransition_Timeout;
            _coverTransitionTimeout.Start();
            _coverPreviewCancellation = new();
            _ = LoadNavigationPreviewAsync(transition, _coverPreviewCancellation.Token);
        }

        private async Task LoadNavigationPreviewAsync(MusicCoverTransition transition, CancellationToken token)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var scale = XamlRoot?.RasterizationScale ?? App.MainMizonWindow?.Content?.XamlRoot?.RasterizationScale ?? 1;
                var pixels = Math.Clamp((int)Math.Ceiling(280 * scale), 280, 1680);
                var preview = await transition.TakePreviewAsync(pixels, token);
                if (token.IsCancellationRequested || !ReferenceEquals(_coverTransition, transition)) return;
                if (preview is null) { ClearCoverTransition(); return; }
                NavigationCoverImage.Source = preview;
                _navigationPreviewReady = true;
                System.Diagnostics.Debug.WriteLine($"[MusicNavigation] Cover ready: {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
                TryStartCoverAnimation();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                AppLog.Error("Unable to decode the full-size navigation cover.", error);
                if (ReferenceEquals(_coverTransition, transition)) ClearCoverTransition();
            }
        }

        private bool HasCurrentDestinationCover()
        {
            if (MusicPublicId == Guid.Empty || ViewModel.CurrentMusicPublicId != MusicPublicId
                || string.IsNullOrWhiteSpace(ViewModel.CoverImagePath)
                || CoverImage.Source is not BitmapImage { PixelWidth: > 0, UriSource: { } actual }) return false;
            return Uri.TryCreate(ViewModel.CoverImagePath, UriKind.Absolute, out var expected) && actual.Equals(expected);
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
        {
            CancelCommentNavigation();
            ClearCoverTransition();
            base.OnNavigatedFrom(args);
        }

        internal MusicCoverTransition? PrepareReturnCover()
        {
            var source = _navigationPreviewReady && NavigationCoverImage.Opacity > 0 ? NavigationCoverImage : CoverImage;
            if (source.Opacity <= 0) return null;
            return MusicCoverTransition.Prepare(source);
        }

        private void NavigationCoverImage_Ready(object sender, RoutedEventArgs args) => TryStartCoverAnimation();
        private void NavigationCoverImage_SizeChanged(object sender, SizeChangedEventArgs args) => TryStartCoverAnimation();

        private void TryStartCoverAnimation()
        {
            if (!_pageActive || _coverTransition is null || _coverTransitionStarted || !_navigationPreviewReady
                || NavigationCoverImage.ActualWidth <= 0 || NavigationCoverImage.ActualHeight <= 0) return;
            _coverTransitionStarted = true;
            try
            {
                // These changes happen in one UI turn; the target is never rendered
                // at full size before ConnectedAnimation takes ownership of it.
                NavigationCoverImage.Opacity = 1;
                if (!_coverTransition.Animation.TryStart(NavigationCoverImage)) ClearCoverTransition();
            }
            catch (Exception error)
            {
                AppLog.Error("Music cover transition could not start.", error);
                ClearCoverTransition();
            }
        }

        private void CoverTransition_Completed(ConnectedAnimation sender, object args)
        {
            if (!ReferenceEquals(_coverTransition?.Animation, sender)) return;
            _coverTransitionCompleted = true;
            _coverNavigationSettled?.TrySetResult();
            QueueInsightsCreation();
            StopCoverTransitionTimeout();
            _destinationCoverReady = HasCurrentDestinationCover();
            if (_destinationCoverReady) ClearCoverTransition();
        }

        private void CoverImage_ImageOpened(object sender, RoutedEventArgs args)
        {
            _destinationCoverReady = HasCurrentDestinationCover();
            if (!_destinationCoverReady) return;
            if (_coverTransition is null) CoverImage.Opacity = 1;
            if (_coverTransitionCompleted) ClearCoverTransition();
        }

        private void CoverTransition_Timeout(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => ClearCoverTransition();

        private void ClearCoverTransition()
        {
            _coverNavigationSettled?.TrySetResult();
            _coverNavigationSettled = null;
            _coverPreviewCancellation?.Cancel();
            _coverPreviewCancellation?.Dispose();
            _coverPreviewCancellation = null;
            StopCoverTransitionTimeout();
            if (_coverTransition is not null)
            {
                _coverTransition.Animation.Completed -= CoverTransition_Completed;
                _coverTransition.Dispose();
                _coverTransition = null;
            }
            CoverImage.Opacity = HasCurrentDestinationCover() ? 1 : 0;
            NavigationCoverImage.Opacity = 0;
            NavigationCoverImage.Source = null;
            NavigationCoverImage.Visibility = Visibility.Collapsed;
            _navigationPreviewReady = false;
            _coverTransitionStarted = _coverTransitionCompleted = _destinationCoverReady = false;
            QueueInsightsCreation();
        }

        private void QueueInsightsCreation()
        {
            if (!_pageActive || !IsLoaded || _insightsCreated || _insightsQueued
                || (_coverTransition is not null && !_coverTransitionCompleted)) return;
            _insightsQueued = true;
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _insightsQueued = false;
                if (!_pageActive || !IsLoaded || _insightsCreated
                    || (_coverTransition is not null && !_coverTransitionCompleted)) return;
                // The fixed-height host remains in layout; initializing Skia must
                // not delay the first frame or the connected cover animation.
                _insightsCreated = FindName("MusicInsightsChart") is not null;
            })) _insightsQueued = false;
        }

        private void StopCoverTransitionTimeout()
        {
            if (_coverTransitionTimeout is not null)
            {
                _coverTransitionTimeout.Stop();
                _coverTransitionTimeout.Tick -= CoverTransition_Timeout;
                _coverTransitionTimeout = null;
            }
        }

        private async void MusicPlaylistsFlyout_Opened(object sender, object e)
        {
            try
            {
                await ViewModel.LoadMusicPlaylistsAsync();

                while (MusicPlaylistsMenuFlyout.Items.Count > 1)
                    MusicPlaylistsMenuFlyout.Items.RemoveAt(1);

                if (ViewModel.MusicPlaylists.Count > 0)
                    MusicPlaylistsMenuFlyout.Items.Add(new MenuFlyoutSeparator());

                foreach (var playlist in ViewModel.MusicPlaylists)
                    MusicPlaylistsMenuFlyout.Items.Add(CreateMusicPlaylistMenuItem(playlist));
            }
            catch (Exception exception)
            {
                AppLog.Error("Music playlist flyout could not be opened.", exception);
            }
        }

        private async void AlbumDetailsTab_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            CancelCommentNavigation();
            CommentNavigationNotice.IsOpen = false;
            await ViewModel.SelectAlbumDetailsAsync();
        }

        private async void ContributorDetailsTab_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            CancelCommentNavigation();
            CommentNavigationNotice.IsOpen = false;
            await ViewModel.SelectContributorDetailsAsync();
        }

        private async void CommentsDetailsTab_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            CancelCommentNavigation();
            CommentNavigationNotice.IsOpen = false;
            await ViewModel.SelectCommentsDetailsAsync();
        }

        private void MusicDetailsMusicCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: MusicDetailsMusicItemViewModel music })
                return;

            ViewModel.NavigationService.InternalNavigate(typeof(MusicsPage), new()
            {
                [PageConstants.MUSIC_ID] = music.PublicId
            });
        }

        private async void MusicPlaylistCheckBox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ViewModel.UpdateMusicPlaylistMembershipAsync((sender as FrameworkElement)?.DataContext as MusicPlaylistItemViewModel);
            }
            catch (Exception exception)
            {
                AppLog.Error("Music playlist membership could not be updated.", exception);
            }
        }

        private MenuFlyoutItem CreateMusicPlaylistMenuItem(MusicPlaylistItemViewModel playlist)
        {
            return new MenuFlyoutItem
            {
                DataContext = playlist,
                Template = (ControlTemplate)Resources["MusicPlaylistMenuFlyoutItemTemplate"]
            };
        }

        private async void CreateMusicPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            MusicPlaylistsMenuFlyout.Hide();

            var titleTextBox = new TextBox
            {
                MaxLength = 60,
                PlaceholderText = "عنوان پلی‌لیست"
            };

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "ساخت پلی‌لیست",
                Content = titleTextBox,
                PrimaryButtonText = "ساخت و افزودن آهنگ",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                await ViewModel.CreateMusicPlaylistAsync(titleTextBox.Text);
            }
            catch (Exception exception)
            {
                AppLog.Error("Music playlist could not be created.", exception);
            }
        }







        private void CreateOrUpdateSpringAnimation(float rms)
        {
            if (!_pageActive || _coverTransition is not null || _musicVisual is null || float.IsNaN(rms) || float.IsInfinity(rms))
                return;

            float intensity = rms * 1f;
            intensity = Math.Clamp(intensity, 0f, 1f); // بین 0 و 1

            float scale = 1.0f + intensity * 0.10f;

            if (_musicSpringVector3NaturalMotionAnimation == null)
            {
                _musicSpringVector3NaturalMotionAnimation = _compositor.CreateSpringVector3Animation();
                _musicSpringVector3NaturalMotionAnimation.Target = "Scale";
                _musicSpringVector3NaturalMotionAnimation.DampingRatio = 0.01f;
                _musicSpringVector3NaturalMotionAnimation.Period = TimeSpan.FromMilliseconds(5);
            }

            _musicSpringVector3NaturalMotionAnimation.FinalValue = new Vector3(scale);
            _musicVisual.StartAnimation("Scale", _musicSpringVector3NaturalMotionAnimation);
        }







        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_pageActive) return;
            _pageActive = true;
            ApplyDetailLayout(ActualWidth);
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            MusicBorder.SizeChanged += MusicBorder_SizeChanged;
            PlayPauseButton.SizeChanged += PlayerButton_SizeChanged;
            LikesButton.SizeChanged += LikesButton_SizeChanged;
            InitializeCoverVisual();
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (!_pageActive) return;
                TryStartCoverAnimation();
                QueueInsightsCreation();
                try { await ApplyButtonHalfCircleClip(); }
                catch (Exception error) { AppLog.Error("Music play-button clipping failed.", error); }
                if (_pendingCommentPublicId != Guid.Empty) await OpenCommentAsync(_pendingCommentPublicId);
            });


            //      StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(
            //new Uri("ms-appx:///Assets/image1.jpeg"));

            //      using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);

            //      BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

            //      SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
            //          BitmapPixelFormat.Bgra8,
            //          BitmapAlphaMode.Premultiplied);

            //     var asdasd= BlurHash.Encode(bitmap);



            //      var bmp = 
            // BlurHash.Decode(asdasd, 128, 128);

            //      var source = new SoftwareBitmapSource();



            //      await source.SetBitmapAsync(bmp);

            //      CoverImage.Source = source;
        }

        private void MusicPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (IsLoaded || !_pageActive) return;
            _pageActive = false;
            CancelCommentNavigation();
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ClearCoverTransition();
            MusicBorder.SizeChanged -= MusicBorder_SizeChanged;
            PlayPauseButton.SizeChanged -= PlayerButton_SizeChanged;
            LikesButton.SizeChanged -= LikesButton_SizeChanged;
            _storyViewer?.Close();
            _musicVisual?.StopAnimation("Scale");
            if (_musicVisual is not null) _musicVisual.Scale = Vector3.One;
            _musicSpringVector3NaturalMotionAnimation?.Dispose();
            _musicSpringVector3NaturalMotionAnimation = null;
            _musicVisual = null;
            shadow.Receivers.Clear();
            ElementCompositionPreview.GetElementVisual(PlayPauseButton).Clip = null;
            ElementCompositionPreview.GetElementVisual(LikesButton).Clip = null;
            _playClip?.Dispose(); _playClip = null;
            _likesClip?.Dispose(); _likesClip = null;
            _playClipGeometry?.Dispose(); _playClipGeometry = null;
            _likesClipGeometry?.Dispose(); _likesClipGeometry = null;
            _playClipSource?.Dispose(); _playClipSource = null;
            _likesClipSource?.Dispose(); _likesClipSource = null;
            ConnectedAnimationService.GetForCurrentView().GetAnimation("ImageToPopup")?.Cancel();
            ConnectedAnimationService.GetForCurrentView().GetAnimation("PopupToImage")?.Cancel();
            SmokeGrid.Visibility = Visibility.Collapsed;
            SmokeGrid.Opacity = 0;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.ServerWaveformIntensity))
            {
                CreateOrUpdateSpringAnimation(ViewModel.ServerWaveformIntensity);
            }

        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            if (!_pageActive || sender is not Button button || !button.IsEnabled) return;
            var musicId = ViewModel.CurrentMusicPublicId;
            button.IsEnabled = false;
            try
            {
                var path = await ViewModel.DownloadCurrentMusicAsync();
                if (!_pageActive || ViewModel.CurrentMusicPublicId != musicId) return;
                var file = await StorageFile.GetFileFromPathAsync(path);
                var folder = await file.GetParentAsync();
                if (!_pageActive || ViewModel.CurrentMusicPublicId != musicId) return;
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(file);
                if (!await Launcher.LaunchFolderAsync(folder, options))
                    throw new InvalidOperationException("پوشهٔ فایل دانلودشده باز نشد.");
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(button)
                    ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button);
                peer?.RaiseNotificationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.ActionCompleted,
                    Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.ImportantMostRecent,
                    "دانلود آهنگ کامل شد؛ فایل در پوشه نمایش داده شد.", "MusicDownload");
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                AppLog.Error("Manual music download failed.", error);
                if (_pageActive && ViewModel.CurrentMusicPublicId == musicId)
                {
                    try
                    {
                        await new ContentDialog
                        {
                            XamlRoot = XamlRoot,
                            Title = "دانلود آهنگ انجام نشد",
                            Content = "اتصال و دسترسی به فایل را بررسی کنید و دوباره تلاش کنید.",
                            CloseButtonText = "بستن",
                            FlowDirection = FlowDirection.RightToLeft
                        }.ShowAsync();
                    }
                    catch (Exception dialogError) { AppLog.Error("Download error dialog could not be displayed.", dialogError); }
                }
            }
            finally { button.IsEnabled = true; }
        }
        private void Image_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ViewModel.CoverImagePath))
                return;

            var connectedAnimationService = ConnectedAnimationService
           .GetForCurrentView();
            connectedAnimationService.DefaultDuration = TimeSpan.FromSeconds(0.8);

            // آماده‌سازی انیمیشن از عکس
            var animation = connectedAnimationService
               .PrepareToAnimate("ImageToPopup", CoverImage);

            // نمایش لایهٔ بزرگ کاور با همان تصویر دانلود/کش‌شدهٔ موسیقی.
            SmokeGrid.Visibility = Visibility.Visible;
            SmokeGrid.Opacity = 1;
            SmokeGrid.Focus(FocusState.Programmatic);
            animation.Configuration = new BasicConnectedAnimationConfiguration();
            CoverPreviewScrollViewer.ChangeView(null, null, 1f, true);

            // شروع انیمیشن روی خود Image مقصد، نه یک Border یا تصویر نمونه.
            animation.TryStart(ExpandedCoverImage);



        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseCoverPreview();
        }

        private void ResetCoverZoomButton_Click(object sender, RoutedEventArgs e)
        {
            CoverPreviewScrollViewer.ChangeView(null, null, 1f, false);
        }

        private void SmokeGrid_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Escape)
                return;

            e.Handled = true;
            CloseCoverPreview();
        }

        private void CloseCoverPreview()
        {
            if (SmokeGrid.Visibility != Visibility.Visible)
                return;

            var connectedAnimationService = ConnectedAnimationService
           .GetForCurrentView();
            connectedAnimationService.DefaultDuration = TimeSpan.FromSeconds(0.8);

            var animation = connectedAnimationService
       .PrepareToAnimate("PopupToImage", ExpandedCoverImage);
            animation.Configuration = new BasicConnectedAnimationConfiguration();

            SmokeGrid.Opacity = 0;

            animation.TryStart(CoverImage);
            animation.Completed += Animation_Completed;
        }

        private void Animation_Completed(ConnectedAnimation sender, object args)
        {
            sender.Completed -= Animation_Completed;
            SmokeGrid.Visibility = Visibility.Collapsed;
        }

        private void TopEqualizerGrid_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe &&
            fe.DataContext is EqualizerBarItemViewModel item)
            {
                int index = ViewModel.Bars.IndexOf(item);
                ViewModel.UpdateHoverPreview(index);
            }
        }

        private void TopEqualizerGrid_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ViewModel.ClearHoverPreview();
        }

        private void TopEqualizerGrid_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(sender as UIElement);

            if (!point.Properties.IsLeftButtonPressed)
                return;

            if (sender is Grid grid && grid.DataContext is EqualizerBarItemViewModel item)
            {
                int index = ViewModel.Bars.IndexOf(item);

                ViewModel.SeekByProgress(TimeSpan.FromSeconds(index), index);
                e.Handled = true;
            }
        }

        private void BottomEqualizerGrid_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: EqualizerBarItemViewModel item })
                ViewModel.UpdateBottomBarHover(item);
        }

        private void BottomEqualizerGrid_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: EqualizerBarItemViewModel item } && item.IsBottomHover)
                ViewModel.UpdateBottomBarHover(null);
        }

        private async void BottomEqualizerGrid_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
                return;

            if (sender is not FrameworkElement { DataContext: EqualizerBarItemViewModel item })
                return;

            var index = ViewModel.Bars.IndexOf(item);
            if (index < 0)
                return;

            e.Handled = true;
            var timestamp = ViewModel.GetCommentTimestampForBarIndex(index);
            var commentTextBox = new TextBox
            {
                AcceptsReturn = true,
                MinHeight = 92,
                TextWrapping = TextWrapping.Wrap,
                PlaceholderText = $"نظر شما در {timestamp:mm\\:ss}",
                MaxLength = 1000
            };

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"ثبت نظر در {timestamp:mm\\:ss}",
                Content = commentTextBox,
                PrimaryButtonText = "ثبت نظر",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false
            };

            commentTextBox.TextChanged += (_, _) =>
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(commentTextBox.Text);

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                await ViewModel.CreateMusicCommentAsync(commentTextBox.Text, index);
            }
            catch (Exception exception)
            {
                AppLog.Error("Music comment could not be created.", exception);
            }
        }

        private void PlayerButton_SizeChanged(object sender, SizeChangedEventArgs e) => _ = ApplyButtonHalfCircleClip();
        private void LikesButton_SizeChanged(object sender, SizeChangedEventArgs e) => _ = ApplyLikeButtonHalfCircleClip();

        private Task ApplyButtonHalfCircleClip()
        {
            if (!_pageActive || PlayPauseButton.ActualWidth <= 0 || PlayPauseButton.ActualHeight <= 0)
                return Task.CompletedTask;
            var visual = ElementCompositionPreview.GetElementVisual(PlayPauseButton);
            var compositor = visual.Compositor;

            float width = (float)PlayPauseButton.ActualWidth;
            float height = (float)PlayPauseButton.ActualHeight;

            // شعاع دایره برای حفره
            float radius = Math.Min(width, height) / 4; // یک چهارم کوچکترین بعد

            // مرکز
            float centerX = width / 2;
            float centerY = height / 2;

            var device = CanvasDevice.GetSharedDevice();

            // ۱. ایجاد مستطیل کامل عکس
            using var fullRect = CanvasGeometry.CreateRectangle(device, 0, 0, width, height);

            // ۲. ایجاد دایره برای حفره
            using var circle = CanvasGeometry.CreateCircle(device, 48, 52, 20);

            // ۳. دایره را از مستطیل حذف کنید (Exclude = ایجاد سوراخ)
            var geometryWithHole = fullRect.CombineWith(
                circle,
                Matrix3x2.Identity,
                CanvasGeometryCombine.Exclude);

            var compositionPath = new CompositionPath(geometryWithHole);
            visual.Clip = null;
            _playClip?.Dispose();
            _playClipGeometry?.Dispose();
            _playClipSource?.Dispose();
            _playClipSource = geometryWithHole;
            _playClipGeometry = compositor.CreatePathGeometry();
            _playClipGeometry.Path = compositionPath;
            _playClip = compositor.CreateGeometricClip(_playClipGeometry);
            visual.Clip = _playClip;
            return Task.CompletedTask;
        }


        private Task ApplyLikeButtonHalfCircleClip()
        {
            if (!_pageActive || LikesButton.ActualWidth <= 0 || LikesButton.ActualHeight <= 0)
                return Task.CompletedTask;
            var visual = ElementCompositionPreview.GetElementVisual(LikesButton);
            var compositor = visual.Compositor;

            float width = (float)LikesButton.ActualWidth;
            float height = (float)LikesButton.ActualHeight;


            var device = CanvasDevice.GetSharedDevice();

            // ۱. ایجاد مستطیل کامل عکس
            using var fullRect = CanvasGeometry.CreateRectangle(device, 0, 0, width, height);

            // ۲. ایجاد دایره برای حفره
            using var circle = CanvasGeometry.CreateCircle(device, -16, height / 2, 24);

            // ۳. دایره را از مستطیل حذف کنید (Exclude = ایجاد سوراخ)
            var geometryWithHole = fullRect.CombineWith(
                circle,
                Matrix3x2.Identity,
                CanvasGeometryCombine.Exclude);

            var compositionPath = new CompositionPath(geometryWithHole);
            visual.Clip = null;
            _likesClip?.Dispose();
            _likesClipGeometry?.Dispose();
            _likesClipSource?.Dispose();
            _likesClipSource = geometryWithHole;
            _likesClipGeometry = compositor.CreatePathGeometry();
            _likesClipGeometry.Path = compositionPath;
            _likesClip = compositor.CreateGeometricClip(_likesClipGeometry);
            visual.Clip = _likesClip;
            return Task.CompletedTask;
        }

        private void MusicBorder_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeCoverVisual();
        }

        private void InitializeCoverVisual()
        {
            if (!shadow.Receivers.Contains(ShadowCastGrid)) shadow.Receivers.Add(ShadowCastGrid);

            _musicVisual = ElementCompositionPreview.GetElementVisual(MusicBorder);
            _compositor = _musicVisual.Compositor;

            RecenterCoverVisual();
            _musicVisual.Scale = Vector3.One;
        }

        private void MusicBorder_SizeChanged(object sender, SizeChangedEventArgs e) => RecenterCoverVisual();

        private void RecenterCoverVisual()
        {
            if (_musicVisual is null) return;
            _musicVisual.CenterPoint = new Vector3(
                (float)MusicBorder.ActualWidth / 2f,
                (float)MusicBorder.ActualHeight / 2f,
                0f
            );
        }

        private void Button_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var button = (Button)sender;
            ViewModel.EqualizerCommentBringToFront((EqualizerCommentItemViewModel)button.DataContext);
        }

        private async void LikeButton_Click(object sender, RoutedEventArgs e)
        {
            await ApplyLikeButtonHalfCircleClip();
        }

        private async void StoryCard_Click(object sender, RoutedEventArgs e)
        {
            if (!_pageActive || (sender as FrameworkElement)?.Tag is not StoryItemViewModel story ||
                App.MainMizonWindow is not MainWindow window)
                return;
            var stories = new List<StoryItemViewModel>(ViewModel.Stories);
            var index = stories.IndexOf(story);
            if (index < 0) return;
            _storyViewer?.Close();
            var preview = ((sender as Button)?.Content as FrameworkElement)?.FindName("StoryPreviewArtwork") as Image;
            var transition = MusicCoverTransition.Prepare(preview);
            var transitionHandedOff = false;
            var viewer = new UserControls.StoryViewer();
            _storyViewer = viewer;
            _storySource = sender as Button;
            viewer.CloseRequested += StoryViewer_CloseRequested;
            try
            {
                window.ShowWindowOverlay(viewer);
                transitionHandedOff = true;
                await viewer.OpenWithTransitionAsync(ViewModel, stories, index, transition, CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (ReferenceEquals(_storyViewer, viewer)) viewer.Close();
                AppLog.Error("Music story could not be opened.", error);
            }
            finally
            {
                if (!transitionHandedOff) transition?.Dispose();
            }
        }

        private void StoryViewer_CloseRequested(object? sender, EventArgs e)
        {
            if (sender is not UserControls.StoryViewer viewer) return;
            viewer.CloseRequested -= StoryViewer_CloseRequested;
            if (App.MainMizonWindow is MainWindow window) window.HideWindowOverlay(viewer);
            if (!ReferenceEquals(_storyViewer, viewer)) return;
            _storyViewer = null;
            if (_pageActive && _storySource is { IsLoaded: true }) _storySource.Focus(FocusState.Programmatic);
            _storySource = null;
        }

        private void StoryCard_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
                element.Opacity = 1;
            
        }

        private void StoryCard_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
                element.Opacity = 0.8;
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyDetailLayout(e.NewSize.Width);
    }
}
