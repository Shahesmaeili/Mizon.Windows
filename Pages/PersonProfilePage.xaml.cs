using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;

namespace Mizon.Windows.Pages;

public sealed partial class PersonProfilePage : Page, IMcpPersonProfilePage
{
    private readonly PersonProfileViewModel _viewModel;
    private readonly NavigationService _navigation;
    private readonly MizonApi _api;
    private readonly MediaDownloader _downloader;
    private readonly Dictionary<Image, CancellationTokenSource> _coverRequests = [];
    private static readonly SemaphoreSlim CoverSlots = new(4, 4);
    private CancellationTokenSource? _lifetime;
    private bool _loadingProfile, _loadingTracks, _followBusy, _tracksFailed;
    private string? _profileError, _generalStatus;
    private string _modelStatus = "NoModel";
    private int _layoutBand = -1;
    private PersonProfileEntrance? _portraitEntrance;
    private ImageSource? _pendingAvatar;
    private TaskCompletionSource<bool> _ready = NewCompletion();

    public Guid PersonPublicId { get; private set; }
    public LocalizationService Localization => LocalizationService.Current;

    public PersonProfilePage()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        _viewModel = services.GetRequiredService<PersonProfileViewModel>();
        _navigation = services.GetRequiredService<NavigationService>();
        _api = services.GetRequiredService<MizonApi>();
        _downloader = services.GetRequiredService<MediaDownloader>();
        TracksList.ItemsSource = _viewModel.Tracks;
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<bool> WaitForProfileAsync(CancellationToken token) => _ready.Task.WaitAsync(token);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        PersonPublicId = e.Parameter is Dictionary<string, object> args
            && args.TryGetValue(PageConstants.PERSON_ID, out var value) && value is Guid id ? id : Guid.Empty;
        if (e.Parameter is Dictionary<string, object> parameters && parameters.Remove(PersonProfileEntrance.Key, out var entrance))
            _portraitEntrance = entrance as PersonProfileEntrance;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        EndPage();
        base.OnNavigatedFrom(e);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_lifetime is not null) return;
        _lifetime = new();
        Localization.PropertyChanged += LanguageChanged;
        Bindings.Update();
        RefreshText();
        if (_portraitEntrance is { } entrance)
        {
            ArtistName.Text = Avatar.DisplayName = entrance.DisplayName;
            Avatar.ProfilePicture = entrance.Portrait;
            Avatar.LayoutUpdated += StartPortraitEntrance;
            Hero.Visibility = Visibility.Visible;
            ProfileLoad.IsLoading = false;
        }
        await LoadProfileAsync();
    }

    private void StartPortraitEntrance(object? sender, object args)
    {
        if (_portraitEntrance is not { } entrance || !Avatar.IsLoaded || Avatar.ActualWidth <= 0) return;
        Avatar.LayoutUpdated -= StartPortraitEntrance;
        entrance.Animation.Completed += PortraitEntrance_Completed;
        try
        {
            if (entrance.Animation.TryStart(Avatar)) return;
        }
        catch (Exception error) { AppLog.Error("Artist portrait transition could not start.", error); }
        ClearPortraitEntrance();
    }

    private void PortraitEntrance_Completed(ConnectedAnimation sender, object args) => ClearPortraitEntrance(cancel: false);

    private void ClearPortraitEntrance(bool cancel = true)
    {
        Avatar.LayoutUpdated -= StartPortraitEntrance;
        var entrance = _portraitEntrance;
        _portraitEntrance = null;
        if (entrance is null) return;
        entrance.Animation.Completed -= PortraitEntrance_Completed;
        if (cancel) entrance.Animation.Cancel();
        if (_pendingAvatar is { } avatar && _lifetime is { IsCancellationRequested: false })
            Avatar.ProfilePicture = avatar;
        _pendingAvatar = null;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => EndPage();

    private void EndPage()
    {
        if (_lifetime is not { } lifetime)
        {
            ClearPortraitEntrance();
            return;
        }
        _lifetime = null;
        ClearPortraitEntrance();
        lifetime.Cancel();
        lifetime.Dispose();
        Localization.PropertyChanged -= LanguageChanged;
        Bindings.StopTracking();
        foreach (var image in _coverRequests.Keys)
            image.Source = null;
        _coverRequests.Clear();
        Model.ModelPath = string.Empty;
        Avatar.ProfilePicture = null;
        Cover.ImageSource = null;
        _viewModel.Clear();
        _loadingProfile = _loadingTracks = _followBusy = _tracksFailed = false;
        _ready.TrySetResult(false);
    }

    private async Task LoadProfileAsync()
    {
        if (_lifetime is not { } lifetime || _loadingProfile) return;
        if (_ready.Task.IsCompleted) _ready = NewCompletion();
        var token = lifetime.Token;
        _loadingProfile = true;
        _profileError = null;
        ProfileFailure.Visibility = Visibility.Collapsed;
        ProfileLoad.IsLoading = _portraitEntrance is null;
        ProfileLoad.Visibility = Visibility.Visible;
        try
        {
            var error = await _viewModel.LoadProfileAsync(PersonPublicId, token);
            if (error is not null)
            {
                _profileError = error == 40000 ? "NotFound" : "LoadFailed";
                return;
            }
            if (_viewModel.Profile is not { } profile) return;

            ArtistName.Text = $"{profile.FirstName} {profile.LastName}".Trim();
            Avatar.DisplayName = ArtistName.Text;
            Hero.Visibility = Visibility.Visible;
            TrackHeading.Visibility = Visibility.Visible;
            ProfileLoad.IsLoading = false;
            _modelStatus = profile.ModelMediaPublicId is null ? "NoModel" : "LoadingModel";
            RefreshText();
            _ready.TrySetResult(true);

            // Identity is usable while optional images, model and discography arrive.
            _ = LoadHeroImageAsync(profile.ProfileMediaPublicId, avatar: true, token);
            if (profile.CoverMediaPublicId != profile.ProfileMediaPublicId)
                _ = LoadHeroImageAsync(profile.CoverMediaPublicId, avatar: false, token);
            _ = LoadModelAsync(profile.ModelMediaPublicId, token);
            await LoadTracksAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            AppLog.Error("Artist profile could not load.", error);
            _profileError = "LoadFailed";
        }
        finally
        {
            if (ReferenceEquals(_lifetime, lifetime))
            {
                _loadingProfile = false;
                ProfileLoad.IsLoading = false;
                if (_profileError is not null)
                {
                    ClearPortraitEntrance();
                    ProfileLoad.Visibility = Visibility.Collapsed;
                    ProfileFailure.Visibility = Visibility.Visible;
                    RefreshText();
                    _ready.TrySetResult(false);
                }
            }
        }
    }

    CancellationToken IMcpPersonProfilePage.Lifetime => _lifetime?.Token ?? new CancellationToken(canceled: true);

    McpPersonProfileState IMcpPersonProfilePage.ReadState()
    {
        var profile = _viewModel.Profile;
        return new()
        {
            PersonPublicId = PersonPublicId, DisplayName = profile is null ? null : ArtistName.Text,
            IsLoading = _loadingProfile || _loadingTracks, IsUpdatingFollow = _followBusy,
            CanFollow = profile?.CanFollow == true, IsFollowing = profile?.IsFollowing == true,
            FollowersCount = profile?.FollowersCount ?? 0, FollowingCount = profile?.FollowingCount,
            TracksCount = profile?.MusicsCount ?? 0, HasCover = profile?.CoverMediaPublicId is not null,
            HasModel = profile?.ModelMediaPublicId is not null, HasMoreTracks = _viewModel.NextCursor is not null,
            ErrorCode = _profileError switch { "NotFound" => "not_found", not null => "load_failed", _ => _tracksFailed ? "tracks_failed" : null },
            Tracks = _viewModel.Tracks.TakeLast(48).Select(item => new McpPersonTrack(item.PublicId, item.Title)).ToArray()
        };
    }

    Task<bool> IMcpPersonProfilePage.SetFollowingAsync(bool following, CancellationToken token) => SetFollowingAsync(following, token);

    Task<bool> IMcpPersonProfilePage.LoadNextTracksAsync(CancellationToken token) =>
        _viewModel.NextCursor is not null || _tracksFailed ? LoadTracksAsync(token) : Task.FromResult(false);

    bool IMcpPersonProfilePage.TryOpenTrack(Guid id)
    {
        if (!_viewModel.Tracks.Any(item => item.PublicId == id)) return false;
        OpenTrack(id);
        return true;
    }

    private async Task<bool> LoadTracksAsync(CancellationToken token)
    {
        if (_lifetime is not { } lifetime || _loadingTracks || _viewModel.Profile is null) return false;
        _loadingTracks = true;
        var hadError = _tracksFailed;
        _tracksFailed = false;
        TracksLoading.Visibility = Visibility.Visible;
        RefreshText();
        try
        {
            _tracksFailed = !await _viewModel.LoadTracksAsync(PersonPublicId, token);
            return !_tracksFailed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (ReferenceEquals(_lifetime, lifetime)) _tracksFailed = hadError;
            return false;
        }
        catch (Exception error)
        {
            AppLog.Error("Artist tracks could not load.", error);
            _tracksFailed = true;
            return false;
        }
        finally
        {
            if (ReferenceEquals(_lifetime, lifetime))
            {
                _loadingTracks = false;
                TracksLoading.Visibility = Visibility.Collapsed;
                RefreshText();
            }
        }
    }

    private async Task<bool> SetFollowingAsync(bool following, CancellationToken cancellationToken)
    {
        if (_lifetime is not { } lifetime || _followBusy || _viewModel.Profile is not { CanFollow: true }) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var token = linked.Token;
        _followBusy = true;
        _generalStatus = null;
        RefreshText();
        try
        {
            var changed = await _viewModel.SetFollowingAsync(following, token);
            _generalStatus = changed ? (following ? "FollowedStatus" : "UnfollowedStatus") : "FollowFailed";
            return changed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            AppLog.Error("Artist follow state could not update.", error);
            _generalStatus = "FollowFailed";
            return false;
        }
        finally
        {
            if (ReferenceEquals(_lifetime, lifetime))
            {
                _followBusy = false;
                RefreshText();
            }
        }
    }

    private async Task LoadHeroImageAsync(Guid? mediaId, bool avatar, CancellationToken token)
    {
        if (mediaId is not Guid id) return;
        try
        {
            var path = await GetMediaPathAsync(id, 20 * 1024 * 1024, token);
            var image = await ImageSourceFactory.LoadFileAsync(path, avatar ? 256 : 1280, token);
            token.ThrowIfCancellationRequested();
            if (avatar)
            {
                if (_portraitEntrance is not null) _pendingAvatar = image;
                else Avatar.ProfilePicture = image;
                if (Cover.ImageSource is null) Cover.ImageSource = image;
            }
            else Cover.ImageSource = image;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Artist artwork could not load.", error); }
    }

    private async Task LoadModelAsync(Guid? mediaId, CancellationToken token)
    {
        if (mediaId is not Guid id) return;
        try
        {
            var path = await GetMediaPathAsync(id, 32 * 1024 * 1024, token);
            token.ThrowIfCancellationRequested();
            Model.ModelPath = path;
            Model.Visibility = Visibility.Visible;
            ModelPlaceholder.Visibility = Visibility.Collapsed;
            ResetModel.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            AppLog.Error("Artist model could not download.", error);
            if (!token.IsCancellationRequested)
            {
                _modelStatus = "ModelFailed";
                RefreshText();
            }
        }
    }

    private async Task<string> GetMediaPathAsync(Guid id, long maximumBytes, CancellationToken token)
    {
        var response = await _api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = id }), token);
        token.ThrowIfCancellationRequested();
        if (response.Content is not { } media || !response.IsSuccess || media.FileSize <= 0 || media.FileSize > maximumBytes
            || !Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri)
            || (!uri.IsFile && uri.Scheme is not ("https" or "http")))
            throw new IOException("Artist media metadata is missing or outside its size limit.");
        return await _downloader.DownloadToCacheAsync(media, uri, cancellationToken: token);
    }

    private void TrackCover_Loaded(object sender, RoutedEventArgs args) => RequestCover((Image)sender);
    private void TrackCover_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (((Image)sender).IsLoaded) RequestCover((Image)sender);
    }
    private void TrackCover_Unloaded(object sender, RoutedEventArgs args) => CancelCover((Image)sender);

    private void CancelCover(Image image)
    {
        if (_coverRequests.Remove(image, out var request)) request.Cancel();
        image.Source = null;
    }

    private void RequestCover(Image image)
    {
        CancelCover(image);
        if (_lifetime is not { } lifetime || image.DataContext is not PersonProfileTrackViewModel { CoverMediaPublicId: Guid id }) return;
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _coverRequests[image] = request;
        _ = RenderCoverAsync(image, id, request);
    }

    private async Task RenderCoverAsync(Image image, Guid id, CancellationTokenSource request)
    {
        var token = request.Token;
        try
        {
            await CoverSlots.WaitAsync(token);
            try
            {
                var path = await GetMediaPathAsync(id, 8 * 1024 * 1024, token);
                var source = await ImageSourceFactory.LoadFileAsync(path, 104, token);
                token.ThrowIfCancellationRequested();
                if (_coverRequests.TryGetValue(image, out var current) && ReferenceEquals(current, request)) image.Source = source;
            }
            finally { CoverSlots.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Track artwork could not load.", error); }
        finally
        {
            if (_coverRequests.TryGetValue(image, out var current) && ReferenceEquals(current, request))
                _coverRequests.Remove(image);
            request.Dispose();
        }
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision)) return;
        RefreshText();
        foreach (var track in _viewModel.Tracks) track.RefreshLanguage();
    }

    private string Text(string key) => Localization.GetString("PersonProfile/" + key);
    private void RefreshText()
    {
        ProfileError.Text = _profileError is null ? string.Empty : Text(_profileError);
        ModelStatus.Text = Text(_modelStatus);
        GeneralStatus.Text = _generalStatus is null ? string.Empty : Text(_generalStatus);
        GeneralStatus.Visibility = Visible(_generalStatus is not null);
        if (_viewModel.Profile is not { } profile) return;

        var culture = CultureInfo.GetCultureInfo(Localization.LanguageTag);
        Followers.Text = profile.FollowersCount.ToString("N0", culture);
        Following.Text = profile.FollowingCount?.ToString("N0", culture) ?? "—";
        TracksCount.Text = profile.MusicsCount.ToString("N0", culture);
        AutomationProperties.SetName(Followers, Localization.Format("PersonProfile/FollowersCount", Followers.Text));
        AutomationProperties.SetName(Following, profile.FollowingCount is null ? Text("FollowingUnavailable")
            : Localization.Format("PersonProfile/FollowingCount", Following.Text));
        AutomationProperties.SetName(TracksCount, Localization.Format("PersonProfile/TracksCount", TracksCount.Text));
        FollowButton.Visibility = Visible(profile.CanFollow);
        FollowLabel.Text = Text(profile.IsFollowing ? "Unfollow" : "Follow");
        FollowIcon.Symbol = profile.IsFollowing ? FluentIcons.Common.Symbol.Checkmark : FluentIcons.Common.Symbol.PersonAdd;
        FollowProgress.Visibility = Visible(_followBusy);
        FollowProgress.IsActive = _followBusy;
        AutomationProperties.SetName(FollowButton, Localization.Format(
            profile.IsFollowing ? "PersonProfile/UnfollowName" : "PersonProfile/FollowName", ArtistName.Text));
        TracksStatus.Text = _tracksFailed ? Text("TracksFailed") : Text("NoTracks");
        TracksStatus.Visibility = Visible(!_loadingTracks && (_tracksFailed || _viewModel.Tracks.Count == 0));
        var showMore = _tracksFailed || _viewModel.NextCursor is not null
            || (_loadingTracks && MoreButton.Visibility == Visibility.Visible);
        MoreButton.Visibility = Visible(showMore);
        MoreButton.Content = Text(_tracksFailed ? "Retry" : "More");
    }

    private void Hero_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var band = width < 600 ? 0 : width < 1040 ? 1 : width < 1450 ? 2 : 3;
        if (_layoutBand == band) return;
        _layoutBand = band;
        var stacked = band == 0;
        ModelColumn.Width = stacked ? new GridLength(0) : new GridLength(band == 1 ? 1.15 : 1, GridUnitType.Star);
        Grid.SetColumn(ModelPanel, stacked ? 0 : 1);
        Grid.SetRow(ModelPanel, stacked ? 1 : 0);
        ModelPanel.Height = band switch { 0 => 220, 1 => 288, 2 => 350, _ => 420 };
        Hero.MinHeight = band switch { 0 => 0, 1 => 336, 2 => 374, _ => 450 };
        Identity.Padding = new Thickness(band switch { 0 => 20, 1 => 24, 2 => 38, _ => 56 }, 24, 24, 24);
        ArtistName.FontSize = band switch { 0 => 30, 1 => 36, 2 => 51, _ => 65 };
        var avatarSize = band switch { 0 => 78, 1 => 94, 2 => 112, _ => 124 };
        Avatar.Width = Avatar.Height = avatarSize;
        AvatarFrame.Width = AvatarFrame.Height = avatarSize + 14;
        AvatarFrame.CornerRadius = new CornerRadius((avatarSize + 14) / 2d);
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private async void RetryProfile_Click(object sender, RoutedEventArgs args) => await LoadProfileAsync();
    private async void More_Click(object sender, RoutedEventArgs args)
    {
        if (_lifetime is { } lifetime && !_loadingTracks) await LoadTracksAsync(lifetime.Token);
    }
    private async void Follow_Click(object sender, RoutedEventArgs args)
    {
        if (_viewModel.Profile is { } profile) await SetFollowingAsync(!profile.IsFollowing, CancellationToken.None);
    }
    private void Reset_Click(object sender, RoutedEventArgs args) => Model.ResetView();
    private void Track_Click(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is PersonProfileTrackViewModel track) OpenTrack(track.PublicId);
    }
    private void OpenTrack(Guid id) => _navigation.InternalNavigate(typeof(MusicsPage), new() { [PageConstants.MUSIC_ID] = id });

    private void Profile_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.GamepadB || Frame?.CanGoBack != true) return;
        e.Handled = true;
        _navigation.InternalGoBack();
    }
}
