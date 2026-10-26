using System;
using System.Diagnostics;
using System.IO;

namespace Mizon.Windows.Services;

/// <summary>
/// Writes diagnostic information that can be collected from a packaged WinUI process.
/// </summary>
internal static class AppLog
{
    private const long MaximumLogSizeInBytes = 2 * 1024 * 1024;
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mizon",
        "Logs");

    public static string FilePath => Path.Combine(LogDirectory, "Mizon.Windows.log");

    public static void Information(string message) => Write("INF", message, null);

    public static void Error(string message, Exception exception) => Write("ERR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var entry = $"{DateTimeOffset.Now:O} [{level}] [T{Environment.CurrentManagedThreadId}] {message}";
        if (exception is not null)
            entry += $"{Environment.NewLine}{exception}";

        Debug.WriteLine(entry);
        Trace.WriteLine(entry);
        Console.WriteLine(entry);

        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);

                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= MaximumLogSizeInBytes)
                {
                    var archivePath = Path.Combine(LogDirectory, "Mizon.Windows.previous.log");
                    File.Move(FilePath, archivePath, true);
                }

                File.AppendAllText(FilePath, entry + Environment.NewLine);
            }
        }
        catch (Exception writeException)
        {
            var failureEntry = $"{DateTimeOffset.Now:O} [ERR] [T{Environment.CurrentManagedThreadId}] Unable to write diagnostics file '{FilePath}'.{Environment.NewLine}{writeException}";
            Debug.WriteLine(failureEntry);
            Trace.WriteLine(failureEntry);
            Console.WriteLine(failureEntry);
        }
    }
}
