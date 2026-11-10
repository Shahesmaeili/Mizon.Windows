using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Mizon.Windows.UserControls;
using Mizon.Windows.Services;
using Mizon.API;
using System;
using System.Collections.Generic;

namespace Mizon.Windows.Pages;

public sealed partial class MusicPlaylistPage : Page
{
    private readonly MizonApi _api;
    private readonly NavigationService _navigation;
    private bool _departed, _preserveContent, _observingNavigation;
    private string? _owner;
    private MusicCoverTransition? _returnTransition;
    private Guid _returnMusic;
    private MusicLibraryBrowser? _content;
    public Guid PlaylistPublicId { get; private set; }
    public MusicPlaylistPage()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        _api = services.GetRequiredService<MizonApi>();
        _navigation = services.GetRequiredService<NavigationService>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        _departed = false;
        _returnTransition?.Dispose(); _returnTransition = null;
        _returnMusic = Guid.Empty;
        if (args.Parameter is not Dictionary<string, object> parameters) return;
        if (parameters.TryGetValue(PageConstants.MUSIC_PLAYLIST_ID, out var value) && value is Guid id)
        {
            if (id != PlaylistPublicId || _owner != _api.GetToken()) ReleaseContent();
            PlaylistPublicId = id;
        }
        if (parameters.Remove(MusicCoverTransition.ParameterKey, out var payload))
            _returnTransition = payload as MusicCoverTransition;
        if (parameters.Remove(MusicCoverTransition.ReturnMusicKey, out var music) && music is Guid musicId)
            _returnMusic = musicId;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs args)
    {
        if (_departed) return;
        if (_content is null)
        {
            _returnTransition?.Dispose(); _returnTransition = null;
            _owner = _api.GetToken();
            _navigation.MainNavigationChanged += MainNavigationChanged;
            _navigation.InternalNavigationChanged += InternalNavigationChanged;
            _observingNavigation = true;
            var content = _content = new MusicLibraryBrowser();
            ContentHost.Children.Add(content);
            await content.OpenAsync(PlaylistPublicId);
        }
        else
        {
            var transition = _returnTransition;
            _returnTransition = null;
            await _content.RestoreMusicPositionAsync(transition, _returnMusic);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        _departed = true;
        _returnTransition?.Dispose(); _returnTransition = null;
        _preserveContent = args.SourcePageType == typeof(MusicsPage) && _content?.Details.HasLoaded == true;
        if (_preserveContent) _content!.SaveMusicPosition();
        else ReleaseContent();
        base.OnNavigatedFrom(args);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded && !_preserveContent) ReleaseContent();
    }

    private void MainNavigationChanged(object? sender, NavigationChangedEventArgs args)
    {
        if (!args.IsEnteringPage && args.FromPageType == typeof(HomePage)) ReleaseContent();
    }

    private void InternalNavigationChanged(object? sender, NavigationChangedEventArgs args)
    {
        if (_departed && args.IsEnteringPage && args.ToPageType != typeof(MusicsPage)
            && args.ToPageType != typeof(MusicPlaylistPage)) ReleaseContent();
    }

    private void ReleaseContent()
    {
        if (_observingNavigation)
        {
            _navigation.MainNavigationChanged -= MainNavigationChanged;
            _navigation.InternalNavigationChanged -= InternalNavigationChanged;
            _observingNavigation = false;
        }
        _returnTransition?.Dispose(); _returnTransition = null;
        _preserveContent = false;
        _content?.Dispose();
        _content = null;
        _owner = null;
        ContentHost.Children.Clear();
    }
}
