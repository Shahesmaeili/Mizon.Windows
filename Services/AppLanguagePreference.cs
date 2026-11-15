using System;
using System.IO;

namespace Mizon.Windows.Services;

internal static class AppLanguagePreference
{
    internal const string SettingKey = "UiLanguage";
    internal const string Persian = "fa-IR";
    internal const string English = "en-US";

    private static string SharedPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mizon", "ui-language");

    internal static string? Normalize(string? language)
    {
        var normalized = language?.Trim().Replace('_', '-');
        if (string.Equals(normalized, "fa", StringComparison.OrdinalIgnoreCase)
            || normalized?.StartsWith("fa-", StringComparison.OrdinalIgnoreCase) == true)
            return Persian;
        if (string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase)
            || normalized?.StartsWith("en-", StringComparison.OrdinalIgnoreCase) == true)
            return English;
        return null;
    }

    // The separately packaged Search provider does not share the main app's LocalSettings.
    internal static string ReadShared()
    {
        try
        {
            using var stream = new FileStream(SharedPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length != 5) return Persian;
            using var reader = new StreamReader(stream);
            return Normalize(reader.ReadToEnd()) ?? Persian;
        }
        catch (IOException) { return Persian; }
        catch (UnauthorizedAccessException) { return Persian; }
    }

    internal static void Publish(string language)
    {
        var path = SharedPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, language);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
