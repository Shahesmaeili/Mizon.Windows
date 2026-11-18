using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mizon.Windows.PreviewXaml;

internal static partial class PreviewHelperProcess
{
    private const uint StartWithoutFeedback = 0x00000080; // STARTF_FORCEOFFFEEDBACK
    private const uint CreateNoWindow = 0x08000000;

    internal static async Task<int> RunAsync(string executable, string arguments)
    {
        using var process = Start(executable, arguments);
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = ThreadPool.RegisterWaitForSingleObject(process, (_, timedOut) => exited.TrySetResult(!timedOut),
            null, TimeSpan.FromSeconds(60), executeOnlyOnce: true);
        try
        {
            if (!await exited.Task.ConfigureAwait(false))
            {
                TerminateProcess(process.SafeWaitHandle, 2);
                return 2;
            }
            return GetExitCodeProcess(process.SafeWaitHandle, out var code) ? unchecked((int)code) : 2;
        }
        finally { wait.Unregister(null); }
    }

    private static unsafe WaitHandle Start(string executable, string arguments)
    {
        // Win32 may edit the command line. The executable path is explicit,
        // and arguments are fixed by the caller and its destination allowlist.
        var commandLine = ("\"" + executable + "\" " + arguments + '\0').ToCharArray();
        var startup = new StartupInfo { Size = (uint)sizeof(StartupInfo), Flags = StartWithoutFeedback };
        var wait = new ProcessWaitHandle();
        try
        {
            fixed (char* command = commandLine)
            {
                if (!CreateProcess(executable, command, 0, 0, 0, CreateNoWindow, 0, null, in startup, out var created))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                using var thread = new SafeWaitHandle(created.Thread, ownsHandle: true);
                wait.SafeWaitHandle = new SafeWaitHandle(created.Process, ownsHandle: true);
            }
            return wait;
        }
        catch { wait.Dispose(); throw; }
    }

    internal static bool IsStartupFeedbackSuppressed()
    {
        GetStartupInfo(out var startup);
        return (startup.Flags & StartWithoutFeedback) != 0;
    }

    private sealed class ProcessWaitHandle : WaitHandle;

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        internal uint Size;
        internal nint Reserved, Desktop, Title;
        internal uint X, Y, Width, Height, Columns, Rows, Fill, Flags;
        internal ushort ShowWindow, ReservedSize;
        internal nint ReservedData, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        internal nint Process, Thread;
        internal uint ProcessId, ThreadId;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcess(string application, char* commandLine, nint processAttributes,
        nint threadAttributes, int inheritHandles, uint creationFlags, nint environment, string? directory,
        in StartupInfo startup, out ProcessInformation process);

    [LibraryImport("kernel32.dll", EntryPoint = "GetStartupInfoW")]
    private static partial void GetStartupInfo(out StartupInfo startup);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeWaitHandle process, out uint code);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeWaitHandle process, uint code);
}
