using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Mizon.Windows.UserControls;

namespace Mizon.Windows.Pages;

public sealed partial class MusicPlaylistsPage : Page
{
    private bool _departed;
    private MusicLibraryBrowser? _content;
    public MusicPlaylistsPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs args)
    {
        if (_content is not null || _departed) return;
        var content = _content = new MusicLibraryBrowser();
        ContentHost.Children.Add(content);
        await content.OpenAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        _departed = true;
        ReleaseContent();
        base.OnNavigatedFrom(args);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        // The cached Home shell can unload without navigating its inner Frame.
        // Release account data now, then recreate the browser when it returns.
        ReleaseContent();
    }

    private void ReleaseContent()
    {
        _content?.Dispose();
        _content = null;
        ContentHost.Children.Clear();
    }
}
