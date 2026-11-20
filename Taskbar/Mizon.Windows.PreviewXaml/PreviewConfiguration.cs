using System.Globalization;
using Mizon.Windows.Services;

namespace Mizon.Windows.PreviewXaml;

internal readonly record struct PreviewConfiguration(nint Window, uint ProcessId, bool Persian, string Title)
{
    internal static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mizon", "TaskbarPreview");

    internal TaskbarOnlineSnapshot ReadOnline()
    {
        var path = Path.Combine(DirectoryPath, "online.txt");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 192 &&
                TaskbarOnlineSnapshot.Decode(File.ReadAllText(path), Window, ProcessId) is { } snapshot)
                return snapshot;
        }
        catch (IOException) { }
        return new(TaskbarOnlinePhase.Disconnected, null, null, false);
    }

    internal static unsafe PreviewConfiguration? Read()
    {
        var path = Path.Combine(DirectoryPath, "window.txt");
        if (!File.Exists(path)) return null;
        var values = File.ReadAllText(path).Split('|');
        if (values.Length != 3 || !long.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle)
            || !uint.TryParse(values[1], out var process) || values[2] is not ("fa-IR" or "en-US")
            || !Native.IsWindow((nint)handle)) return null;
        Native.GetWindowThreadProcessId((nint)handle, out var actual);
        if (process != actual) return null;
        var buffer = stackalloc char[260];
        var length = Native.GetWindowText((nint)handle, buffer, 260);
        var title = new string(buffer, 0, Math.Max(0, length));
        if (title != "Mizon" && !title.StartsWith("Mizon - ", StringComparison.Ordinal)) return null;
        return new((nint)handle, process, values[2] == "fa-IR", title);
    }
}
