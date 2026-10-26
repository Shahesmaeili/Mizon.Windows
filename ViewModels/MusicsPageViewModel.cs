using Mizon.API;
using Mizon.Windows.Services;

namespace Mizon.Windows.ViewModels;

public class MusicsPageViewModel : MainViewModel
{
    public MusicsPageViewModel(MizonApi mizonApi,
        MizonSocket mizonSocket,
        NavigationService navigationService,
        IAudioPlayerService audioPlayerService) : base(mizonApi, mizonSocket, navigationService, audioPlayerService)
    {
        NavigationService.MainNavigationChanged += NavigationService_NavigationChanged;

    }



    private void NavigationService_NavigationChanged(object? sender, NavigationChangedEventArgs e)
    {
        if (e.IsEnteringPage)
        {

        }
    }




}
