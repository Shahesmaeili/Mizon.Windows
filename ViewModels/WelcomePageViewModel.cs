using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.BadgeNotifications;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public class WelcomePageViewModel : MainViewModel
{
    private TextBlock? _statusTextBlock;
    private StackPanel? _statusStackPanel;
    private readonly LocalStorageService _localStorageService;

    public WelcomePageViewModel(MizonApi mizonApi,
    MizonSocket mizonSocket,
    NavigationService navigationService,
     IAudioPlayerService audioPlayerService,
     LocalStorageService localStorageService)
    : base(mizonApi, mizonSocket, navigationService, audioPlayerService)
    {
        NavigationService.MainNavigationChanged += OnNavigationChanged;
        _localStorageService = localStorageService;
    }

    public Visibility ProfileShimmerVisibility
    {
        get => field;
        set => SetField(ref field, value);
    } = Visibility.Visible;

    public string ProfileImageURL
    {
        get => field;
        set => SetField(ref field, value);
    } = string.Empty;

    public void SetStatusTextBlock(TextBlock statusTextBlock)
    {
        _statusTextBlock = statusTextBlock;
    }

    public void SetStackPanel(StackPanel statusStackPanel)
    {
        _statusStackPanel = statusStackPanel;
    }

    private async void OnNavigationChanged(object? sender, NavigationChangedEventArgs e)
    {
        if (e.IsEnteringPage && e.ToPageType == typeof(WelcomePage))
        {
            await InitializeAsync(CancellationToken.None);
        }
    }



    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        BadgeNotificationManager.Current.SetBadgeAsGlyph(BadgeNotificationGlyph.Activity);
        await GetMeAsync(cancellationToken);

        ProfileImageURL = "ms-appx:///Assets/photo.jpg";
        ProfileShimmerVisibility = Visibility.Collapsed;

        await Task.Delay(1000);

        _statusStackPanel!.Visibility = Visibility.Collapsed;
        await Task.Delay(500);

        _statusTextBlock!.Text = "در حال چک کردن نسخه برنامه";
        _statusStackPanel.Visibility = Visibility.Visible;
        await Task.Delay(3000);




        NavigationService.MainNavigate(typeof(HomePage));

        BadgeNotificationManager.Current.ClearBadge();

        ElementSoundPlayer.State = ElementSoundPlayerState.On;
        ElementSoundPlayer.Play(ElementSoundKind.Show);
        // ElementSoundPlayer.State = ElementSoundPlayerState.Off;

    }


    private async Task GetMeAsync(CancellationToken cancellationToken)
    {
        var baseApiResponse = await MizonApi.SendRequestAsync(new MeOperation(new())
            , cancellationToken);

        if (baseApiResponse.IsSuccess)
        {
         
        }
        else if (baseApiResponse.Error?.Code == 401)
        {
            _localStorageService.SetJwtToken(null);
            NavigationService.MainNavigate(typeof(LoginPage));
        }

    }


}
