using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Mizon.Windows.Services;



public interface IShortcutService
{
    Task<string> CreateMusicDesktopShortcutAsync(Guid musicPublicId, string? musicTitleEnglish, string? musicTitlePersian, string? musicIconPath);
}

public class ShortcutService : IShortcutService
{
    // GUID های ضروری برای COM
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
    }

    public async Task<string> CreateMusicDesktopShortcutAsync(Guid musicPublicId, string? musicTitleEnglish, string? musicTitlePersian, string? musicIconPath)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(musicPublicId, Guid.Empty);

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutTitle = GetShortcutTitle(musicTitleEnglish, musicTitlePersian, musicPublicId);
        var shortcutPath = Path.Combine(desktopPath, $"{shortcutTitle}.lnk");
        var targetPath = GetCurrentExecutablePath();

        if (!File.Exists(targetPath))
            throw new FileNotFoundException("Executable file for the desktop shortcut was not found.", targetPath);

        var shellLink = new ShellLink();
        try
        {
            var link = (IShellLink)shellLink;
            link.SetPath(targetPath);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath)!);
            link.SetArguments($"--music \"{musicPublicId:D}\"");
            link.SetDescription($"باز کردن آهنگ {shortcutTitle} در Mizon");

            var iconPath = await GetShortcutIconPathAsync(musicIconPath, musicPublicId, targetPath);
            link.SetIconLocation(iconPath, 0);

            var persistFile = (IPersistFile)link;
            persistFile.Save(shortcutPath, true);
            return shortcutPath;
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
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
        return string.IsNullOrWhiteSpace(safeTitle) ? "Music" : safeTitle;
    }

    private static async Task<string> GetShortcutIconPathAsync(string? requestedIconPath, Guid musicPublicId, string executablePath)
    {
        try
        {
            var iconDirectory = Path.Combine(
                ApplicationData.Current.LocalCacheFolder.Path,
                "Mizon",
                "ShortcutIcons");
            Directory.CreateDirectory(iconDirectory);

            var sourceImagePath = !string.IsNullOrWhiteSpace(requestedIconPath) && File.Exists(requestedIconPath)
                ? requestedIconPath
                : GetApplicationIconSourcePath(executablePath);

            AppLog.Information($"Music shortcut icon requested. CoverPath='{requestedIconPath ?? "null"}', SourcePath='{sourceImagePath ?? "null"}', IconDirectory='{iconDirectory}'.");

            if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
                return executablePath;

            if (Path.GetExtension(sourceImagePath) is ".ico" or ".exe" or ".dll")
                return sourceImagePath;

            var iconPath = Path.Combine(iconDirectory, $"music-{musicPublicId:N}.ico");
            var sourceLastWriteTime = File.GetLastWriteTimeUtc(sourceImagePath);
            if (!File.Exists(iconPath) || File.GetLastWriteTimeUtc(iconPath) < sourceLastWriteTime)
                await CreateIconFileAsync(sourceImagePath, iconPath);

            AppLog.Information($"Music shortcut ICO created. IconPath='{iconPath}', Size={new FileInfo(iconPath).Length}.");
            return iconPath;
        }
        catch (Exception exception)
        {
            AppLog.Error($"Music cover could not be converted to an ICO shortcut icon. Source='{requestedIconPath}'.", exception);
            return executablePath;
        }
    }

    private static string? GetApplicationIconSourcePath(string executablePath)
    {
        var appIconPath = Path.Combine(Path.GetDirectoryName(executablePath)!, "Assets", "Square150x150Logo.scale-200.png");
        return File.Exists(appIconPath) ? appIconPath : null;
    }

    private static async Task CreateIconFileAsync(string sourceImagePath, string iconPath)
    {
        using var sourceFileStream = File.OpenRead(sourceImagePath);
        using var sourceStream = sourceFileStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(sourceStream);

        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
            throw new InvalidDataException("The music cover does not contain image pixels.");

        var scale = Math.Min(1d, 256d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var width = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
        var height = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));
        var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height };
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        using var pngStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngStream);
        encoder.SetPixelData(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Premultiplied,
            width,
            height,
            decoder.DpiX,
            decoder.DpiY,
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

    private string GetCurrentExecutablePath()
    {
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
    }
}
