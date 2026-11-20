using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
using Microsoft.UI.Xaml.Input;
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
        private MusicPageViewModel ViewModel { get; }
        private ContentDialog? _waveformCommentDialog;
        private ContentDialog? _createPlaylistDialog;
        public LocalizationService Localization => LocalizationService.Current;
        private string Text(string key, int revision) => Localization.GetString("MusicDetails/" + key, revision);
        private string Text(string key) => Localization.GetString("MusicDetails/" + key);
        private void RefreshTrackTitle()
        {
            var title = ViewModel.Title;
            var persian = ViewModel.PersianTitle?.Trim('(', ')').Trim();
            var hasPersianTitle = !string.IsNullOrWhiteSpace(persian);
            var hasTitle = !string.IsNullOrWhiteSpace(title);
            var showPersianFirst = hasPersianTitle && (Localization.IsPersian || !hasTitle);

            TrackTitleText.Text = (showPersianFirst ? persian : title) ?? string.Empty;
            TrackPersianTitleText.Text = hasTitle && hasPersianTitle
                ? $"({(showPersianFirst ? title : persian)})"
                : string.Empty;
            TrackPersianTitleText.Visibility = hasTitle && hasPersianTitle ? Visibility.Visible : Visibility.Collapsed;
        }
        private string ViewsLabel(int total, int live, int revision) =>
            Localization.Format("MusicDetails/ViewsStatus", total, live);
        private void OpenMusicVideo_Click(object sender, RoutedEventArgs args)
        {
            if (!IsMusicDetailsActive || !ViewModel.HasMusicVideo || ViewModel.MusicVideo is not { } video) return;

            ViewModel.NavigationService.InternalNavigate(typeof(MusicVideoPage), new()
            {
                [PageConstants.MUSIC_ID] = MusicPublicId,
                [PageConstants.MUSIC_VIDEO] = new MusicVideoNavigationData(video, ViewModel.Title, ViewModel.PersianTitle?.Trim('(', ')'))
            });
        }

        private async void PictureInPicture_Click(object sender, RoutedEventArgs args)
        {
            try { await ((App)App.Current).Services.GetRequiredService<PictureInPictureService>().ShowMusicAsync(); }
            catch (Exception error)
            {
                AppLog.Error("Unable to open the picture-in-picture player.", error);
                ((App)App.Current).Services.GetRequiredService<TeachingTipService>()
                    .ShowMessage(Text("FloatingPlayer"), Text("FloatingPlayerFailed"), Text("Dismiss"));
            }
        }
        private Compositor _compositor = App.MainMizonWindow!.Compositor;
        private SpringVector3NaturalMotionAnimation? _musicSpringVector3NaturalMotionAnimation;
        private Visual? _musicVisual;
        private bool _pageActive;
        public Guid MusicPublicId { get; private set; }
        internal Task<bool> WaitForDetailsAsync(CancellationToken token) =>
            ViewModel.WaitForMusicDetailsAsync(MusicPublicId, token);
        private MusicCoverTransition? _coverTransition;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _coverTransitionTimeout;
        private bool _coverTransitionStarted, _coverTransitionCompleted, _destinationCoverReady;
        private CancellationTokenSource? _coverPreviewCancellation;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _coverPreviewCloseTimer;
        private bool _isCoverPreviewOpen;
        private bool _navigationPreviewReady;
        private UserControls.StoryViewer? _storyViewer;
        private Button? _storySource;
    public MusicsPage()
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            ViewModel = ((App)App.Current).Services.GetRequiredService<MusicPageViewModel>();
            InitializeComponent();
            DataContext = ViewModel;
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
            ResetCoverPreview();
            ClearCoverTransition();
            base.OnNavigatedFrom(args);
        }

        internal MusicCoverTransition? PrepareReturnCover()
        {
            var source = PrepareReturnSource();
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
                var musicId = MusicPublicId;
                await ViewModel.LoadMusicPlaylistsAsync();
                if (!IsMusicDetailsActive || MusicPublicId != musicId) return;

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

        private async void DetailsTab_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs e)
        {
            if (!_pageActive || ViewModel.CurrentMusicPublicId != MusicPublicId) return;
            try
            {
                if (sender.SelectedItem == AlbumDetailsTab && !ViewModel.IsAlbumDetailsSelected)
                {
                    CancelCommentNavigation();
                    CommentNavigationNotice.IsOpen = false;
                    await ViewModel.SelectAlbumDetailsAsync();
                }
                else if (sender.SelectedItem == ContributorDetailsTab && !ViewModel.IsContributorDetailsSelected)
                {
                    CancelCommentNavigation();
                    CommentNavigationNotice.IsOpen = false;
                    await ViewModel.SelectContributorDetailsAsync();
                }
                else if (sender.SelectedItem == CommentsDetailsTab && !ViewModel.IsCommentsDetailsSelected)
                {
                    CancelCommentNavigation();
                    CommentNavigationNotice.IsOpen = false;
                    await ViewModel.SelectCommentsDetailsAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { AppLog.Error("Music details tab could not be loaded.", error); }
        }

        private void ContributorProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MusicContributorItemViewModel person })
                ViewModel.NavigationService.OpenPersonProfile(person.PersonPublicId,
                    sender is Button { Content: StackPanel content } && content.Children[0] is PersonPicture portrait ? portrait : null);
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
                Language = Localization.LanguageTag,
                FontFamily = Localization.TextFontFamily,
                Template = (ControlTemplate)Resources["MusicPlaylistMenuFlyoutItemTemplate"]
            };
        }

        private async void CreateMusicPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsMusicDetailsActive || _createPlaylistDialog is not null || XamlRoot is not { } root) return;

            var titleTextBox = new TextBox { MaxLength = 60 };
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Content = titleTextBox,
                DefaultButton = ContentDialogButton.Primary
            };
            _createPlaylistDialog = dialog;
            var musicId = MusicPublicId;
            try
            {
                MusicPlaylistsMenuFlyout.Hide();
                if (await ShowLocalizedTrackDialogAsync(dialog, () =>
                {
                    titleTextBox.PlaceholderText = Text("PlaylistTitle");
                    AutomationProperties.SetName(titleTextBox, Text("PlaylistTitle"));
                    dialog.Title = Text("NewPlaylist");
                    dialog.PrimaryButtonText = Text("CreateAndAddTrack");
                    dialog.CloseButtonText = Text("Cancel");
                }) == ContentDialogResult.Primary && IsMusicDetailsActive && musicId == MusicPublicId)
                    await ViewModel.CreateMusicPlaylistAsync(titleTextBox.Text);
            }
            catch (Exception exception)
            {
                AppLog.Error("Music playlist could not be created.", exception);
            }
            finally
            {
                _createPlaylistDialog = null;
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
            AttachInsightsPresentation();
            ApplyDetailLayout(ActualWidth);
            RefreshTrackTitle();
            AttachWaveformMotion();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            Localization.PropertyChanged -= TrackDetailsLanguageChanged;
            Localization.PropertyChanged += TrackDetailsLanguageChanged;
            ObserveSelectedAudioOutput();
            MusicBorder.SizeChanged += MusicBorder_SizeChanged;
            InitializeCoverVisual();
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (!_pageActive) return;
                TryStartCoverAnimation();
                if (_pendingCommentPublicId != Guid.Empty) await OpenCommentAsync(_pendingCommentPublicId);
            });

        }

        private void MusicPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (IsLoaded || !_pageActive) return;
            _pageActive = false;
            _waveformCommentDialog?.Hide();
            _createPlaylistDialog?.Hide();
            if (_audioOutputMenuOpen) AudioRenderMenuFlyout.Hide();
            CloseAudioOutputMenu();
            UnobserveSelectedAudioOutput();
            Localization.PropertyChanged -= TrackDetailsLanguageChanged;
            DetachInsightsPresentation();
            CancelCommentNavigation();
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _waveformAnimations.AnimationsEnabledChanged -= WaveformMotionChanged;
            ViewModel.ClearWaveformHover();
            ClearWaveformCommentEmphasis();
            ClearCoverTransition();
            MusicBorder.SizeChanged -= MusicBorder_SizeChanged;
            _storyViewer?.Close();
            _musicVisual?.StopAnimation("Scale");
            if (_musicVisual is not null) _musicVisual.Scale = Vector3.One;
            _musicSpringVector3NaturalMotionAnimation?.Dispose();
            _musicSpringVector3NaturalMotionAnimation = null;
            _musicVisual = null;
            shadow.Receivers.Clear();
            ResetCoverPreview();
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.ServerWaveformIntensity))
                CreateOrUpdateSpringAnimation(ViewModel.ServerWaveformIntensity);
            else if (e.PropertyName is nameof(ViewModel.Title) or nameof(ViewModel.PersianTitle))
                RefreshTrackTitle();
            else if (e.PropertyName is nameof(ViewModel.SelectedAudioDevice) or nameof(ViewModel.IsAudioDeviceSwitchPending))
            {
                if (e.PropertyName == nameof(ViewModel.SelectedAudioDevice))
                    ObserveSelectedAudioOutput();
                else
                    SyncSelectedOutputVolume();
                SyncAudioOutputMenu();
            }
        }

        private async void Waveform_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!IsMusicDetailsActive || _isCoverPreviewOpen ||
                !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), WaveformScroller)) return;

            if (e.OriginalKey is VirtualKey.GamepadA or VirtualKey.Enter)
            {
                e.Handled = true;
                if (!e.KeyStatus.WasKeyDown)
                {
                    var contentX = WaveformScroller.HorizontalOffset + WaveformScroller.ViewportWidth / 2;
                    await ShowWaveformCommentAsync(contentX);
                }
                return;
            }

            var seconds = e.OriginalKey switch
            {
                VirtualKey.GamepadLeftTrigger or VirtualKey.PageUp => -5,
                VirtualKey.GamepadRightTrigger or VirtualKey.PageDown => 5,
                _ => 0
            };
            if (seconds == 0) return;
            e.Handled = e.KeyStatus.WasKeyDown || ViewModel.SeekRelative(seconds);
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
                    throw new InvalidOperationException(Text("DownloadFolderFailed"));
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(button)
                    ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button);
                peer?.RaiseNotificationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.ActionCompleted,
                    Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.ImportantMostRecent,
                    Text("DownloadCompleted"), "MusicDownload");
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                AppLog.Error("Manual music download failed.", error);
                if (_pageActive && ViewModel.CurrentMusicPublicId == musicId)
                {
                    try
                    {
                        var dialog = new ContentDialog { XamlRoot = XamlRoot };
                        await ShowLocalizedTrackDialogAsync(dialog, () =>
                        {
                            dialog.Title = Text("DownloadFailed");
                            dialog.Content = Text("DownloadRetry");
                            dialog.CloseButtonText = Text("Close");
                        });
                    }
                    catch (Exception dialogError) { AppLog.Error("Download error dialog could not be displayed.", dialogError); }
                }
            }
            finally { button.IsEnabled = true; }
        }
        private void CoverButton_Click(object sender, RoutedEventArgs e) => SetCoverPreview(true, restoreFocus: true);

        private bool SetCoverPreview(bool open, bool restoreFocus = false)
        {
            if (!_pageActive) return false;
            if (open == _isCoverPreviewOpen) return true;
            if (open && (ViewModel.CurrentMusicPublicId != MusicPublicId ||
                         string.IsNullOrWhiteSpace(ViewModel.CoverImagePath))) return false;

            _isCoverPreviewOpen = open;
            StopCoverPreviewClose();
            CancelPreviewAnimation();
            var activeCover = PrepareReturnSource();
            var useMotion = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            SmokeGrid.OpacityTransition = useMotion ? CoverPreviewFade : null;
            if (open)
            {
                var animation = useMotion ? PreparePreviewAnimation(activeCover, "MusicCoverToPreview") : null;
                SmokeGrid.IsHitTestVisible = true;
                SmokeGrid.Visibility = Visibility.Visible;
                SmokeGrid.Opacity = 1;
                CoverPreviewScrollViewer.ChangeView(null, null, 1f, true);
                if (restoreFocus) CloseCoverButton.Focus(FocusState.Programmatic);
                DetailsScroller.IsEnabled = false;
                StartPreviewAnimation(animation, ExpandedCoverImage);
                return true;
            }

            var exit = useMotion ? PreparePreviewAnimation(ExpandedCoverImage, "MusicCoverFromPreview") : null;
            DetailsScroller.IsEnabled = true;
            SmokeGrid.IsHitTestVisible = false;
            SmokeGrid.Opacity = 0;
            if (restoreFocus) CoverButton.Focus(FocusState.Programmatic);
            StartPreviewAnimation(exit, activeCover);
            if (useMotion)
            {
                _coverPreviewCloseTimer = DispatcherQueue.CreateTimer();
                _coverPreviewCloseTimer.IsRepeating = false;
                _coverPreviewCloseTimer.Interval = CoverPreviewFade.Duration;
                _coverPreviewCloseTimer.Tick += CoverPreviewClose_Tick;
                _coverPreviewCloseTimer.Start();
            }
            else SmokeGrid.Visibility = Visibility.Collapsed;
            return true;
        }

        private Image PrepareReturnSource() =>
            _navigationPreviewReady && NavigationCoverImage.Opacity > 0 ? NavigationCoverImage : CoverImage;

        private ConnectedAnimation? PreparePreviewAnimation(Image image, string key)
        {
            if (image.Opacity == 0 || image.Source is null || image.ActualWidth <= 0 || image.ActualHeight <= 0)
                return null;
            var animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(key, image);
            animation.Configuration = new BasicConnectedAnimationConfiguration();
            return animation;
        }

        private static void StartPreviewAnimation(ConnectedAnimation? animation, Image destination)
        {
            if (animation is null) return;
            try
            {
                if (!animation.TryStart(destination)) animation.Cancel();
            }
            catch (COMException error)
            {
                animation.Cancel();
                AppLog.Error("The cover preview animation could not be started.", error);
            }
        }

        private void StopCoverPreviewClose()
        {
            if (_coverPreviewCloseTimer is not { } timer) return;
            _coverPreviewCloseTimer = null;
            timer.Stop();
            timer.Tick -= CoverPreviewClose_Tick;
        }

        private void CoverPreviewClose_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            StopCoverPreviewClose();
            if (!_isCoverPreviewOpen) SmokeGrid.Visibility = Visibility.Collapsed;
        }

        private void CancelPreviewAnimation()
        {
            var animations = ConnectedAnimationService.GetForCurrentView();
            animations.GetAnimation("MusicCoverToPreview")?.Cancel();
            animations.GetAnimation("MusicCoverFromPreview")?.Cancel();
        }

        private void ResetCoverPreview()
        {
            _isCoverPreviewOpen = false;
            StopCoverPreviewClose();
            CancelPreviewAnimation();
            DetailsScroller.IsEnabled = true;
            SmokeGrid.IsHitTestVisible = false;
            SmokeGrid.Visibility = Visibility.Collapsed;
            SmokeGrid.Opacity = 0;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => SetCoverPreview(false, restoreFocus: true);

        private void ResetCoverZoomButton_Click(object sender, RoutedEventArgs e) =>
            CoverPreviewScrollViewer.ChangeView(null, null, 1f, false);

        private void ZoomInCover_Click(object sender, RoutedEventArgs e) => AdjustCoverZoom(1.25f);

        private void ZoomOutCover_Click(object sender, RoutedEventArgs e) => AdjustCoverZoom(0.8f);

        private void AdjustCoverZoom(float factor) => CoverPreviewScrollViewer.ChangeView(null, null,
            Math.Clamp(CoverPreviewScrollViewer.ZoomFactor * factor, CoverPreviewScrollViewer.MinZoomFactor,
                CoverPreviewScrollViewer.MaxZoomFactor), false);

        private void MusicDetails_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var gamepadBack = e.OriginalKey == VirtualKey.GamepadB;
            if (!gamepadBack && e.Key != VirtualKey.Escape) return;
            if (e.KeyStatus.WasKeyDown)
            {
                // A held B can close the preview or go back, but must not do both.
                e.Handled = gamepadBack || _isCoverPreviewOpen;
                return;
            }
            if (_isCoverPreviewOpen)
            {
                e.Handled = SetCoverPreview(false, restoreFocus: true);
                return;
            }
            if (gamepadBack && Frame?.CanGoBack == true)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        private void WaveformTimeline_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_pageActive || e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return;

            var position = e.GetCurrentPoint(WaveformTimeline).Position;
            if (!IsInsideWaveform(position))
            {
                ViewModel.ClearWaveformHover();
                return;
            }

            ViewModel.UpdateWaveformHover(position.X, position.Y >= WaveformSeekRow.ActualHeight);
        }

        private bool IsInsideWaveform(global::Windows.Foundation.Point position) =>
            position.X >= 0 && position.X < WaveformTimeline.ActualWidth &&
            position.Y >= 0 && position.Y < WaveformTimeline.ActualHeight;

        private void WaveformTimeline_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!IsInsideWaveform(e.GetCurrentPoint(WaveformTimeline).Position) ||
                ReferenceEquals(e.OriginalSource, WaveformTimeline))
                ViewModel.ClearWaveformHover();
        }

        private void WaveformScroller_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(WaveformScroller).Position;
            if (ReferenceEquals(e.OriginalSource, WaveformScroller) ||
                point.X < 0 || point.Y < 0 ||
                point.X >= WaveformScroller.ActualWidth || point.Y >= WaveformScroller.ActualHeight)
                ViewModel.ClearWaveformHover();
        }

        private void WaveformTimeline_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
            ViewModel.ClearWaveformHover();

        private void TopEqualizerGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(WaveformTimeline);
            if (!point.Properties.IsLeftButtonPressed) return;

            ViewModel.SeekWaveform(point.Position.X);
            e.Handled = true;
        }

        private async void BottomEqualizerGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(WaveformTimeline);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            e.Handled = true;
            await ShowWaveformCommentAsync(point.Position.X);
        }

        private async Task ShowWaveformCommentAsync(double contentX)
        {
            if (!IsMusicDetailsActive || _waveformCommentDialog is not null ||
                ViewModel.GetWaveformCommentTimestamp(contentX) is not { } timestamp || XamlRoot is not { } root)
                return;

            var musicPublicId = MusicPublicId;
            var textBox = new TextBox
            {
                AcceptsReturn = true,
                MinHeight = 92,
                TextWrapping = TextWrapping.Wrap,
                MaxLength = 1000
            };
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Content = textBox,
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false
            };
            _waveformCommentDialog = dialog;
            textBox.TextChanged += UpdateSubmitState;
            try
            {
                if (await ShowLocalizedTrackDialogAsync(dialog, () =>
                    {
                        var caption = Localization.Format("MusicDetails/WaveformCommentAt", timestamp.ToString(@"h\:mm\:ss"));
                        textBox.PlaceholderText = caption;
                        AutomationProperties.SetName(textBox, caption);
                        dialog.Title = caption;
                        dialog.PrimaryButtonText = Text("PostWaveformComment");
                        dialog.CloseButtonText = Text("Cancel");
                    }) != ContentDialogResult.Primary ||
                    !IsMusicDetailsActive || MusicPublicId != musicPublicId)
                    return;

                var avatar = ((App)App.Current).Services.GetRequiredService<HomePageViewModel>().ProfileImage;
                if (await ViewModel.CreateMusicCommentAsync(musicPublicId, textBox.Text, timestamp, avatar) &&
                    IsMusicDetailsActive && MusicPublicId == musicPublicId)
                    AccessibilityAnnouncements.Announce(WaveformScroller, Text("WaveformCommentPosted", Localization.Revision), "WaveformCommentPosted");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Error("Music comment could not be created.", exception);
            }
            finally
            {
                textBox.TextChanged -= UpdateSubmitState;
                if (ReferenceEquals(_waveformCommentDialog, dialog))
                    _waveformCommentDialog = null;
            }

            void UpdateSubmitState(object sender, TextChangedEventArgs args) =>
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        }

        private async Task<ContentDialogResult> ShowLocalizedTrackDialogAsync(ContentDialog dialog, Action applyText)
        {
            void ApplyLanguage()
            {
                dialog.FontFamily = Localization.TextFontFamily;
                dialog.Language = Localization.LanguageTag;
                applyText();
            }

            void LanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(LocalizationService.Revision))
                    ApplyLanguage();
            }

            ApplyLanguage();
            Localization.PropertyChanged += LanguageChanged;
            try { return await dialog.ShowAsync(); }
            finally { Localization.PropertyChanged -= LanguageChanged; }
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
