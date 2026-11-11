using Mizon.Windows.Services;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;

namespace Mizon.Windows.Taskbar;

internal sealed class TaskbarDeepLinkTransport(Func<nint> ownerWindow) : ITaskbarDeepLinkTransport
{
    public async Task<TaskbarDeepLinkResponse> LaunchAsync(TaskbarDeepLinkRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var window = ownerWindow();
        if (window == 0 || !Native.IsWindow(window)
            || Native.GetWindowThreadProcessId(window, out var processId) == 0 || processId != Environment.ProcessId)
            throw new InvalidOperationException("The companion window is unavailable.");

        var options = new LauncherOptions { TargetApplicationPackageFamilyName = request.PackageFamilyName };
        // Desktop LauncherOptions implements IInitializeWithWindow. Never pass
        // the foreign Explorer/taskbar parent as the calling application's owner.
        WinRT.Interop.InitializeWithWindow.Initialize(options, window);
        var input = new ValueSet
        {
            [AppDeepLinkResultProtocol.VersionKey] = AppDeepLinkResultProtocol.Version,
            [AppDeepLinkResultProtocol.RequestIdKey] = request.RequestId.ToString("D")
        };
        var operation = Launcher.LaunchUriForResultsAsync(request.Uri, options, input);
        try
        {
            var result = await operation.AsTask(token);
            var status = result.Status switch
            {
                LaunchUriStatus.Success => TaskbarLaunchStatus.Success,
                LaunchUriStatus.AppUnavailable => TaskbarLaunchStatus.AppUnavailable,
                LaunchUriStatus.ProtocolUnavailable => TaskbarLaunchStatus.ProtocolUnavailable,
                _ => TaskbarLaunchStatus.Unknown
            };
            var json = status == TaskbarLaunchStatus.Success && result.Result is { } values
                && values.TryGetValue(AppDeepLinkResultProtocol.ResponseKey, out var raw) ? raw as string : null;
            return new(status, json);
        }
        finally
        {
            // AsTask forwards cancellation. Close completed native work; Windows
            // forbids Close while a non-cooperative operation is still Started.
            try { if (operation.Status != AsyncStatus.Started) operation.Close(); }
            catch (COMException error) { TaskbarPaths.Log($"Profile result cleanup failed (0x{error.HResult:X8})."); }
        }
    }
}
