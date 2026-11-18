using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Mizon.Windows.UserControls;

using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class MusicPlaylistsPage : Page
{
    private Task? _contentLoad;
    private bool _departed;
    private MusicLibraryBrowser? _content;
    public MusicPlaylistsPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs args)
    {
        if (_content is not null || _departed) return;
        var content = _content = new MusicLibraryBrowser();
        ContentHost.Children.Add(content);
        _contentLoad = content.OpenAsync();
        await _contentLoad;
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

    internal async Task<bool> WaitForContentAsync(CancellationToken token)
    {
        var content = _content;
        var load = _contentLoad;
        if (content is null || load is null) return false;
        await load.WaitAsync(token);
        return ReferenceEquals(content, _content) && content.Library.HasLoaded && !content.Library.HasError;
    }

    private void ReleaseContent()
    {
        _contentLoad = null;
        _content?.Dispose();
        _content = null;
        ContentHost.Children.Clear();
    }
}
