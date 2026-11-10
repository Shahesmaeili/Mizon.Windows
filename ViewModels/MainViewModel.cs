using System;
using DevWinUI;
using Microsoft.UI.Xaml;
using Mizon.API;
using Mizon.Windows.Services;
using Windows.UI;

namespace Mizon.Windows.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public readonly IAudioPlayerService _audioPlayerService;

    protected MizonApi MizonApi { get; }

    private readonly Lazy<MizonSocket> _socket;
    protected MizonSocket MizonSocket => _socket.Value;

    public NavigationService NavigationService { get; }

    private Window? _window { get; set; }

    private RainbowFrame? _rainbowFrame { get; set; }

    public MainViewModel(MizonApi mizonApi,
        Lazy<MizonSocket> mizonSocket,
        NavigationService navigationService,
        IAudioPlayerService audioPlayerService)
    {
        MizonApi = mizonApi;
        _socket = mizonSocket;
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
        if (_window is not null)
            _ = Common.WindowNativeInterop.AnimateWindowBorderColor(_window, color);
    }
}
