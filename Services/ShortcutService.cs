using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Mizon.Windows.Services;



public interface IShortcutService
{
    Task<string> CreateMusicDesktopShortcutAsync(Guid musicPublicId, string? musicTitleEnglish, string? musicTitlePersian, string? musicIconPath);
}

public sealed class ShortcutService : IShortcutService, IDisposable
{
    private readonly System.Threading.SemaphoreSlim _createGate = new(1, 1);

    public async Task<string> CreateMusicDesktopShortcutAsync(Guid musicPublicId, string? musicTitleEnglish, string? musicTitlePersian, string? musicIconPath)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(musicPublicId, Guid.Empty);
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var iconDirectory = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ShortcutIcons");
        return await CreateMusicShortcutAsync(musicPublicId, musicTitleEnglish, musicTitlePersian,
            musicIconPath, desktopPath, iconDirectory);
    }

    // Explicit directories allow integration tests without touching the user's desktop.
    internal async Task<string> CreateMusicShortcutAsync(Guid musicPublicId, string? englishTitle, string? persianTitle,
        string? coverPath, string desktopPath, string iconDirectory)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(musicPublicId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopPath);
        await _createGate.WaitAsync();
        try
        {
            return await Task.Run(async () =>
            {
                Directory.CreateDirectory(desktopPath);
                var title = GetShortcutTitle(englishTitle, persianTitle, musicPublicId);
                var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                var arguments = $"\"mizon://music/{musicPublicId:D}\"";
                var icon = await GetShortcutIconPathAsync(coverPath, musicPublicId, iconDirectory);
                // A registered URI survives MSIX upgrades and moving the build output.
                // AppInstance redirects it to the existing Mizon window.
                return SaveShortcut(desktopPath, title, musicPublicId, target, arguments, icon);
            });
        }
        finally { _createGate.Release(); }
    }

    private static string SaveShortcut(string desktopPath, string title, Guid musicId, string target, string arguments, string icon)
    {
        var shortcutPath = Path.Combine(desktopPath, title + ".lnk");
        var suffix = 0;
        while (File.Exists(shortcutPath) && !IsMusicShortcut(shortcutPath, musicId, arguments))
        {
            var disambiguator = suffix++ == 0 ? musicId.ToString("N") : $"{musicId:N}-{suffix}";
            shortcutPath = Path.Combine(desktopPath, $"{title} [{disambiguator}].lnk");
        }
        var temporaryPath = Path.Combine(desktopPath, $".mizon-{Guid.NewGuid():N}.lnk");
        try
        {
            using (var link = new ShellLinkNative())
            {
                link.SetTarget(target);
                link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
                link.SetArguments(arguments);
                link.SetDescription($"باز کردن آهنگ {title} در Mizon");
                link.SetIcon(icon);
                link.Save(temporaryPath);
            }
            // Publish only a complete file, preserving the old link if saving fails.
            File.Move(temporaryPath, shortcutPath, overwrite: true);
            AppLog.Information($"Music desktop shortcut saved. MusicPublicId='{musicId}', ShortcutPath='{shortcutPath}', IconPath='{icon}'.");
            return shortcutPath;
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private static bool IsMusicShortcut(string path, Guid musicId, string arguments)
    {
        try
        {
            using var link = new ShellLinkNative();
            link.Load(path);
            var existing = link.GetArguments();
            return string.Equals(existing, arguments, StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing, $"--music \"{musicId:D}\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
    private static string GetShortcutTitle(string? musicTitleEnglish, string? musicTitlePersian, Guid musicPublicId)
    {
        var englishTitle = musicTitleEnglish?.Trim();
        var persianTitle = musicTitlePersian?.Trim().Trim('(', ')').Trim();
        var title = string.IsNullOrWhiteSpace(englishTitle)
            ? persianTitle
            : string.IsNullOrWhiteSpace(persianTitle)
                ? englishTitle
                : $"{englishTitle} ({persianTitle})";

        if (string.IsNullOrWhiteSpace(title))
            title = "Music";

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeTitle = string.Concat(title.Select(character => invalidCharacters.Contains(character) ? '_' : character)).Trim();
        safeTitle = safeTitle[..Math.Min(safeTitle.Length, 96)].TrimEnd(' ', '.');
        if (safeTitle.Length > 0 && char.IsHighSurrogate(safeTitle[^1])) safeTitle = safeTitle[..^1];
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "Music";
        var stem = safeTitle.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                && "123456789¹²³".Contains(stem[3]))) safeTitle = "Music - " + safeTitle;
        return safeTitle;
    }

    private static async Task<string> GetShortcutIconPathAsync(string? requestedIconPath, Guid musicPublicId, string iconDirectory)
    {
        try
        {
            Directory.CreateDirectory(iconDirectory);

            var sourceImagePath = !string.IsNullOrWhiteSpace(requestedIconPath) && File.Exists(requestedIconPath)
                ? requestedIconPath
                : Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon", "Mizon.ico");

            AppLog.Information($"Music shortcut icon requested. CoverPath='{requestedIconPath ?? "null"}', SourcePath='{sourceImagePath ?? "null"}', IconDirectory='{iconDirectory}'.");

            if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
                return Environment.ProcessPath ?? string.Empty;

            // Content-addressing also refreshes Explorer's icon cache when the cover
            // changes, even if the new download has an older file timestamp.
            byte[] hash;
            await using (var source = File.OpenRead(sourceImagePath))
                hash = await SHA256.HashDataAsync(source);
            var iconPath = Path.Combine(iconDirectory, $"music-{musicPublicId:N}-v2-{Convert.ToHexString(hash.AsSpan(0, 12))}.ico");
            if (!File.Exists(iconPath) || new FileInfo(iconPath).Length < 22)
            {
                var temporaryPath = iconPath + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    if (Path.GetExtension(sourceImagePath).Equals(".ico", StringComparison.OrdinalIgnoreCase))
                        File.Copy(sourceImagePath, temporaryPath);
                    else
                        await CreateIconFileAsync(sourceImagePath, temporaryPath);
                    File.Move(temporaryPath, iconPath, overwrite: true);
                }
                finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            }

            AppLog.Information($"Music shortcut ICO created. IconPath='{iconPath}', Size={new FileInfo(iconPath).Length}.");
            return iconPath;
        }
        catch (Exception exception)
        {
            AppLog.Error($"Music cover could not be converted to an ICO shortcut icon. Source='{requestedIconPath}'.", exception);
            return requestedIconPath is not null
                ? await GetShortcutIconPathAsync(null, musicPublicId, iconDirectory)
                : Environment.ProcessPath ?? string.Empty;
        }
    }

    private static async Task CreateIconFileAsync(string sourceImagePath, string iconPath)
    {
        using var sourceFileStream = File.OpenRead(sourceImagePath);
        using var sourceStream = sourceFileStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(sourceStream);

        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
            throw new InvalidDataException("The music cover does not contain image pixels.");

        var scale = Math.Min(1d, 256d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var width = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelWidth * scale));
        var height = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelHeight * scale));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        using var pngStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngStream);
        encoder.SetPixelData(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Straight,
            width,
            height,
            96,
            96,
            pixels.DetachPixelData());
        await encoder.FlushAsync();

        pngStream.Seek(0);
        using var reader = new DataReader(pngStream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)pngStream.Size);
        var pngBytes = new byte[(int)pngStream.Size];
        reader.ReadBytes(pngBytes);

        await using var iconFileStream = new FileStream(iconPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024, useAsync: true);
        await using var writer = new BinaryWriter(iconFileStream, Encoding.UTF8, leaveOpen: false);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(width == 256 ? (byte)0 : (byte)width);
        writer.Write(height == 256 ? (byte)0 : (byte)height);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(pngBytes.Length);
        writer.Write(22);
        writer.Write(pngBytes);
    }

    public void Dispose() => _createGate.Dispose();
}
