using Microsoft.UI.Dispatching;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Operations.Search;
using Mizon.API.Client.Requests.Media;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class HomePageViewModel : MainViewModel
{
    private readonly LocalStorageService _localStorageService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly MediaDownloader _mediaDownloader;
    private readonly MulticastDiscoveryServiceV2 _discoveryService;
    private CancellationTokenSource? _shellCancellation;
    private Task _socketStopTask = Task.CompletedTask;
    private Task _discoveryStopTask = Task.CompletedTask;

    public HomePageViewModel(MizonApi mizonApi,
    Lazy<MizonSocket> mizonSocket,
    NavigationService navigationService,
    LocalStorageService localStorageService,
    DispatcherQueue dispatcherQueue,
    IAudioPlayerService audioPlayerService,
    MediaDownloader mediaDownloader,
    MulticastDiscoveryServiceV2 discoveryService)
    : base(mizonApi, mizonSocket, navigationService, audioPlayerService)
    {
        _dispatcherQueue = dispatcherQueue;
        _mediaDownloader = mediaDownloader;
        _discoveryService = discoveryService;

        NavigationService.MainNavigationChanged += NavigationService_NavigationChanged;
        _localStorageService = localStorageService;
        LogoutCommand = new RelayCommand(LogoutAsync, (_, _) => !IsSigningOut);

    }
    public RelayCommand LogoutCommand { get; }

    public string? Username
    {
        get => field;
        set => SetField(ref field, value);
    } = string.Empty;

    public string FirstName
    {
        get => field;
        set => SetField(ref field, value);
    } = string.Empty;

    public string LastName
    {
        get => field;
        set => SetField(ref field, value);
    } = string.Empty;

    public string? NickName
    {
        get => field;
        set => SetField(ref field, value);
    } = string.Empty;

    public string EmailAddress
    {
        get => field;
        set => SetField(ref field, value);
    } = string.Empty;

    public string? ProfileImagePath
    {
        get => field;
        set
        {
            if (SetField(ref field, value) && _hasAccount)
                TaskbarProfileBridge.PublishProfile(_taskbarProfileSession, DisplayName, value);
        }
    }


    public bool IsIndeterminateTopProgressBar
    {
        get => field;
        set => SetField(ref field, value);
    }

    public ObservableCollection<object> MainSearch { get; set; } = new();

    public async IAsyncEnumerable<object> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            yield break;

        await foreach (var result in MizonApi.SendStreamRequestAsync(
            new GlobalSearchOperation(new() { Query = query.Trim() }),
            cancellationToken))
        {
            if (result.ItemType == "Client")
            {
                var client = new MainSearchClientItemViewModel
                {
                    PublicId = result.PublicId,
                    FirstName = result.FirstName ?? string.Empty,
                    LastName = result.LastName ?? string.Empty,
                    NickName = result.NickName,
                    Username = result.Username,
                    ProfileMediaPublicId = result.ProfileMediaPublicId
                };

                if (client.ProfileMediaPublicId is Guid profileMediaPublicId)
                    _ = LoadSearchImageAsync(profileMediaPublicId, path => client.ProfileImagePath = path, cancellationToken);

                yield return client;
                continue;
            }

            if (result.ItemType == "Music")
            {
                var music = new MainSearchMusicItemViewModel
                {
                    PublicId = result.PublicId,
                    Title = result.Title ?? string.Empty,
                    PersianTitle = result.PersianTitle,
                    CoverMediaPublicId = result.CoverMediaPublicId
                };

                if (music.CoverMediaPublicId is Guid coverMediaPublicId)
                    _ = LoadSearchImageAsync(coverMediaPublicId, path => music.CoverImagePath = path, cancellationToken);

                yield return music;
            }
        }
    }

    private async Task LoadSearchImageAsync(Guid mediaPublicId, Action<string> setImagePath, CancellationToken cancellationToken)
    {
        try
        {
            var mediaResponse = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
            {
                MediaPublicId = mediaPublicId
            }), cancellationToken);

            if (!mediaResponse.IsSuccess || mediaResponse.Content is null)
                return;

            var imagePath = await _mediaDownloader.DownloadToCacheAsync(
                mediaResponse.Content,
                new Uri(mediaResponse.Content.StoragePath),
                cancellationToken: cancellationToken);

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!cancellationToken.IsCancellationRequested) setImagePath(imagePath);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error($"Search image could not be downloaded. MediaPublicId='{mediaPublicId}'.", exception);
        }
    }

    private void NavigationService_NavigationChanged(object? sender, NavigationChangedEventArgs e)
    {
        if (!e.IsEnteringPage && e.FromPageType == typeof(HomePage))
        {
            _profileSession++;
            ClearProfile();
            _shellCancellation?.Cancel();
            _shellCancellation?.Dispose();
            _shellCancellation = null;
            MizonSocket.HubConnectionStateChanged -= MizonSocket_HubConnectionStateChanged;
            _socketStopTask = StopSocketAsync();
            _discoveryStopTask = StopDiscoveryAsync();
        }
        if (e.IsEnteringPage)
        {
            if (e.ToPageType == typeof(HomePage))
            {
                _shellCancellation?.Cancel();
                _shellCancellation?.Dispose();
                _shellCancellation = new CancellationTokenSource();
                MizonSocket.HubConnectionStateChanged -= MizonSocket_HubConnectionStateChanged;
                MizonSocket.HubConnectionStateChanged += MizonSocket_HubConnectionStateChanged;
                if (!_hasAccount || (_profileMediaPublicId is not null && ProfileImage is null))
                    _ = RefreshProfileAsync(_shellCancellation.Token);

                _ = StartSocketAsync(_shellCancellation.Token);
                _ = StartDiscoveryAsync(_shellCancellation.Token);
            }
        }
    }

    private async Task StartSocketAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _socketStopTask.WaitAsync(cancellationToken);
            await MizonSocket.StartAsync(cancellationToken);
            await MizonSocket.RegisterDeviceAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("Authenticated socket initialization failed.", exception); }
    }

    private async Task StopSocketAsync()
    {
        try { await MizonSocket.StopAsync(CancellationToken.None); }
        catch (Exception exception) { AppLog.Error("Signed-out socket shutdown failed.", exception); }
    }

    private async Task StartDiscoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            // A quick sign-out/sign-in must wait for the previous socket and workers.
            await _discoveryStopTask.WaitAsync(cancellationToken);
            await _discoveryService.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // LAN discovery is optional; its failure must not interrupt signing in.
            AppLog.Error("Authenticated device discovery initialization failed.", exception);
        }
    }

    private async Task StopDiscoveryAsync()
    {
        try { await _discoveryService.StopAsync(); }
        catch (Exception exception) { AppLog.Error("Signed-out device discovery shutdown failed.", exception); }
    }

    private async Task LogoutAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (IsSigningOut) return;
        ProfileStatus = "در حال خروج از حساب…";
        IsSigningOut = true;
        App.CancelPendingNavigation();
        _profileSession++;
        _shellCancellation?.Cancel();
        try
        {
            // A server outage must not prevent signing out on this device.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await MizonApi.SendRequestAsync(new LogoutOperation(new()), timeout.Token);
        }
        catch (Exception exception) { AppLog.Error("Remote sign-out failed; clearing the local session.", exception); }
        finally
        {
            App.ClearPendingActivation();
            _localStorageService.SetJwtToken(null);
            MizonApi.ClearToken();
            ClearProfile();
            IsSigningOut = false;
            NavigationService.MainNavigate(typeof(LoginPage));
        }
    }


    private void MizonSocket_HubConnectionStateChanged(object? sender, Microsoft.AspNetCore.SignalR.Client.HubConnectionState e)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (e is Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
                IsIndeterminateTopProgressBar = false;
            else IsIndeterminateTopProgressBar = true;
        });
    }
}
