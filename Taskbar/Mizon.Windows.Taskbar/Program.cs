using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Mizon.Windows.Taskbar;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments is ["--test-layout"])
        {
            var checks = TaskbarLayoutChecks.Run();
            TaskbarPaths.Log($"Taskbar placement checks passed: {checks}.");
            return 0;
        }
        var replace = false;
        var diagnostic = false;
        var duration = 0;
        var inspectLayout = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] == "--replace") replace = true;
            else if (arguments[index] == "--inspect-layout") inspectLayout = true;
            else if (arguments[index] == "--diagnostic-state-directory" && index + 1 < arguments.Length)
            {
                TaskbarPaths.UseDiagnosticDirectory(arguments[++index]);
                diagnostic = true;
            }
            else if (arguments[index] == "--diagnostic-duration-seconds" && index + 1 < arguments.Length
                && int.TryParse(arguments[++index], out duration) && duration is >= 1 and <= 300) { }
            else return 2;
        }
        if (duration != 0 && !diagnostic) return 2;
        if (inspectLayout)
        {
            var taskbar = Native.FindWindow("Shell_TrayWnd", null);
            var layout = Task.Run(() => TaskbarLayoutLocator.Read(taskbar)).GetAwaiter().GetResult();
            TaskbarPaths.Log($"Layout root: {layout.Bounds.Left},{layout.Bounds.Top},{layout.Bounds.Right},{layout.Bounds.Bottom}");
            foreach (var item in layout.Items)
                TaskbarPaths.Log($"Layout {(TaskbarLayoutLocator.IsOccupied(item, Environment.ProcessId) ? "occupied" : "container")}: {item.AutomationId}; {item.ClassName}; type={item.ControlType}; pid={item.ProcessId}; rect={item.Bounds.Left},{item.Bounds.Top},{item.Bounds.Right},{item.Bounds.Bottom}");
            return 0;
        }
        using var identity = WindowsIdentity.GetCurrent();
        var instanceName = @"Local\Mizon.Taskbar.Profile." + identity.User?.Value;
        if (diagnostic) instanceName += ".Diagnostic." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TaskbarPaths.DirectoryPath)))[..16];
        using var mutex = new Mutex(false, instanceName);
        if (!TryAcquire(mutex, 0))
        {
            if (!replace) return 0;
            try
            {
                using var existing = EventWaitHandle.OpenExisting(instanceName + ".Exit");
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            if (!TryAcquire(mutex, 8000))
            {
                TaskbarPaths.Log("Replacement deferred: existing companion did not finish its cleanup within eight seconds.");
                return 3;
            }
        }
        try
        {
            using var exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Exit");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            TaskbarApplication? application = null;
            try
            {
                Application.Start(_ =>
                {
                    var queue = DispatcherQueue.GetForCurrentThread();
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
                    application = new TaskbarApplication(exitRequested, duration);
                });
            }
            finally { application?.Dispose(); }
            return 0;
        }
        catch (Exception error)
        {
            TaskbarPaths.Log($"Fatal: {error}");
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static bool TryAcquire(Mutex mutex, int timeout)
    {
        try { return mutex.WaitOne(timeout); }
        catch (AbandonedMutexException) { return true; }
    }

}
