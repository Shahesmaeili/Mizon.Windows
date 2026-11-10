using Microsoft.Windows.Storage;


using Mizon.API.Client;
using Mizon.Windows.SearchIntegration;
using System;

namespace Mizon.Windows.Services;

public class LocalStorageService
{
    private const string JwtTokenKey = "JwtToken";

    private ApplicationDataContainer LocalSettings => ApplicationData.GetDefault().LocalSettings;

    internal event EventHandler<bool>? AuthenticationChanged;


    public void SetJwtToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            LocalSettings.Values.Remove(JwtTokenKey);
        else
            LocalSettings.Values[JwtTokenKey] = token;
        PublishExternalAuthentication(token);
        AuthenticationChanged?.Invoke(this, !string.IsNullOrWhiteSpace(token));
    }

    public string? GetJwtToken()
    {
        var token = LocalSettings.Values.TryGetValue(JwtTokenKey, out object? value) ? value as string : null;
        PublishExternalAuthentication(token);
        return token;
    }

    private static void PublishExternalAuthentication(string? token)
    {
        try { TaskbarProfileBridge.SynchronizeAuthentication(token); }
        catch (Exception error)
        {
            AppLog.Information($"Taskbar authentication synchronization failed ({error.GetType().Name}).");
        }
        try
        {
            var apiAddress = new Uri(Endpoints.Search.Global).GetLeftPart(UriPartial.Authority) + "/";
            PaletteAuthenticationStore.Write(token, apiAddress);
        }
        catch (Exception error)
        {
            AppLog.Information($"Search authentication synchronization failed ({error.GetType().Name}).");
        }
    }

}
