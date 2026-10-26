using Microsoft.Windows.Storage;


namespace Mizon.Windows.Services;

public class LocalStorageService
{
    private const string JwtTokenKey = "JwtToken";

    private ApplicationDataContainer LocalSettings => ApplicationData.GetDefault().LocalSettings;


    public void SetJwtToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            LocalSettings.Values.Remove(JwtTokenKey);
        else
            LocalSettings.Values[JwtTokenKey] = token;
    }

    public string? GetJwtToken()
    {
        if (LocalSettings.Values.TryGetValue(JwtTokenKey, out object? value))
            return value as string;
        return null;
    }

}
