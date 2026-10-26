using DevWinUI;
using Microsoft.UI.Xaml;
using Mizon.API;
using Mizon.Windows.Services;
using Windows.UI;

namespace Mizon.Windows.ViewModels;

public class MainViewModel : ViewModelBase
{
    public readonly IAudioPlayerService _audioPlayerService;

    protected MizonApi MizonApi { get; }

    protected MizonSocket MizonSocket { get; }

    public NavigationService NavigationService { get; }

    private Window? _window { get; set; }

    private RainbowFrame? _rainbowFrame { get; set; }

    public MainViewModel(MizonApi mizonApi,
        MizonSocket mizonSocket,
        NavigationService navigationService,
        IAudioPlayerService audioPlayerService)
    {
        MizonApi = mizonApi;
        MizonSocket = mizonSocket;
        NavigationService = navigationService;
        _audioPlayerService = audioPlayerService;
    }




    public void InitializeMainWindow(Window window)
    {
        _window = window;
        _rainbowFrame = new RainbowFrame(window);
    }

    public void ChangeWindowBorderColor(Color color)
    {
        _rainbowFrame!.ChangeFrameColor(color);
    }
}
