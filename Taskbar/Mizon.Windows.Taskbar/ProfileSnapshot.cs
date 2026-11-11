using System.Text.Json.Serialization;

namespace Mizon.Windows.Taskbar;

// Atomic IPC snapshot shared with the main application. It contains no credentials.
internal sealed record ProfileSnapshot
{
    public int Version { get; init; } = 1;
    public long Revision { get; init; }
    public bool IsAuthenticated { get; init; }
    public bool IsOnline { get; init; }
    public int MainProcessId { get; init; }
    public long MainProcessStartTimeUtcTicks { get; init; }
    public long PresenceUpdatedAtUtcTicks { get; init; }
    public string LanguageTag { get; init; } = "fa-IR";
    public string? OnlinePresenceLabel { get; init; }
    public string? OfflinePresenceLabel { get; init; }
    public Guid? ClientPublicId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? ImagePath { get; init; }
    public Guid? MoodPublicId { get; init; }
    public string? MoodEmoji { get; init; }
    public string? MoodTitle { get; init; }
    public string? MoodAnimationPath { get; init; }
    public string? MoodEffectPath { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProfileSnapshot))]
internal partial class TaskbarJsonContext : JsonSerializerContext;

internal static class TaskbarPaths
{
    internal static string DirectoryPath { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mizon", "TaskbarProfile");
    internal static string SnapshotPath => Path.Combine(DirectoryPath, "profile.json");
    internal static string LogPath => Path.Combine(DirectoryPath, "Taskbar.log");

    internal static void UseDiagnosticDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("A diagnostic state directory must be a fully qualified local path.");
        DirectoryPath = Path.GetFullPath(path);
    }

    internal static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
