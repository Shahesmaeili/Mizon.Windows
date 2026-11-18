namespace Mizon.Windows.Services;

// Shared by the shell adapter, desktop launcher and app protocol receiver.
internal static class TaskbarPreviewDeepLink
{
    internal const string Playlists = "mizon://playlists";
    internal const string Storage = "mizon://settings/storage";
    internal const string PresentAfterResponseKey = "mizon.preview.presentAfterResponse";

    internal static bool IsSupported(string? uri) => uri is Playlists or Storage;
}
