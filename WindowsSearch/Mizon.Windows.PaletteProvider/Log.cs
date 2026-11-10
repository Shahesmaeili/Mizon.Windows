namespace Mizon.Windows.PaletteProvider;

internal static class Log
{
    private static readonly Lock Gate = new();
    // Outside AppData virtualization: the shell server and developer tools must
    // see the same trace even when launched by a different packaged application.
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mizon", "WindowsPaletteProvider", "Logs");
    internal static readonly string FilePath = Path.Combine(DirectoryPath, "WindowsPaletteProvider.log");
    // Opt-in tracing for shell-only rendering issues. No media URLs or tokens
    // are written, and normal searches do not pay for preview diagnostics.
    internal static readonly bool PreviewDiagnosticsEnabled = File.Exists(Path.Combine(DirectoryPath, "PreviewDiagnostics.enabled"));
    internal static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2 * 1024 * 1024)
                    File.Move(FilePath, FilePath + ".previous", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static int Error(string operation, Exception error)
    {
        Write($"{operation}: {error}");
        return error.HResult < 0 ? error.HResult : unchecked((int)0x80004005);
    }
}
