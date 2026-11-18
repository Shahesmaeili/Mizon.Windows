using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Mizon.Windows.PreviewXaml;

[GeneratedComInterface, Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, in Guid iid, out nint result);
    [PreserveSig] int LockServer(int locked);
}

[GeneratedComInterface, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352")]
internal partial interface IObjectWithSite
{
    [PreserveSig] int SetSite(nint site);
    [PreserveSig] int GetSite(in Guid iid, out nint result);
}

[StructLayout(LayoutKind.Sequential)]
internal struct SourceInfo
{
    internal nint FileName;
    internal uint Line, Column, Character;
    internal nint Hash;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VisualElement
{
    internal ulong Handle;
    internal SourceInfo Source;
    internal nint Type, Name;
    internal uint Children;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ParentChildRelation
{
    internal ulong Parent, Child;
    internal uint Index;
}

[GeneratedComInterface, Guid("AA7A8931-80E4-4FEC-8F3B-553F87B4966E")]
internal partial interface IVisualTreeCallback
{
    [PreserveSig] int OnVisualTreeChange(ParentChildRelation relation, VisualElement element, int mutation);
}

[GeneratedComInterface, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
internal partial interface IAgileObject { }

[GeneratedComInterface, Guid("BAD9EB88-AE77-4397-B948-5FA2DB0A19EA")]
internal partial interface IVisualTreeCallback2 : IVisualTreeCallback
{
    [PreserveSig] int OnElementStateChanged(ulong element, int state, nint context);
}

internal static unsafe partial class Exports
{
    internal static readonly Guid ClassId = new("E3700439-6637-42B5-ACD5-1F514BDE17CE");
    private static readonly Factory Instance = new();

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject", CallConvs = [typeof(CallConvStdcall)])]
    internal static int GetClassObject(Guid* classId, Guid* iid, nint* result)
    {
        if (result == null) return unchecked((int)0x80004003);
        *result = 0;
        if (classId == null || iid == null || *classId != ClassId) return unchecked((int)0x80040111);
        try
        {
            // NativeAOT shared libraries must remain resident until their host exits.
            GetModuleHandleEx(5, (nint)(delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int>)&GetClassObject, out _);
            var factory = ComInterfaceMarshaller<IClassFactory>.ConvertToUnmanaged(Instance);
            try { *result = Native.Query((nint)factory, *iid); }
            finally { ComInterfaceMarshaller<IClassFactory>.Free(factory); }
            return *result == 0 ? unchecked((int)0x80004002) : 0;
        }
        catch (Exception error) { return error.HResult; }
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow", CallConvs = [typeof(CallConvStdcall)])]
    internal static int CanUnload() => 1;

    [UnmanagedCallersOnly(EntryPoint = "MizonVerifyMarkup", CallConvs = [typeof(CallConvStdcall)])]
    internal static int VerifyMarkup(int persian)
    {
        var apartment = Native.RoInitialize(0);
        nint manager = 0, factory = 0, row = 0;
        try
        {
            Native.Check(apartment);
            factory = Native.Factory("Windows.UI.Xaml.Hosting.WindowsXamlManager", new("28258A12-7D82-505B-B210-712B04A58882"));
            manager = Native.GetObject(factory, 6);
            row = PreviewRow.LoadMarkup(PreviewMarkup.Create(persian != 0));
            var name = Native.Query(row, Native.Framework);
            try
            {
                if (Native.GetText(name, 33) != "MizonPreviewActions") return unchecked((int)0x80004005);
                PreviewRow.VerifyAttachment();
                return 0;
            }
            finally { Native.Release(name); }
        }
        catch (Exception error) { PreviewTap.Log("Markup self-test", error); return error.HResult; }
        finally
        {
            Native.Release(row);
            if (manager != 0)
            {
                var close = Native.Query(manager, new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E"));
                try { if (close != 0) ((delegate* unmanaged[Stdcall]<nint, int>)Native.Slot(close, 6))(close); }
                finally { Native.Release(close); }
            }
            Native.Release(manager); Native.Release(factory);
            if (apartment >= 0) Native.RoUninitialize();
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleEx(uint flags, nint address, out nint module);
}

[GeneratedComClass]
internal sealed partial class Factory : IClassFactory
{
    public unsafe int CreateInstance(nint outer, in Guid iid, out nint result)
    {
        result = 0;
        if (outer != 0) return unchecked((int)0x80040110);
        try
        {
            var pointer = ComInterfaceMarshaller<IObjectWithSite>.ConvertToUnmanaged(new PreviewTap());
            try { result = Native.Query((nint)pointer, iid); }
            finally { ComInterfaceMarshaller<IObjectWithSite>.Free(pointer); }
            return result == 0 ? unchecked((int)0x80004002) : 0;
        }
        catch (Exception error) { return error.HResult; }
    }

    public int LockServer(int locked) => 0;
}
