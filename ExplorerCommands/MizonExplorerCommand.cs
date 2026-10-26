using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Windows.Storage;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;

namespace Mizon.Windows.ExplorerCommands;

[ComVisible(true)]
[Guid("E72D6A3B-29A8-4B1F-B0FB-D78D3DF8B8B7")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class MizonExplorerCommand : IExplorerCommand
{
    public int GetTitle(IntPtr _, out IntPtr title)
        => Copy($"Mizon · {ExplorerCommandCurrentUser.GetFullName()}", out title);
    public int GetIcon(IntPtr _, out IntPtr icon) => Copy("shell32.dll,-16769", out icon);
    public int GetToolTip(IntPtr _, out IntPtr tooltip) { tooltip = IntPtr.Zero; return unchecked((int)0x80004001); }
    public int GetCanonicalName(out Guid canonicalName) { canonicalName = Guid.Empty; return 0; }
    public int GetState(IntPtr _, bool __, out ExplorerCommandState state) { state = ExplorerCommandState.Enabled; return 0; }
    public int Invoke(IntPtr _, IntPtr __) => 0;
    public int GetFlags(out ExplorerCommandFlags flags) { flags = ExplorerCommandFlags.HasSubCommands; return 0; }
    public int EnumSubCommands(out IEnumExplorerCommand commands) { commands = new MizonCommandEnumerator(); return 0; }
    private static int Copy(string value, out IntPtr result) { result = Marshal.StringToCoTaskMemUni(value); return 0; }

}

/// <summary>
/// Command shown only when the user right-clicks an empty area of the desktop.
/// It intentionally has no subcommands, so Explorer invokes it immediately.
/// </summary>
[ComVisible(true)]
[Guid("C1B40E19-79ED-4A8A-A099-9F7B4A5DD7E3")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class SiloDesktopExplorerCommand : IExplorerCommand
{
    public int GetTitle(IntPtr _, out IntPtr title) => Copy("پخش سریال Silo", out title);
    public int GetIcon(IntPtr _, out IntPtr icon)
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(SiloDesktopExplorerCommand).Assembly.Location)
            ?? AppContext.BaseDirectory;

        return Copy($"{Path.Combine(assemblyDirectory, "Silo.ico")},0", out icon);
    }
    public int GetToolTip(IntPtr _, out IntPtr tooltip) => Copy("پخش سریال Silo با Mizon", out tooltip);
    public int GetCanonicalName(out Guid canonicalName) { canonicalName = new Guid("C1B40E19-79ED-4A8A-A099-9F7B4A5DD7E3"); return 0; }
    public int GetState(IntPtr _, bool __, out ExplorerCommandState state) { state = ExplorerCommandState.Enabled; return 0; }
    public int Invoke(IntPtr _, IntPtr __)
    {
        Process.Start(new ProcessStartInfo("mizon://play?series=silo") { UseShellExecute = true });
        return 0;
    }
    public int GetFlags(out ExplorerCommandFlags flags) { flags = ExplorerCommandFlags.Default; return 0; }
    public int EnumSubCommands(out IEnumExplorerCommand commands) { commands = null!; return unchecked((int)0x80004001); }

    private static int Copy(string value, out IntPtr result) { result = Marshal.StringToCoTaskMemUni(value); return 0; }
}

