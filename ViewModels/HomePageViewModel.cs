using Microsoft.UI.Dispatching;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Operations.Search;
using Mizon.API.Client.Requests.Media;
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

public class HomePageViewModel : MainViewModel
{
    private readonly LocalStorageService _localStorageService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly MediaDownloader _mediaDownloader;

    public HomePageViewModel(MizonApi mizonApi,
    MizonSocket mizonSocket,
    NavigationService navigationService,
    LocalStorageService localStorageService,
    DispatcherQueue dispatcherQueue,
    IAudioPlayerService audioPlayerService,
    MediaDownloader mediaDownloader)
    : base(mizonApi, mizonSocket, navigationService, audioPlayerService)
    {
        _dispatcherQueue = dispatcherQueue;
        _mediaDownloader = mediaDownloader;

        NavigationService.MainNavigationChanged += NavigationService_NavigationChanged;
        _localStorageService = localStorageService;
        ProfileCommand = new RelayCommand(ProfileAsync);
        LogoutCommand = new RelayCommand(LogoutAsync);

    }
    public RelayCommand ProfileCommand { get; }
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
        set => SetField(ref field, value);
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

            _dispatcherQueue.TryEnqueue(() => setImagePath(imagePath));
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
        if (e.IsEnteringPage)
        {
            if (e.ToPageType == typeof(HomePage))
            {
                MizonSocket.HubConnectionStateChanged += MizonSocket_HubConnectionStateChanged;
                _ = GetMe(CancellationToken.None);

                _ = MizonSocket.StartAsync(CancellationToken.None);
            }
        }
    }

    private async Task GetMe(CancellationToken cancellationToken)
    {
        var baseApiResponse = await MizonApi.SendRequestAsync(new MeOperation(new()), cancellationToken);
        if (baseApiResponse.IsSuccess)
        {
            var meResponse = baseApiResponse.Content!;

            Username = meResponse.Username;
            FirstName = meResponse.FirstName;
            LastName = meResponse.LastName;
            NickName = meResponse.NickName;
            EmailAddress = meResponse.EmailAddress;

            if (meResponse.MeProfile is not null)
                _ = LoadProfileImageAsync(meResponse.MeProfile.MediaPublicId, cancellationToken);
        }
    }

    private async Task LoadProfileImageAsync(Guid mediaPublicId, CancellationToken cancellationToken)
    {
        var mediaResponse = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
        {
            MediaPublicId = mediaPublicId
        }), cancellationToken);

        if (!mediaResponse.IsSuccess || mediaResponse.Content is null)
            return;

        var profileImagePath = await _mediaDownloader.DownloadToCacheAsync(
            mediaResponse.Content,
            new Uri(mediaResponse.Content.StoragePath),
            cancellationToken: cancellationToken);

        _dispatcherQueue.TryEnqueue(() => ProfileImagePath = profileImagePath);
    }

    private async Task ProfileAsync(object? parameter, CancellationToken cancellationToken)
    {
        await GetMe(cancellationToken);
    }

    private async Task LogoutAsync(object? parameter, CancellationToken cancellationToken)
    {
        var baseApiResponse = await MizonApi.SendRequestAsync(new LogoutOperation(new()), cancellationToken);

        _localStorageService.SetJwtToken(null);
        NavigationService.MainNavigate(typeof(LoginPage));

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
