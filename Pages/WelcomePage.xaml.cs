using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Mizon.Windows.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class WelcomePage : Page
    {
        private WelcomePageViewModel ViewModel => (WelcomePageViewModel)DataContext;

        private List<DotInfo> _dots = new List<DotInfo>();
        private DispatcherTimer _animationTimer;
        private readonly Random _random = new Random();
        private readonly Point _center = new Point(200, 200);

        public WelcomePage()
        {
            this.InitializeComponent();
            DataContext = ((App)App.Current).Services.GetRequiredService<WelcomePageViewModel>();
            ViewModel.SetStatusTextBlock(StatusTextBlock);
            ViewModel.SetStackPanel(StatusStackPanel);
            ViewModel.NavigationService.MainNavigationChanged += (s, args) =>
            {
                if (!args.IsEnteringPage
                    && args.FromPageType == typeof(WelcomePage)
                    && args.ToPageType == typeof(HomePage))
                {
                    var connectedAnimationService = ConnectedAnimationService.GetForCurrentView();
                    connectedAnimationService.PrepareToAnimate("ProfilePictureAnimation", ProfileBorder);
                }
            };
            this.Loaded += OnLoaded;
        }


        // کلاس برای نگهداری اطلاعات هر نقطه
        private class DotInfo
        {
            public Ellipse Dot { get; set; }
            public double Radius { get; set; }
            public double CurrentAngle { get; set; }
            public double Speed { get; set; }
            public double InitialAngle { get; set; }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CreateAnimatedDots();
            StartAnimation();

            _ = new JumpListService().ConfigureLoginRegisterJumpListAsync();



        }

        private void CreateAnimatedDots()
        {
            // ایجاد 8 لایه با شعاع های مختلف
            for (int layer = 0; layer < 8; layer++)
            {
                double radius = 80 + (layer * 25); // شعاع هر لایه
                double opacity = Math.Max(0.2, 1.0 - (layer * 0.1)); // کاهش شفافیت
                double dotSize = Math.Max(3, 12 - (layer * 1.2)); // کاهش اندازه نقاط

                // تعداد نقاط رندوم برای هر لایه (1 تا 5)
                int dotCount = _random.Next(2, 8);

                for (int i = 0; i < dotCount; i++)
                {
                    CreateDot(radius, opacity, dotSize, layer);
                }
            }
        }

        private void CreateDot(double radius, double opacity, double dotSize, int layer)
        {
            // ایجاد نقطه با رنگ SystemAccentColor
            Ellipse dot = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Fill = GetAccentColorBrush(layer),
                Opacity = opacity
            };

            AnimationCanvas.Children.Add(dot);

            // اطلاعات نقطه برای انیمیشن
            DotInfo dotInfo = new DotInfo
            {
                Dot = dot,
                Radius = radius,
                CurrentAngle = _random.NextDouble() * 2 * Math.PI, // زاویه اولیه رندوم
                Speed = (0.02 + _random.NextDouble() * 0.01) * (1.0 / (1 + layer * 0.2)), // سرعت متفاوت
                InitialAngle = _random.NextDouble() * 2 * Math.PI
            };

            _dots.Add(dotInfo);
        }

        private void StartAnimation()
        {
            _animationTimer = new DispatcherTimer();
            _animationTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
            _animationTimer.Tick += AnimationTimer_Tick;
            _animationTimer.Start();
        }

        private void AnimationTimer_Tick(object sender, object e)
        {
            foreach (var dotInfo in _dots)
            {
                // به‌روزرسانی زاویه
                dotInfo.CurrentAngle += dotInfo.Speed;

                // محاسبه موقعیت جدید
                double x = _center.X + dotInfo.Radius * Math.Cos(dotInfo.CurrentAngle) - dotInfo.Dot.Width / 2;
                double y = _center.Y + dotInfo.Radius * Math.Sin(dotInfo.CurrentAngle) - dotInfo.Dot.Height / 2;

                // تنظیم موقعیت
                Canvas.SetLeft(dotInfo.Dot, x);
                Canvas.SetTop(dotInfo.Dot, y);

                // بازگردانی زاویه به محدوده 0-2π
                if (dotInfo.CurrentAngle > 2 * Math.PI)
                    dotInfo.CurrentAngle -= 2 * Math.PI;
            }
        }

        private SolidColorBrush GetAccentColorBrush(int layer)
        {
            // دریافت رنگ SystemAccentColor از منابع تم
            if (Application.Current.Resources.TryGetValue("SystemAccentColor", out object accentColorObj) &&
                accentColorObj is Color accentColor)
            {
                // کم‌رنگ کردن بر اساس لایه
                double factor = 1.0 - (layer * 0.12); // هرچی لایه بالاتر، کم‌رنگ‌تر
                factor = Math.Max(0.2, factor); // حداقل 20% روشنایی

                byte r = (byte)(accentColor.R * factor);
                byte g = (byte)(accentColor.G * factor);
                byte b = (byte)(accentColor.B * factor);

                return new SolidColorBrush(Color.FromArgb(accentColor.A, r, g, b));
            }

            // در صورت عدم دسترسی به SystemAccentColor، رنگ پیش‌فرض آبی
            double defaultFactor = 1.0 - (layer * 0.12);
            defaultFactor = Math.Max(0.2, defaultFactor);

            byte defaultR = (byte)(0 * defaultFactor);
            byte defaultG = (byte)(120 * defaultFactor);
            byte defaultB = (byte)(215 * defaultFactor);

            return new SolidColorBrush(Color.FromArgb(255, defaultR, defaultG, defaultB));
        }

        // متد برای توقف انیمیشن ها
        public void StopAnimations()
        {
            _animationTimer?.Stop();
        }

        // متد برای شروع مجدد انیمیشن ها
        public void StartAnimations()
        {
            if (_animationTimer != null && !_animationTimer.IsEnabled)
            {
                _animationTimer.Start();
            }
        }

        // تمیز کردن منابع
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _animationTimer?.Stop();
            _animationTimer = null;
        }
    }
}
