using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Common;
using Mizon.Windows.ViewModels;
using Windows.System;


namespace Mizon.Windows.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class LoginPage : Page
    {
        private LoginPageViewModel ViewModel => (LoginPageViewModel)DataContext;
        public LoginPage()
        {
            this.InitializeComponent();
            DataContext = ((App)App.Current).Services.GetRequiredService<LoginPageViewModel>();
        }

        private void Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.LoginCommand.Execute();
        }

        private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            //_ = ViewModel.JumpListService.ConfigureLoginRegisterJumpListAsync();
        }

        private void HyperlinkButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            MainFlipSide.IsFlipped = !MainFlipSide.IsFlipped;
        }

        private void PasswordBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (ViewModel.LoginButtonIsEnabled)
                if (e.Key is VirtualKey.Enter)
                    ViewModel.LoginCommand.Execute();
        }

        private void TextBox_GotFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            KeyboardLayoutNativeIntrop.SwitchToEnglish();
        }
    }
}
