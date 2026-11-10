using Microsoft.UI.Xaml;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Pages;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Runs on the app dispatcher after the independent navigation permission gate.
// The OS protocol and MCP both use HomePage.OpenDestinationAsync for content.
internal sealed class AppDeepLinkNavigation : IAppDeepLinkNavigation, IDisposable
{
    private readonly NavigationService _navigation;
    private readonly LocalStorageService _storage;
    private readonly McpAuthenticationService _authentication;
    private bool _hasSession;
    private bool _disposed;

    internal AppDeepLinkNavigation(NavigationService navigation, LocalStorageService storage, McpAuthenticationService authentication)
    {
        _navigation = navigation;
        _storage = storage;
        _authentication = authentication;
        _hasSession = !string.IsNullOrWhiteSpace(storage.GetJwtToken());
        storage.AuthenticationChanged += AuthenticationChanged;
    }

    private void AuthenticationChanged(object? sender, bool hasSession) => _hasSession = hasSession;

    public AppNavigationSnapshot GetState()
    {
        // Welcome is an internal validation stage, never a public URI destination.
        if (_navigation.CurrentMainPage is WelcomePage)
            return new("welcome", null, App.PendingActivation.ToUri(), false, _hasSession);
        var current = CurrentDestination();
        return new(current?.RouteKey ?? "unavailable", current?.ToUri(), App.PendingActivation.ToUri(),
            _navigation.CurrentMainPage is HomePage && _navigation.CanInternalGoBack, _hasSession);
    }

    private AppActivationRequest? CurrentDestination()
    {
        if (_navigation.CurrentMainPage is LoginPage login)
            return new(Destination: login.ViewModel.IsRegistrationMode ? "register" : "login");
        if (_navigation.CurrentMainPage is not HomePage home) return null;
        if (home.ActiveStoryPublicId is Guid storyId) return new(StoryPublicId: storyId);
        if (home.ActiveSearchQuery is { Length: > 0 } query) return new(SearchQuery: query);
        return _navigation.CurrentInternalPage switch
        {
            MusicPage => new(Destination: "music"),
            MusicsPage music when music.MusicPublicId != Guid.Empty => new(MusicPublicId: music.MusicPublicId),
            MusicPlaylistsPage => new(Destination: "playlists"),
            MusicPlaylistPage playlist when playlist.PlaylistPublicId != Guid.Empty => new(PlaylistPublicId: playlist.PlaylistPublicId),
            ClientProfilePage client when client.ClientPublicId != Guid.Empty => new(ClientPublicId: client.ClientPublicId),
            FollowingActivitiesPage => new(Destination: "activities"),
            SettingsPage settings => new(Destination: "settings", SettingsSection: settings.SelectedSectionSlug),
            VPNPage => new(Destination: "vpn"),
            SectionLandingPage section => new(Destination: section.Destination),
            _ => new(Destination: "home")
        };
    }

    public async Task<AppNavigationOutcome> OpenAsync(AppActivationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return new(false, "unavailable", "The app navigation connection has closed.");
        if (App.MainMizonWindow?.DispatcherQueue.HasThreadAccess != true)
            throw new InvalidOperationException("App navigation requires the UI dispatcher.");
        if (_navigation.CurrentMainPage is LoginPage { ViewModel.ControlsIsEnabled: false })
            return new(false, "busy", "An authentication request is already in progress.");
        if (_navigation.CurrentMainPage is HomePage { ViewModel.IsSigningOut: true })
            return new(false, "busy", "Sign-out is still in progress. No navigation was queued.");
        if (request.RequiresAuthentication && !_hasSession)
            return new(false, "requires_sign_in", "Sign in first. No navigation was queued.");
        App.ClearPendingActivation();
        if (request.Destination is "login" or "register")
        {
            var result = await _authentication.ExecuteAsync(new McpRequest
            {
                Operation = "open_page", Page = request.Destination
            }, cancellationToken);
            return new(result.Success, result.Code, result.Message);
        }

        var home = _navigation.CurrentMainPage as HomePage ?? await ValidateSessionAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (home is null || !_hasSession)
            return new(false, "requires_sign_in", "The session could not be validated. No destination was queued.");
        if (!await McpAuthenticationService.WaitForLoadedAsync(home, cancellationToken)
            || !ReferenceEquals(_navigation.CurrentMainPage, home))
            return new(false, "page_changed", "The active page changed before navigation completed.");
        var opened = await home.OpenDestinationAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return opened
            ? new(true, "opened", "The destination was opened using the app navigation flow; content may still be loading.")
            : new(false, "page_unavailable", "The destination is unavailable or navigation was superseded.");
    }

    private async Task<HomePage?> ValidateSessionAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<HomePage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, NavigationChangedEventArgs args)
        {
            if (!args.IsEnteringPage) return;
            if (_navigation.CurrentMainPage is HomePage home) completion.TrySetResult(home);
            else if (_navigation.CurrentMainPage is LoginPage) completion.TrySetResult(null);
        }
        _navigation.MainNavigationChanged += Changed;
        try
        {
            if (_navigation.CurrentMainPage is not WelcomePage) _navigation.MainNavigate(typeof(WelcomePage));
            if (_navigation.CurrentMainPage is HomePage home) return home;
            // No pending destination is saved here: a revoked/timed-out request
            // cannot execute later when an account check eventually succeeds.
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
        }
        finally { _navigation.MainNavigationChanged -= Changed; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _storage.AuthenticationChanged -= AuthenticationChanged;
    }
}
