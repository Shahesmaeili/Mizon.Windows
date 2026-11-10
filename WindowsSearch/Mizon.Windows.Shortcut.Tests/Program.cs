using System.Runtime.InteropServices;
using Mizon.Windows.Services;

WinRT.ComWrappersSupport.InitializeComWrappers();
var root = Path.Combine(Path.GetTempPath(), "MizonShortcutTests", Guid.NewGuid().ToString("N"));
var desktop = Path.Combine(root, "Desktop");
var icons = Path.Combine(root, "Icons");
var cover = Path.Combine(AppContext.BaseDirectory, "Fixtures", "photo.jpg");
var otherCover = Path.Combine(AppContext.BaseDirectory, "Fixtures", "image3.jpeg");
var id = Guid.NewGuid();
var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
string Arguments(string path) { using var native = new ShellLinkNative(); native.Load(path); return native.GetArguments(); }
using var service = new ShortcutService();
Task<string> Create(Guid musicId, string? name, string? image = null) => service.CreateMusicShortcutAsync(musicId, name, null, image, desktop, icons);
if (args is ["--sample", var musicIdText] && Guid.TryParse(musicIdText, out var sampleId))
{
    Console.WriteLine(await Create(sampleId, "Mizon shortcut activation test", cover));
    return;
}
var first = await Create(id, "آهنگ تست", cover);
Check(Arguments(first) == $"\"mizon://music/{id:D}\"", "shell reads the exact music URI from the saved link");
Check(AppActivationRequest.FromUri(new Uri(Arguments(first).Trim('"'))).MusicPublicId == id, "the app router receives the same music ID");
Check(AppActivationRequest.FromUri(new Uri(Arguments(first).Trim('"'))).MusicAction == MusicActivationAction.Details, "shortcut opens details without autoplay");
Check(await Create(id, "آهنگ تست", cover) == first, "repeated creation updates the same shortcut");
var original = File.ReadAllBytes(first);
var second = await Create(Guid.NewGuid(), "آهنگ تست", cover);
Check(second != first && original.SequenceEqual(File.ReadAllBytes(first)), "same-title music does not overwrite the first song");
var unrelated = Path.Combine(desktop, "existing.lnk");
await File.WriteAllTextAsync(unrelated, "unrelated desktop item");
Check(await Create(id, "existing", cover) != unrelated && File.ReadAllText(unrelated) == "unrelated desktop item", "unreadable unrelated link is preserved");
foreach (var name in new[] { "CON", "NUL.txt", "LPT1", "COM¹", "..", "Title...", "bad<>:\"/\\|?*name", new string('a', 500) })
    Check(File.Exists(await Create(Guid.NewGuid(), name, cover)), "valid file for reserved/long/invalid title: " + name[..Math.Min(20, name.Length)]);
Check(File.Exists(await Create(id, null)), "missing cover and title use app fallback");
var before = Directory.GetFiles(icons).Length;
await Create(id, "آهنگ تست", otherCover);
Check(Directory.GetFiles(icons).Length > before, "changed cover gets a new shell icon cache identity");
var corrupt = Path.Combine(root, "corrupt.jpg");
await File.WriteAllTextAsync(corrupt, "not an image");
Check(File.Exists(await Create(Guid.NewGuid(), "Corrupt", corrupt)), "corrupt cover falls back without losing shortcut");
var parallel = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Create(id, "parallel", cover)));
Check(parallel.Distinct().Count() == 1, "repeated concurrent clicks produce one complete link");
foreach (var icon in Directory.GetFiles(icons, "*.ico"))
{
    using var reader = new BinaryReader(File.OpenRead(icon));
    Check(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() > 0, "valid ICO directory");
    var handle = NativeIcon.LoadImage(0, icon, 1, 48, 48, 0x10);
    Check(handle != 0, "Windows decodes ICO into HICON");
    if (handle != 0) NativeIcon.DestroyIcon(handle);
}
Check(!Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "no unfinished temporary icon files");
Check(!Directory.GetFiles(desktop, ".mizon-*").Any(), "no unfinished temporary shortcuts");
Console.WriteLine($"PASS {checks} assertions. Output: {root}");

internal static partial class NativeIcon
{
    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool DestroyIcon(nint icon);
}
namespace Mizon.Windows.Services
{
    internal static class AppLog
    {
        public static void Information(string message) { }
        public static void Error(string message, Exception error) => Console.WriteLine("Expected fallback diagnostic: " + error.GetType().Name);
    }
}
