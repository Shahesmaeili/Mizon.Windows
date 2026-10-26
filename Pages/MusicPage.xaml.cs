using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Mizon.Windows.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MusicPage : Page
    {

        IList<string> colors = new List<string>()
        {
            "Daily Mix — انتخاب‌شده برای تو",
            "آرامش شبانه — ۴۲ دقیقه",
            "ترک‌های ترند امروز",
            "تمرکز عمیق — بدون کلام",
            "کشف‌های این هفته",
            "سفر جاده‌ای — ۲۸ آهنگ"
        };

        private double AnimatedBtnHeight;
        private Microsoft.UI.Xaml.Thickness AnimatedBtnMargin;
        private MusicPageViewModel ViewModel => (MusicPageViewModel)DataContext;

        public MusicPage()
        {
            this.InitializeComponent();
            animatedScrollRepeater.ItemsSource = colors;
            animatedScrollRepeater.ElementPrepared += OnElementPrepared;
            DataContext = ((App)App.Current).Services.GetRequiredService<MusicPageViewModel>();

            ViewModel.NavigationService.InternalNavigationChanged += (s, args) =>
            {
                if (!args.IsEnteringPage && args.ToPageType == typeof(MusicsPage) && args.FromPageType == typeof(MusicPage))
                {
                    try
                    {
                        var connectedAnimationService = ConnectedAnimationService.GetForCurrentView();
                       // connectedAnimationService.DefaultDuration = TimeSpan.FromMilliseconds(500);
                        connectedAnimationService.PrepareToAnimate("MusicCover", ShadowRect);
                    }
                    finally
                    {

                    }
                  
                }
            };

        }

        /* This function occurs each time an element is made ready for use.
* This is necessary for virtualization. */
        private void OnElementPrepared(Microsoft.UI.Xaml.Controls.ItemsRepeater sender,
                                       Microsoft.UI.Xaml.Controls.ItemsRepeaterElementPreparedEventArgs args)
        {
            var item = ElementCompositionPreview.GetElementVisual(args.Element);
            var svVisual = ElementCompositionPreview.GetElementVisual(Animated_ScrollViewer);
            var scrollProperties = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(Animated_ScrollViewer);

            var scaleExpresion = scrollProperties.Compositor.CreateExpressionAnimation();
            scaleExpresion.SetReferenceParameter("svVisual", svVisual);
            scaleExpresion.SetReferenceParameter("scrollProperties", scrollProperties);
            scaleExpresion.SetReferenceParameter("item", item);

            // Scale the item based on the distance of the item relative to the center of the viewport.
            scaleExpresion.Expression = "1 - abs((svVisual.Size.Y/2 - scrollProperties.Translation.Y) - (item.Offset.Y + item.Size.Y / 2))*(.25 / (svVisual.Size.Y / 2))";
        
    // Animate the item based on its distance to the center of the viewport.
    item.StartAnimation("Scale.X", scaleExpresion);
            item.StartAnimation("Scale.Y", scaleExpresion);
            var centerPointExpression = scrollProperties.Compositor.CreateExpressionAnimation();
            centerPointExpression.SetReferenceParameter("item", item);
            centerPointExpression.Expression = "Vector3(item.Size.X/2, item.Size.Y/2, 0)";
            item.StartAnimation("CenterPoint", centerPointExpression);
        }

        /* This function identifies the item that's currently in the middle of the viewport,
        and sets the rectangle to match its color. */
        private void Animated_ScrollViewer_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            Button SelectedItem = GetSelectedItemFromViewport() as Button;
            // Update corresponding rectangle with selected color
           // colorRectangle.Fill = SelectedItem.Background;
        }

        // The remainder of these functions are helper functions for the ViewChanging function:

        // Find centerpoint of ScrollViewer
        private double CenterPointOfViewportInExtent()
        {
            return Animated_ScrollViewer.VerticalOffset + Animated_ScrollViewer.ViewportHeight / 2;
        }

        // Find index of the item that's at the center of the viewport
        private int GetSelectedIndexFromViewport()
        {
            int selectedItemIndex = (int)Math.Floor(CenterPointOfViewportInExtent()
                                      / ((double)AnimatedBtnMargin.Top + AnimatedBtnHeight));
            selectedItemIndex %= animatedScrollRepeater.ItemsSourceView.Count;
            return selectedItemIndex;
        }

        // Return item that's at the center of the viewport.
        private object GetSelectedItemFromViewport()
        {
            var selectedIndex = GetSelectedIndexFromViewport();
            var selectedElement = animatedScrollRepeater.TryGetElement(selectedIndex) as Button;
            return selectedElement;
        }


        private void ShadowRect_Loaded(object sender, RoutedEventArgs e)
        {
            shadow.Receivers.Add(ShadowCastGrid);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

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
                : new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(contentUri), Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = story.CreatorDisplayName,
                CloseButtonText = "بستن",
                Content = new Grid
                {
                    Width = 360,
                    Height = 640,
                    Children = { content },
                },
            };
            await dialog.ShowAsync();
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



        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton selectedMood)
                return;

            if (selectedMood.IsChecked != true)
            {
                selectedMood.IsChecked = true;
                return;
            }

            foreach (UIElement child in QuickMoodGrid.Children)
            {
                if (child is ToggleButton mood && mood != selectedMood)
                    mood.IsChecked = false;
            }
        }

        private void ShadowRect_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ViewModel.NavigationService.InternalNavigate(typeof(MusicsPage));
        }

        //private async void MainRefreshContainer_RefreshRequested(RefreshContainer sender, RefreshRequestedEventArgs args)
        //{
        //    var deferral = args.GetDeferral();
        //    await Task.Delay(3000);
        //    deferral.Complete();
        //}

        //private void RefreshVisualizer_RefreshStateChanged(RefreshVisualizer sender, RefreshStateChangedEventArgs args)
        //{
        //    var visualizerContentVisual = ElementCompositionPreview.GetElementVisual((UIElement)MainRefreshContainer.Content);
        //    visualizerContentVisual.StopAnimation("RotationAngle");

        //}
    }
}

