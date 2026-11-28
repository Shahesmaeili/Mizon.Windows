using Microsoft.UI.Xaml.Media;
using Mizon.API.Client.Operations.Search;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.Services.Discovery;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Networking.Connectivity;

namespace Mizon.Windows.ViewModels;

public partial class HomePageViewModel
{
    private bool _nearbyEnabled;
    internal bool NearbyDiscoveryEnabled => _nearbyEnabled;
    internal bool CanStartNearbyDiscovery => _hasAccount && !IsSigningOut
        && _shellCancellation is { IsCancellationRequested: false };

    internal Task SetNearbyDiscoveryEnabledAsync(bool enabled)
    {
        _nearbyEnabled = enabled;
        try { _localStorageService.SetNearbyDiscoveryEnabled(enabled); }
        catch (Exception error)
        {
            AppLog.Information($"Nearby discovery preference could not be saved ({error.GetType().Name}).");
        }
        if (!enabled) return NearbyDevices.StopAsync();
        TryStartNearbyDiscovery();
        return Task.CompletedTask;
    }

    private bool ReadNearbyPreference()
    {
        try { return _localStorageService.GetNearbyDiscoveryEnabled(); }
        catch (Exception error)
        {
            AppLog.Information($"Nearby discovery preference could not be read ({error.GetType().Name}).");
            return true;
        }
    }

    private void NearbyNetworkChanged(object sender)
    {
        if (NearbyDevices.ReadState().Status == NearbyDiscoveryStatus.Unavailable)
            _dispatcherQueue.TryEnqueue(TryStartNearbyDiscovery);
    }

    private void TryStartNearbyDiscovery()
    {
        if (!_nearbyEnabled || !CanStartNearbyDiscovery) return;
        var version = Package.Current.Id.Version;
        NearbyDevices.Start($"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}", Environment.MachineName, Username, NickName);
    }

    internal async Task<ImageSource?> ReadNearbyAvatarAsync(string account, CancellationToken cancellationToken)
    {
        if (string.Equals(Username, account, StringComparison.OrdinalIgnoreCase) && ProfileImage is { } ownImage)
            return ownImage;
        await foreach (var result in MizonApi.SendStreamRequestAsync(new GlobalSearchOperation(new() { Query = account }), cancellationToken))
        {
            if (result.ItemType != "Client" || !string.Equals(result.Username, account, StringComparison.OrdinalIgnoreCase))
                continue;
            if (result.ProfileMediaPublicId is not Guid mediaId) return null;
            var avatar = await ProfileImageLoader.LoadAsync(MizonApi, _mediaDownloader, mediaId, cancellationToken, decodePixelWidth: 96);
            return avatar.Image;
        }
        return null;
    }
}
