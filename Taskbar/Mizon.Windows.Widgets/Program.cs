using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Mizon.Windows.Widgets;

internal static partial class Program
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mizon", "Widgets");
    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(DirectoryPath);
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\Mizon.Widgets.Stop");
        if (args.Contains("--stop")) { stop.Set(); return 0; }
        using var single = new Mutex(true, @"Local\Mizon.Widgets.Host", out var created);
        if (!created) return 0;
        stop.Reset();
        if (args.Contains("--install"))
        {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            run.SetValue("MizonWidgets", $"\"{Environment.ProcessPath}\"");
        }
        var nativePath = Path.Combine(AppContext.BaseDirectory, "Mizon.Widgets.Native.dll");
        var native = NativeLibrary.Load(nativePath);
        var attach = Marshal.GetDelegateForFunctionPointer<Attach>(NativeLibrary.GetExport(native, "AttachToExplorer"));
        var resolve = Marshal.GetDelegateForFunctionPointer<Resolve>(NativeLibrary.GetExport(native, "ResolveTaskbarSymbol"));
        var taskbarDll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps", "MicrosoftWindows.Client.Core_cw5n1h2txyewy", "Taskbar.View.dll");
        var symbolCache = Path.Combine(DirectoryPath, "Symbols");
        Directory.CreateDirectory(symbolCache);
        var moduleHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(taskbarDll)));
        var offsetCache = Path.Combine(symbolCache, moduleHash + "-exact-function.txt");
        ulong offset = 0;
        if (File.Exists(offsetCache)) ulong.TryParse(File.ReadAllText(offsetCache), out offset);
        if (offset == 0)
        {
            Log("Resolving taskbar symbol from matching Microsoft PDB; network work stays outside Explorer.");
            offset = resolve(taskbarDll, symbolCache);
            if (offset != 0) File.WriteAllText(offsetCache, offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (offset == 0) { Log("Matching taskbar symbol unavailable. No shell mutation performed."); NativeLibrary.Free(native); return 4; }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "taskbar-symbol.txt"), offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var previousPid = 0u;
        var attempts = 0;
        try
        {
            while (!stop.WaitOne(2500))
            {
                var taskbar = FindWindowW("Shell_TrayWnd", null);
                if (taskbar == 0) { previousPid = 0; continue; }
                GetWindowThreadProcessId(taskbar, out var pid);
                if (pid == previousPid) continue;
                var hr = attach(pid, nativePath);
                Log($"Attach Explorer={pid}: 0x{hr:X8}");
                if (hr >= 0) { previousPid = pid; attempts = 0; }
                else if (++attempts >= 6) { previousPid = pid; attempts = 0; Log("Retry limit reached; waiting for Explorer restart."); }
            }
        }
        finally { NativeLibrary.Free(native); }
        Log("Host stopped. Native timer restores original Widgets content.");
        return 0;
    }

    private static void Log(string value) => File.AppendAllText(Path.Combine(DirectoryPath, "host.log"), $"{DateTimeOffset.Now:O} {value}{Environment.NewLine}");
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int Attach(uint pid, [MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate ulong Resolve([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPWStr)] string cache);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string name, string? title);
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint pid);
}