[ComVisible(true)]
[Guid("6D4F15CB-1B1B-4F46-BEE1-3F6C2DFB384B")]
[ClassInterface(ClassInterfaceType.None)]
public class MizonSendCommand : IExplorerCommand
{
    public virtual int GetTitle(IntPtr _, out IntPtr title)
        => Copy(ExplorerCommandCurrentUser.GetFullName(), out title);
    public virtual int GetIcon(IntPtr _, out IntPtr icon) => Copy("shell32.dll,-16715", out icon);
    public int GetToolTip(IntPtr _, out IntPtr tooltip) { tooltip = IntPtr.Zero; return unchecked((int)0x80004001); }
    public int GetCanonicalName(out Guid name) { name = Guid.Empty; return 0; }
    public int GetState(IntPtr _, bool __, out ExplorerCommandState state) { state = ExplorerCommandState.Enabled; return 0; }
    public virtual int Invoke(IntPtr _, IntPtr __) { Process.Start(new ProcessStartInfo("mizon://send?mode=file") { UseShellExecute = true }); return 0; }
    public int GetFlags(out ExplorerCommandFlags flags) { flags = ExplorerCommandFlags.Default; return 0; }
    public int EnumSubCommands(out IEnumExplorerCommand commands) { commands = null!; return unchecked((int)0x80004001); }
    private static int Copy(string value, out IntPtr result) { result = Marshal.StringToCoTaskMemUni(value); return 0; }
}

internal static class ExplorerCommandCurrentUser
{
    private const string JwtTokenKey = "JwtToken";
    private const string DefaultTitle = "ارسال با Mizon";
    private static readonly object SyncRoot = new();
    private static readonly HttpClient HttpClient = new();
    private static readonly MemoryCache MemoryCache = new(new MemoryCacheOptions());

    private static string? _fullName;
    private static DateTimeOffset _cacheExpiresAt;

    public static string GetFullName()
    {
        lock (SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(_fullName) && DateTimeOffset.UtcNow < _cacheExpiresAt)
                return _fullName;

            var token = GetJwtToken();
            if (string.IsNullOrWhiteSpace(token))
                return DefaultTitle;

            try
            {
                var api = new MizonApi(HttpClient, MemoryCache);
                api.SetToken(token);

                var response = api.SendRequestAsync(new MeOperation(new()), CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                if (!response.IsSuccess || response.Content is null)
                    return DefaultTitle;

                var fullName = $"{response.Content.FirstName} {response.Content.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(fullName))
                    fullName = response.Content.Username;

                if (string.IsNullOrWhiteSpace(fullName))
                    return DefaultTitle;

                _fullName = fullName;
                _cacheExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30);
                return _fullName;
            }
            catch
            {
                return DefaultTitle;
            }
        }
    }

    private static string? GetJwtToken()
    {
        try
        {
            var localSettings = ApplicationData.GetDefault().LocalSettings;
            return localSettings.Values.TryGetValue(JwtTokenKey, out object? token)
                ? token as string
                : null;
        }
        catch
        {
            return null;
        }
    }
}

internal static class DesktopBackgroundContext
{
    public static bool IsDesktop(IntPtr items)
    {
        if (items == IntPtr.Zero)
            return false;

        try
        {
            var shellItemArray = (IShellItemArray)Marshal.GetObjectForIUnknown(items);
            if (shellItemArray.GetCount(out var count) != 0)
                return false;

            if (count != 1)
                return false;

            if (shellItemArray.GetItemAt(0, out var shellItem) != 0)
                return false;

            if (shellItem.GetDisplayName(ShellDisplayName.FileSystemPath, out var displayName) != 0)
                return false;

            try
            {
                var selectedPath = Marshal.PtrToStringUni(displayName);
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return string.Equals(
                    selectedPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    desktopPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Marshal.FreeCoTaskMem(displayName);
            }
        }
        catch
        {
            return false;
        }
    }
}

internal static class DesktopShellWindow
{
    public static bool IsForeground()
    {
        var windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero)
            return false;

        var className = new StringBuilder(256);
        if (GetClassName(windowHandle, className, className.Capacity) == 0)
            return false;

