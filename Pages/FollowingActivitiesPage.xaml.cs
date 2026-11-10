using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;

namespace Mizon.Windows.Pages;

// This is the shared activity destination. Only the home widget applies a music filter.
public sealed partial class FollowingActivitiesPage : Page
{
    private readonly MizonApi _api;
    private readonly MediaDownloader _downloader;
    private readonly NavigationService _navigation;
    private bool _active;
    private bool _departed;
    private bool _loadingMore;
    private FollowingActivityStoryPresenter? _storyPresenter;
    public FollowingActivitiesViewModel? ViewModel { get; private set; }

    public FollowingActivitiesPage()
    {
        var services = ((App)Application.Current).Services;
        _api = services.GetRequiredService<MizonApi>();
        _downloader = services.GetRequiredService<MediaDownloader>();
        _navigation = services.GetRequiredService<NavigationService>();
        ViewModel = new(_api, _downloader, "all", 20);
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs args)
    {
        if (_active || _departed) return;
        _active = true;
        if (ViewModel is null)
        {
            ViewModel = new(_api, _downloader, "all", 20);
            Bindings.Update();
            LoadingHost.ResetPresentation();
        }
        await ViewModel.RefreshAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        Release();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        _departed = true;
        Release();
        base.OnNavigatedFrom(args);
    }

    private void Release()
    {
        _active = false;
        _storyPresenter?.Dispose();
        _storyPresenter = null;
        Bindings.StopTracking();
        ViewModel?.Dispose();
        ViewModel = null;
    }

    private async void Activity_Click(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is FriendMusicActivityItemViewModel activity)
        {
            var container = (sender as ListView)?.ContainerFromItem(activity);
            await FollowingActivityNavigation.OpenAsync(activity, _navigation,
                () => _storyPresenter ??= new(), FollowingActivityNavigation.FindCover(container));
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel is not { IsLoading: false } viewModel) return;
        _loadingMore = false;
        await viewModel.RefreshAsync();
    }

    private async void More_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel is not { IsLoading: false, HasMore: true } viewModel) return;
        _loadingMore = true;
        await viewModel.LoadMoreAsync();
    }

    private async void Retry_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel is not { IsLoading: false } viewModel) return;
        if (_loadingMore) await viewModel.LoadMoreAsync();
        else await viewModel.RefreshAsync();
    }
}
