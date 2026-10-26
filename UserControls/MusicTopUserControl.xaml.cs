using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Mizon.Windows.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Mizon.Windows.UserControls
{
    public sealed partial class MusicTopUserControl : UserControl
    {
        private MusicTopUserControlViewModel ViewModel => (MusicTopUserControlViewModel)DataContext;

        public MusicTopUserControl()
        {
            InitializeComponent();
            DataContext = ((App)App.Current).Services.GetRequiredService<MusicTopUserControlViewModel>();

            MainStoreCarousel.ItemsSource = new List<Mizon.Windows.Controls.StoreCarouselItem>()
            {
                new()
                {
                    Description="Discover new music every day with personalized recommendations.",
                    ActionButtonText= "ddd",
                    ShowActionButton = true,
                    ImageSource = "ms-appx:///Assets/photo.jpg",
                    Title = "عنوان"
                },
                 new()
                {
                    Description="Discover new music every day with personalized recommendations.",
                    ActionButtonText= "ddd",
                    ShowActionButton = true,
                    ImageSource = "ms-appx:///Assets/cover1.jpg",
                    Title = "عنوان"
                },
                  new()
                {
                    Description="Discover new music every day with personalized recommendations.",
                    ActionButtonText= "ddd",
                    ShowActionButton = true,
                    ImageSource = "ms-appx:///Assets/cover2.jpg",
                    Title = "عنوان"
                }
                  , new()
                {
                    Description="Discover new music every day with personalized recommendations.",
                    ActionButtonText= "ddd",
                    ShowActionButton = true,
                    ImageSource = "ms-appx:///Assets/image1.jpeg",
                    Title = "عنوان"
                }
            };

            
        }

        private void Grid1_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            //Grid1.Opacity = 0.1;
        }

        private void Grid1_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            //Grid1.Opacity = 0.3;
        }

        private void UserControl_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        {
            //if (e.NewSize.Width >= 750)
            //{
            //    MainGrid.MinHeight = 300;
            //    MainGrid.MaxHeight = 400;
            //    MainGrid.ColumnDefinitions.Clear();
            //    MainGrid.RowDefinitions.Clear();

            //    MainGrid.ColumnDefinitions.Add(new()
            //    {
            //        Width = new GridLength(7, GridUnitType.Star),
            //    });

            //    MainGrid.ColumnDefinitions.Add(new()
            //    {
            //        Width = new GridLength(3, GridUnitType.Star),
            //    });

            //    Grid.SetColumn(FirstGrid, 0);
            //    Grid.SetColumn(SecondGrid, 1);

            //    SecondGrid.Margin = new Thickness(8, 0, 0, 0);

            //    SecondGrid.ColumnDefinitions.Clear();
            //    SecondGrid.RowDefinitions.Clear();

            //    SecondGrid.RowDefinitions.Add(new()
            //    {
            //        Height = new GridLength(100, GridUnitType.Star),
            //    });

            //    SecondGrid.RowDefinitions.Add(new()
            //    {
            //        Height = new GridLength(100, GridUnitType.Star),
            //    });

            //    Grid.SetRow(Cover2Grid, 0);
            //    Grid.SetRow(Cover3Grid, 1);

            //    Cover3Grid.Margin = new Thickness(0, 8, 0, 0);
            //}
            //else
            //{
            //    MainGrid.MinHeight = 300;
            //    MainGrid.MaxHeight = 500;
            //    MainGrid.ColumnDefinitions.Clear();
            //    MainGrid.RowDefinitions.Clear();

            //    MainGrid.RowDefinitions.Add(new()
            //    {
            //        Height = new GridLength(7, GridUnitType.Star),
            //    });

            //    MainGrid.RowDefinitions.Add(new()
            //    {
            //        Height = new GridLength(3, GridUnitType.Star),
            //    });

            //    Grid.SetRow(FirstGrid, 0);
            //    Grid.SetRow(SecondGrid, 1);

            //    SecondGrid.Margin = new Thickness(0, 8, 0, 0);

            //    SecondGrid.ColumnDefinitions.Clear();
            //    SecondGrid.RowDefinitions.Clear();

            //    SecondGrid.ColumnDefinitions.Add(new()
            //    {
            //        Width = new GridLength(100, GridUnitType.Star),
            //    });

            //    SecondGrid.ColumnDefinitions.Add(new()
            //    {
            //        Width = new GridLength(100, GridUnitType.Star),
            //    });

            //    Grid.SetColumn(Cover2Grid, 0);
            //    Grid.SetColumn(Cover3Grid, 1);

            //    Cover3Grid.Margin = new Thickness(8, 0, 0, 0);
            //}
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            ShareUIOptions a = new ShareUIOptions()
            {
                Theme = ShareUITheme.Light
            };

            



            var notification = new AppNotificationBuilder()
    .AddText("مردگان متحرک")
    .AddText("سریال مردگان متحرک، ریک گرایمز، معاون کلانتر، از کما خارج می‌شود تا بفهمد جهان در حال نابود شدن است و وظیفه دارد گروهی از بازماندگان را رهبری کند تا زنده بمانند.")
    .SetAppLogoOverride(new Uri("ms-appx:///Assets/photo.jpg"), AppNotificationImageCrop.Circle)
    .SetAudioEvent(AppNotificationSoundEvent.Reminder)

     .SetHeroImage(new Uri("ms-appx:///Assets/Cover2.jpg"), "تست")
.AddProgressBar(new AppNotificationProgressBar()
{
    Title = "شروع دانلود",
    Value = 0.0, // 60%
    ValueStringOverride = "60 درصد",
    Status = "در حال دانلود",

})
.AddButton(new AppNotificationButton("لغو کردن دانلود")
        .AddArgument("action", "submit_survey"))
//.AddCameraPreview()
.SetAttributionText("1080p - Bluray")
.SetTag("12213213")
.SetScenario(AppNotificationScenario.Urgent)
    .SetTimeStamp(DateTime.Now)
    .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            uint s = 1;
            for (double i = 0.0; i < 1.0; i= i+0.1)
            {
                await Task.Delay(1000);
               var asd= await AppNotificationManager.Default.UpdateAsync(new AppNotificationProgressData(s)
                {
                    Value = i,
                    ValueStringOverride = $"{i} درصد",
                    
                }, notification.Tag);
                s++;
            }
        }
    }
}
