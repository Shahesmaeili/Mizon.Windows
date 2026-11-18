using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Mizon.Windows.PreviewXaml;
using Mizon.Windows.Services;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;

internal static partial class PreviewNavigation
{
    // Use an owner in this process. Explorer's HWND is not a valid launcher owner.
    internal static int Run(string destination)
    {
        if (!TaskbarPreviewDeepLink.IsSupported(destination)) return 2;
        var apartment = Native.RoInitialize(1);
        nint owner = 0;
        try
        {
            Native.Check(apartment);
            if (PreviewConfiguration.Read() is not { } config) return 2;
            using var app = Process.GetProcessById(checked((int)config.ProcessId));
            if (app.ProcessName != "Mizon.Windows") return 2;
            var family = GetPackageFamily(app.SafeHandle);
            if (family is null) return 2;

            owner = CreateWindow(0, "STATIC", "", 0x80000000, 0, 0, 0, 0, 0, 0, 0, 0);
            if (owner == 0) return 2;
            var options = new LauncherOptions { TargetApplicationPackageFamilyName = family };
            WinRT.Interop.InitializeWithWindow.Initialize(options, owner);
            var requestId = Guid.NewGuid();
            var input = new ValueSet
            {
                [AppDeepLinkResultProtocol.VersionKey] = AppDeepLinkResultProtocol.Version,
                [AppDeepLinkResultProtocol.RequestIdKey] = requestId.ToString("D"),
                [TaskbarPreviewDeepLink.PresentAfterResponseKey] = true
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
            var operation = Launcher.LaunchUriForResultsAsync(new Uri(destination), options, input);
            try
            {
                var response = operation.AsTask(timeout.Token).GetAwaiter().GetResult();
                if (response.Status != LaunchUriStatus.Success || response.Result is not { } values
                    || !values.TryGetValue(AppDeepLinkResultProtocol.ResponseKey, out var raw)
                    || !AppDeepLinkResultProtocol.TryParse(raw as string, requestId, out var result)) return 2;
                return result is { Success: true } && result.Data?.Uri == destination ? 0 : 3;
            }
            finally
            {
                try { if (operation.Status != AsyncStatus.Started) operation.Close(); }
                catch (COMException) { /* Windows has already ended the operation. */ }
            }
        }
        catch (Exception) { return 2; }
        finally
        {
            if (owner != 0) DestroyWindow(owner);
            if (apartment >= 0) Native.RoUninitialize();
        }
    }

    private static unsafe string? GetPackageFamily(SafeProcessHandle process)
    {
        const uint Capacity = 128;
        var size = Capacity;
        var buffer = stackalloc char[(int)Capacity];
        return GetPackageFamilyName(process, ref size, buffer) == 0 && size is > 1 and <= Capacity
            ? new string(buffer, 0, checked((int)size - 1)) : null;
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetPackageFamilyName(SafeProcessHandle process, ref uint size, char* family);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindow(uint extended, string className, string title, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
}
