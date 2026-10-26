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
        private Compositor _compositor = App.MainMizonWindow!.Compositor;
        private SpringVector3NaturalMotionAnimation _musicSpringVector3NaturalMotionAnimation;
        private Visual _musicVisual;
    public MusicsPage()
        {
            this.InitializeComponent();
            DataContext = ((App)App.Current).Services.GetRequiredService<MusicPageViewModel>();
            ViewModel.NavigationService.InternalNavigationChanged += (s, args) =>
            {
                if (args.IsEnteringPage && args.ToPageType == typeof(MusicsPage))
                {
                    var connectedAnimationService = ConnectedAnimationService.GetForCurrentView();
                    var connectedAnimation = connectedAnimationService.GetAnimation("MusicCover");
                    connectedAnimation?.TryStart(CoverImage);
                }
            };
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
            await ViewModel.SelectAlbumDetailsAsync();
        }

        private async void ContributorDetailsTab_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            await ViewModel.SelectContributorDetailsAsync();
        }

        private async void CommentsDetailsTab_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
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
            if (float.IsNaN(rms) || float.IsInfinity(rms))
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







        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyButtonHalfCircleClip();
            });

            ViewModel.EqualizerComments.CollectionChanged += EqualizerComments_CollectionChanged;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;


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

        private void EqualizerComments_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {

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


            // Initialize TaskbarProgress
            //var hwnd = WindowNative.GetWindowHandle(App.MainMizonWindow);
            //TaskbarProgress.Initialize(hwnd);
            //TaskbarProgress.Clear();
            //TaskbarProgress.SetState(TaskbarProgress.TBPFLAG.TBPF_NORMAL);

            //TaskbarProgress.SetState(TaskbarProgress.TBPFLAG.TBPF_INDETERMINATE);

            //await Task.Delay(5000);

            //TaskbarProgress.SetValue(50, 100);
            //await Task.Delay(5000);
            //TaskbarProgress.SetValue(60, 100);

            //// تنظیم حالت پیشرفت
            //TaskbarProgress.SetState( TaskbarProgress.TBPFLAG.TBPF_NORMAL);
            //await Task.Delay(5000);
            //await Task.Delay(5000);
            //TaskbarProgress.SetState(TaskbarProgress.TBPFLAG.TBPF_PAUSED);
            //await Task.Delay(5000);
            //TaskbarProgress.SetState(TaskbarProgress.TBPFLAG.TBPF_ERROR);
            //await Task.Delay(5000);
            //TaskbarProgress.SetState(TaskbarProgress.TBPFLAG.TBPF_NOPROGRESS);
            //await Task.Delay(5000);
            //TaskbarProgress.SetValue(10, 100);

            // تنظیم مقدار پیشرفت (50% در این مثال)
            //taskbar.HrInit();

            //var hwnd = WindowNative.GetWindowHandle(App.MainMizonWindow);

            //// فعال کردن حالت پروگرس
            //taskbar.SetProgressState(hwnd, TBPFLAG.TBPF_NORMAL);

            //// مقدار پروگرس (مثلاً 40 از 100)
            //taskbar.SetProgressValue(hwnd, 40, 100);
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

        private async Task ApplyButtonHalfCircleClip()
        {
            await Task.Delay(2000);
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
            var fullRect = CanvasGeometry.CreateRectangle(device, 0, 0, width, height);

            // ۲. ایجاد دایره برای حفره
            var circle = CanvasGeometry.CreateCircle(device, 48, 52, 20);

            // ۳. دایره را از مستطیل حذف کنید (Exclude = ایجاد سوراخ)
            var geometryWithHole = fullRect.CombineWith(
                circle,
                Matrix3x2.Identity,
                CanvasGeometryCombine.Exclude);

            var compositionPath = new CompositionPath(geometryWithHole);
            var pathGeometry = compositor.CreatePathGeometry();
            pathGeometry.Path = compositionPath;

            visual.Clip = compositor.CreateGeometricClip(pathGeometry);
        }


        private async Task ApplyLikeButtonHalfCircleClip()
        {

            var visual = ElementCompositionPreview.GetElementVisual(LikesButton);
            var compositor = visual.Compositor;

            float width = (float)LikesButton.ActualWidth;
            float height = (float)LikesButton.ActualHeight;


            var device = CanvasDevice.GetSharedDevice();

            // ۱. ایجاد مستطیل کامل عکس
            var fullRect = CanvasGeometry.CreateRectangle(device, 0, 0, width, height);

            // ۲. ایجاد دایره برای حفره
            var circle = CanvasGeometry.CreateCircle(device, -16, height / 2, 24);

            // ۳. دایره را از مستطیل حذف کنید (Exclude = ایجاد سوراخ)
            var geometryWithHole = fullRect.CombineWith(
                circle,
                Matrix3x2.Identity,
                CanvasGeometryCombine.Exclude);

            var compositionPath = new CompositionPath(geometryWithHole);
            var pathGeometry = compositor.CreatePathGeometry();
            pathGeometry.Path = compositionPath;

            visual.Clip = compositor.CreateGeometricClip(pathGeometry);
        }

        private void MusicBorder_Loaded(object sender, RoutedEventArgs e)
        {
            shadow.Receivers.Add(ShadowCastGrid);

            _musicVisual = ElementCompositionPreview.GetElementVisual(MusicBorder);
            _compositor = _musicVisual.Compositor;

            _musicVisual.CenterPoint = new Vector3(
                (float)MusicBorder.ActualWidth / 2f,
                (float)MusicBorder.ActualHeight / 2f,
                0f
            );
            _musicVisual.Scale = Vector3.One;

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
            if ((sender as FrameworkElement)?.Tag is not StoryItemViewModel story)
                return;

            var detail = await ViewModel.GetStoryDetailsAsync(story);
            if (detail is null)
                return;

            await ViewModel.MarkStoryAsViewedAsync(story);
            var contentSource = await ViewModel.PrepareStoryPlaybackAsync(detail.ContentMediaPublicId);
            if (string.IsNullOrWhiteSpace(contentSource) || !Uri.TryCreate(contentSource, UriKind.Absolute, out var contentUri))
                return;

            FrameworkElement content = string.Equals(detail.StoryContentTypeTitle, "Video", StringComparison.OrdinalIgnoreCase)
                ? new MediaPlayerElement { Source = global::Windows.Media.Core.MediaSource.CreateFromUri(contentUri), AutoPlay = true, AreTransportControlsEnabled = true, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill }
                : new Image { Source = new BitmapImage(contentUri), Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };

            await new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = story.CreatorDisplayName,
                CloseButtonText = "بستن",
                Content = new Grid { Width = 360, Height = 640, Children = { content } },
            }.ShowAsync();
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

        private bool _isWide;

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool newWide = e.NewSize.Width > 1000;
            if (newWide == _isWide) return;
            _isWide = newWide;

            TopGrid.ColumnDefinitions.Clear();
            TopGrid.RowDefinitions.Clear();

            if (_isWide)
            {
                TopGrid.ColumnDefinitions.Add(new()
                {
                    Width = new GridLength(1, GridUnitType.Auto),
                });
                TopGrid.ColumnDefinitions.Add(new()
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });
                TopGrid.ColumnDefinitions.Add(new()
                {
                    Width = new GridLength(1, GridUnitType.Auto),
                });

                Grid.SetColumn(ShadowCastGrid, 0);
                Grid.SetColumn(MusicBorder, 0);

                Grid.SetColumn(MusicInforationGrid, 1);
                Grid.SetColumn(VolumeSlider, 2);
            }
            else
            {
                TopGrid.RowDefinitions.Add(new()
                {
                    Height = new GridLength(1, GridUnitType.Auto),
                });
                TopGrid.RowDefinitions.Add(new()
                {
                    Height = new GridLength(1, GridUnitType.Auto),
                });

                Grid.SetRow(ShadowCastGrid, 0);
                Grid.SetRow(MusicBorder, 0);

                Grid.SetRow(MusicInforationGrid, 1);
                Grid.SetRow(VolumeSlider, 1);
            }
        }

    }
}

