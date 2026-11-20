using System.Diagnostics;
using System.Runtime.InteropServices;
using Mizon.Windows.PreviewXaml;

internal static unsafe partial class Program
{
    private static readonly Guid ClassId = new("E3700439-6637-42B5-ACD5-1F514BDE17CE");
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments is ["--open-preview-action", var destination])
            return Task.Run(() => PreviewNavigation.Run(destination)).GetAwaiter().GetResult();

        var apartment = Native.RoInitialize(0);
        try
        {
            Native.Check(apartment);
            if (arguments is ["--self-test", var testLibrary]) return VerifyExports(Path.GetFullPath(testLibrary), true);
            if (arguments is ["--self-test", var englishLibrary, "en-US"]) return VerifyExports(Path.GetFullPath(englishLibrary), false);
            if (arguments is ["--watch", var watchedLibrary]) return Watch(Path.GetFullPath(watchedLibrary));
            if (arguments is ["--stop"])
            {
                Directory.CreateDirectory(PreviewConfiguration.DirectoryPath);
                File.WriteAllText(Path.Combine(PreviewConfiguration.DirectoryPath, "stop"), "stop");
                return 0;
            }
            if (arguments is not ["--attach", var library])
                throw new ArgumentException("Use --attach <NativeAOT DLL>, --stop, or --self-test <DLL>.");
            library = Path.GetFullPath(library);
            if (!File.Exists(library)) throw new FileNotFoundException("Preview NativeAOT DLL was not published.", library);
            if (PreviewConfiguration.Read() is not { } configuration)
                throw new InvalidOperationException("No valid Mizon main-window configuration is available.");
            using var process = Process.GetProcessById(checked((int)configuration.ProcessId));
            if (process.ProcessName != "Mizon.Windows") throw new InvalidOperationException("The target window is not Mizon.");
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == 0) throw new InvalidOperationException("The Windows taskbar is unavailable.");
            Native.GetWindowThreadProcessId(taskbar, out var explorerId);
            using var explorer = Process.GetProcessById(checked((int)explorerId));
            if (explorer.ProcessName != "explorer" || explorer.SessionId != Process.GetCurrentProcess().SessionId)
                throw new InvalidOperationException("The taskbar is outside the current user's desktop session.");
            Directory.CreateDirectory(PreviewConfiguration.DirectoryPath);
            File.Delete(Path.Combine(PreviewConfiguration.DirectoryPath, "stop"));
            var xaml = NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Windows.UI.Xaml.dll"));
            try
            {
                var initialize = (delegate* unmanaged[Stdcall]<char*, uint, char*, char*, Guid, char*, int>)
                    NativeLibrary.GetExport(xaml, "InitializeXamlDiagnosticsEx");
                var last = unchecked((int)0x80070490);
                var attached = 0;
                var misses = 0;
                for (var index = 1; index <= 100; index++)
                {
                    var endpoint = "VisualDiagConnection" + index;
                    fixed (char* name = endpoint)
                    fixed (char* path = library)
                    fixed (char* empty = "")
                        last = initialize(name, explorerId, empty, path, ClassId, empty);
                    if (last == unchecked((int)0x80070490))
                    {
                        if (++misses >= 4 && attached != 0) break;
                        continue;
                    }
                    misses = 0;
                    Console.WriteLine($"Explorer={explorerId}, endpoint={index}, attach HRESULT=0x{last:X8}");
                    if (last >= 0) attached++;
                }
                if (attached != 0) return 0;
                Marshal.ThrowExceptionForHR(last);
                return 1;
            }
            finally { NativeLibrary.Free(xaml); }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 1;
        }
        finally { if (apartment >= 0) Native.RoUninitialize(); }
    }

    private static int Watch(string library)
    {
        if (!File.Exists(library)) throw new FileNotFoundException("The preview DLL is missing.");
        using var current = Process.GetCurrentProcess();
        using var single = new Mutex(false, @"Local\Mizon.PreviewXaml.Host." + current.SessionId);
        var directory = PreviewConfiguration.DirectoryPath;
        Directory.CreateDirectory(directory);
        var stop = Path.Combine(directory, "stop");
        var owner = Path.Combine(directory, "watcher-payload.txt");
        if (!TakeWatcher(single, 0))
        {
            if (File.Exists(owner) && File.ReadAllText(owner) == library) return 0;
            // Let the existing watcher and TAP retire cooperatively before
            // attaching a new payload in the same Explorer session.
            File.WriteAllText(stop, "Mizon preview payload changed.");
            if (!TakeWatcher(single, 8000)) return 1;
        }
        try
        {
            if (File.Exists(stop)) Thread.Sleep(2200); // Allow the older TAP's stop check to run.
            File.WriteAllText(owner, library);
            File.Delete(stop);
            return WatchOwned(library, current.SessionId, stop);
        }
        finally
        {
            try { File.Delete(owner); }
            finally { single.ReleaseMutex(); }
        }
    }

    private static bool TakeWatcher(Mutex watcher, int milliseconds)
    {
        try { return watcher.WaitOne(milliseconds); }
        catch (AbandonedMutexException) { return true; }
    }

    private static int WatchOwned(string library, int sessionId, string stop)
    {
        var previousExplorer = 0u;
        var previousProcess = 0;
        var emptyChecks = 0;
        while (!File.Exists(stop))
        {
            var available = Process.GetProcessesByName("Mizon.Windows");
            try
            {
                var mizon = available.FirstOrDefault(process => process.SessionId == sessionId && process.MainWindowHandle != 0);
                if (mizon is null)
                {
                    if (++emptyChecks >= 10)
                    {
                        File.WriteAllText(stop, "Mizon main window closed.");
                        return 0;
                    }
                }
                else
                {
                    emptyChecks = 0;
                    var configurationFile = Path.Combine(PreviewConfiguration.DirectoryPath, "window.txt");
                    if (PreviewConfiguration.Read() is null && File.Exists(configurationFile))
                    {
                        // Development restarts can replace the HWND before the next app configuration is written.
                        Directory.CreateDirectory(PreviewConfiguration.DirectoryPath);
                        var file = configurationFile;
                        var text = File.Exists(file) ? File.ReadAllText(file) : "";
                        var language = text.EndsWith("|en-US", StringComparison.Ordinal) ? "en-US" : "fa-IR";
                        File.WriteAllText(file + ".tmp", $"{mizon.MainWindowHandle}|{mizon.Id}|{language}");
                        File.Move(file + ".tmp", file, true);
                    }
                    var taskbar = FindWindow("Shell_TrayWnd", null);
                    Native.GetWindowThreadProcessId(taskbar, out var explorer);
                    if (PreviewConfiguration.Read() is not null && explorer != 0
                        && (previousExplorer != explorer || previousProcess != mizon.Id))
                    {
                        using var attach = new Process
                        {
                            StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
                            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }
                        };
                        attach.StartInfo.ArgumentList.Add("--attach");
                        attach.StartInfo.ArgumentList.Add(library);
                        attach.Start();
                        if (!attach.WaitForExit(15000)) { attach.Kill(); return 1; }
                        if (attach.ExitCode != 0)
                        {
                            Thread.Sleep(2000);
                            continue;
                        }
                        previousExplorer = explorer;
                        previousProcess = mizon.Id;
                    }
                }
            }
            finally { foreach (var process in available) process.Dispose(); }
            Thread.Sleep(1000);
        }
        return 0;
    }

    private static int VerifyExports(string path, bool persian)
    {
        // Do not FreeLibrary an AOT module. The process exit reclaims it.
        var module = NativeLibrary.Load(path);
        var getClass = (delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int>)NativeLibrary.GetExport(module, "DllGetClassObject");
        var factoryId = new Guid("00000001-0000-0000-C000-000000000046");
        var siteId = new Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352");
        var clsid = ClassId;
        nint factory = 0;
        Native.Check(getClass(&clsid, &factoryId, &factory));
        if (factory == 0) throw new InvalidOperationException("No class factory returned.");
        try
        {
            nint site = 0;
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Native.Slot(factory, 3))(factory, 0, &siteId, &site));
            try
            {
                Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Native.Slot(site, 3))(site, 0));
                var unknown = new Guid("00000000-0000-0000-C000-000000000046");
                nint missing = 0;
                var hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Native.Slot(site, 4))(site, &unknown, &missing);
                if (hr >= 0 || missing != 0) throw new InvalidOperationException("A detached TAP disclosed a site.");
            }
            finally { Native.Release(site); }
            var invalid = Guid.NewGuid();
            nint unsupported = 0;
            if (getClass(&invalid, &factoryId, &unsupported) >= 0 || unsupported != 0)
                throw new InvalidOperationException("An undeclared COM class was accepted.");
            Console.WriteLine("PASS actual NativeAOT exports, factory, detached TAP, and unsupported class.");
            var verifyMarkup = (delegate* unmanaged[Stdcall]<int, int>)NativeLibrary.GetExport(module, "MizonVerifyMarkup");
            Native.Check(verifyMarkup(persian ? 1 : 0));
            Console.WriteLine("PASS " + (persian ? "Persian" : "English") + " native XAML presence, eased counts, offline reset, caption retention, and layout restoration.");
            return 0;
        }
        finally { Native.Release(factory); }
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string className, string? title);
}
