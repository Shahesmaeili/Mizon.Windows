using Microsoft.Windows.AppLifecycle;
using System;
using System.Linq;
using Windows.ApplicationModel.Activation;

namespace Mizon.Windows.Services;

internal sealed partial record AppActivationRequest
{
    internal static AppActivationRequest FromActivation(AppActivationArguments activation) => activation.Data switch
    {
        IProtocolActivatedEventArgs protocol => FromUri(protocol.Uri),
        ILaunchActivatedEventArgs launch => FromArguments(launch.Arguments),
        _ => new()
    };

    internal static AppActivationRequest FromArguments(string? arguments)
    {
        var tokens = (arguments ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('"')).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Equals("--playlist", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length
                && Guid.TryParse(tokens[i + 1], out var playlistId) && playlistId != Guid.Empty) return new(PlaylistPublicId: playlistId);
            if (tokens[i].Equals("--music", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length
                && Guid.TryParse(tokens[i + 1], out var musicId) && musicId != Guid.Empty) return new(MusicPublicId: musicId);
            if (Uri.TryCreate(tokens[i], UriKind.Absolute, out var uri) && uri.Scheme == "mizon") return FromUri(uri);
        }
        return new(JumpListService.GetRequestedDestination(arguments));
    }

}
