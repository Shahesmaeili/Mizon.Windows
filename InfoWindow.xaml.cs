using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Windows.Graphics;
using Windows.UI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Mizon.Windows
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class InfoWindow : Window
    {
        public InfoWindow()
        {
            InitializeComponent();

            SystemBackdrop = new TransparentBackdrop()
            {
                //Kind = MicaKind.Base
                
            };

            switch (AppWindow.Presenter)
            {
                case Microsoft.UI.Windowing.OverlappedPresenter overlappedPresenter:
                    //overlappedPresenter.SetBorderAndTitleBar(false, false);
                    //overlappedPresenter.IsMaximizable = false;
                    //overlappedPresenter.IsResizable = true;
                    //overlappedPresenter.IsMinimizable = false;

                    overlappedPresenter.PreferredMaximumWidth = 400;
                    overlappedPresenter.PreferredMaximumHeight = 150;
                    overlappedPresenter.IsResizable = false;
                    //overlappedPresenter.Maximize();
                    break;
            }

            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
            AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Collapsed;
            PositionWindowBottomRight();
            WindowNativeInterop.RemoveWindowBorder(this);
            WindowNativeInterop.HideFromTaskbar(this);


        }


        private void PositionWindowBottomRight()
        {
            // اندازه صفحه اصلی
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var screenWidth = displayArea.WorkArea.Width;
            var screenHeight = displayArea.WorkArea.Height;
            
            // اندازه پنجره
            var windowWidth = AppWindow.Size.Width;
            var windowHeight = AppWindow.Size.Height;

            // تعیین موقعیت: پایین سمت راست
            AppWindow.Move(new PointInt32(
                (int)(screenWidth - windowWidth),
                (int)(screenHeight - windowHeight)
            ));
        }
    }
}
