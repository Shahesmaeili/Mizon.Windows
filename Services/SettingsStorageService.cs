using Mizon.API.Client.Operations.Media;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using FileAttributes = System.IO.FileAttributes;

namespace Mizon.Windows.Services;

/// <summary>Reads storage usage and opens the application's known storage folders.</summary>
public sealed class SettingsStorageService(MediaDownloader downloader)
{
    private readonly MediaDownloader _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    private const FileAttributes SkippedAttributes = FileAttributes.ReparsePoint | FileAttributes.System;

    public string CacheDirectory => _downloader.CacheDirectory;
    public string LogDirectory => Path.GetDirectoryName(AppLog.FilePath)!;
    public string PackageDataDirectory => Microsoft.Windows.Storage.ApplicationData.GetDefault().LocalFolder.Path;

    public async Task<SettingsStorageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        await Task.Run(() => ReadSnapshot(cancellationToken), cancellationToken).ConfigureAwait(false);

    private SettingsStorageSnapshot ReadSnapshot(CancellationToken cancellationToken)
    {
        long bytes = 0, fileCount = 0, pendingFileCount = 0;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var directory = new DirectoryInfo(CacheDirectory);
            if (!directory.Exists || (directory.Attributes & SkippedAttributes) != 0)
                return new(0, 0, 0);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = SkippedAttributes,
                ReturnSpecialDirectories = false
            };
            foreach (var file in directory.EnumerateFiles("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Downloads and animation expansion may finish while this snapshot is read.
                    file.Refresh();
                    if (!file.Exists || (file.Attributes & SkippedAttributes) != 0) continue;
                    if (file.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                        || file.Name.EndsWith(".download-lock", StringComparison.OrdinalIgnoreCase)
                        || file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingFileCount++;
                        continue;
                    }
                    bytes += file.Length;
                    fileCount++;
                }
                catch (IOException) { /* A file may have been moved or deleted. */ }
                catch (UnauthorizedAccessException) { /* Skip files that cannot be inspected. */ }
            }
        }
        catch (IOException) { /* A directory may disappear during enumeration; retain the partial snapshot. */ }
        catch (UnauthorizedAccessException) { /* Inaccessible storage is not a page-loading failure. */ }

        cancellationToken.ThrowIfCancellationRequested();
        return new(bytes, fileCount, pendingFileCount);
    }

    public async Task<bool> OpenFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (!string.Equals(path, CacheDirectory, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, LogDirectory, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, PackageDataDirectory, StringComparison.OrdinalIgnoreCase))
                return false;

            Directory.CreateDirectory(path);
            var folder = await StorageFolder.GetFolderFromPathAsync(path);
            return await Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception error)
        {
            AppLog.Error("Could not open an application storage folder.", error);
            return false;
        }
    }
}

/// <summary>Completed media bytes and files; unfinished files are counted separately.</summary>
public sealed record SettingsStorageSnapshot(long Bytes, long FileCount, long PendingFileCount);