        return string.Equals(className.ToString(), "Progman", StringComparison.Ordinal)
            || string.Equals(className.ToString(), "WorkerW", StringComparison.Ordinal);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

[ComVisible(true)]
[Guid("78F5A2D1-ACCC-4D0E-80B2-4B514E6C98C0")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class MizonSendLinkCommand : MizonSendCommand
{
    public override int GetTitle(IntPtr _, out IntPtr title) => Copy("ارسال  لینک با Mizon", out title);
    public override int GetIcon(IntPtr _, out IntPtr icon) => Copy("shell32.dll,-16763", out icon);
    public override int Invoke(IntPtr _, IntPtr __) { Process.Start(new ProcessStartInfo("mizon://send?mode=link") { UseShellExecute = true }); return 0; }
    private static int Copy(string value, out IntPtr result) { result = Marshal.StringToCoTaskMemUni(value); return 0; }
}

[ComVisible(true)]
[Guid("B98297A8-BD10-4A2C-834E-94E1F5C79054")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class MizonCommandEnumerator : IEnumExplorerCommand
{
    private readonly IExplorerCommand[] _commands = [new MizonSendCommand(), new MizonSendLinkCommand()]; private uint _position;
    public int Next(uint count, IExplorerCommand[] commands, IntPtr fetched)
    {
        WriteFetched(fetched, 0);

        if (count != 1 || commands.Length == 0 || _position >= _commands.Length)
            return 1;

        commands[0] = _commands[_position++];
        WriteFetched(fetched, 1);
        return 0;
    }
    public int Skip(uint count) { _position = Math.Min((uint)_commands.Length, _position + count); return _position == _commands.Length ? 1 : 0; }
    public int Reset() { _position = 0; return 0; }
    public int Clone(out IEnumExplorerCommand enumerator) { var clone = new MizonCommandEnumerator(); clone._position = _position; enumerator = clone; return 0; }

    private static void WriteFetched(IntPtr fetched, uint value)
    {
        if (fetched != IntPtr.Zero)
            Marshal.WriteInt32(fetched, unchecked((int)value));
    }
}

[ComImport, Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IntPtr items, out IntPtr title);
    [PreserveSig] int GetIcon(IntPtr items, out IntPtr icon);
    [PreserveSig] int GetToolTip(IntPtr items, out IntPtr tip);
    [PreserveSig] int GetCanonicalName(out Guid name);
    [PreserveSig] int GetState(IntPtr items, bool okToBeSlow, out ExplorerCommandState state);
    [PreserveSig] int Invoke(IntPtr items, IntPtr bindContext);
    [PreserveSig] int GetFlags(out ExplorerCommandFlags flags);
    [PreserveSig] int EnumSubCommands(out IEnumExplorerCommand commands);
}

[ComImport, Guid("A88826F8-186F-4987-AADE-EA0CEF8FBFE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumExplorerCommand
{
    [PreserveSig] int Next(
        uint count,
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IExplorerCommand[] commands,
        IntPtr fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumExplorerCommand enumerator);
}
public enum ExplorerCommandState : uint { Enabled = 0, Hidden = 1 }
[Flags] public enum ExplorerCommandFlags : uint { Default = 0, HasSubCommands = 1 }

[ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr result);
    [PreserveSig] int GetPropertyStore(int flags, ref Guid interfaceId, out IntPtr result);
    [PreserveSig] int GetPropertyDescriptionList(ref PropertyKey keyType, ref Guid interfaceId, out IntPtr result);
    [PreserveSig] int GetAttributes(uint attributeFlags, uint shellAttributesMask, out uint shellAttributes);
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemAt(uint index, out IShellItem shellItem);
    [PreserveSig] int EnumItems(out IntPtr enumShellItems);
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr result);
    [PreserveSig] int GetParent(out IShellItem parentItem);
    [PreserveSig] int GetDisplayName(ShellDisplayName displayName, out IntPtr name);
    [PreserveSig] int GetAttributes(uint mask, out uint attributes);
    [PreserveSig] int Compare(IShellItem otherItem, uint hint, out int order);
}

internal enum ShellDisplayName : uint { FileSystemPath = 0x80058000 }

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey { public Guid FormatId; public uint PropertyId; }
