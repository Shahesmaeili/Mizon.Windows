using System.Runtime.InteropServices;

namespace Mizon.Windows.PaletteProvider;

internal static unsafe class NativeMethods
{
    internal const int NoInterface = unchecked((int)0x80004002);
    internal const int PointerError = unchecked((int)0x80004003);
    internal const int InvalidArgument = unchecked((int)0x80070057);
    internal static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    internal static readonly Guid InspectableId = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    internal static readonly Guid AgileId = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
    internal static readonly Guid MarshalId = new("00000003-0000-0000-C000-000000000046");
    internal static readonly Guid ActivationFactoryId = new("00000035-0000-0000-C000-000000000046");

    [DllImport("combase.dll")] internal static extern int RoInitialize(uint mode);
    [DllImport("combase.dll")] internal static extern void RoUninitialize();
    [DllImport("combase.dll")] internal static extern int WindowsCreateString(char* value, uint length, out nint result);
    [DllImport("combase.dll")] internal static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] internal static extern char* WindowsGetStringRawBuffer(nint value, out uint length);
    [DllImport("combase.dll")] internal static extern int RoGetActivationFactory(nint name, in Guid iid, out nint result);
    [DllImport("combase.dll")] internal static extern int RoActivateInstance(nint name, out nint result);
    [DllImport("ole32.dll")] internal static extern int CoRegisterClassObject(in Guid clsid, nint factory, uint context, uint flags, out uint cookie);
    [DllImport("ole32.dll")] internal static extern int CoRevokeClassObject(uint cookie);
    [DllImport("ole32.dll")] internal static extern int CoSuspendClassObjects();
    [DllImport("ole32.dll")] internal static extern int CoResumeClassObjects();
    [DllImport("ole32.dll")] internal static extern int CoCreateFreeThreadedMarshaler(nint outer, out nint marshaler);
    [DllImport("ole32.dll")] internal static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    internal static nint String(string value)
    {
        fixed (char* text = value)
        {
            Check(WindowsCreateString(text, (uint)value.Length, out var result));
            return result;
        }
    }

    internal static string ReadString(nint value)
    {
        var text = WindowsGetStringRawBuffer(value, out var length);
        return length == 0 ? string.Empty : new(text, 0, checked((int)length));
    }

    internal static nint Factory(string name, Guid iid)
    {
        var text = String(name);
        try { Check(RoGetActivationFactory(text, iid, out var result)); return result; }
        finally { WindowsDeleteString(text); }
    }

    internal static nint Activate(string name)
    {
        var text = String(name);
        try { Check(RoActivateInstance(text, out var result)); return result; }
        finally { WindowsDeleteString(text); }
    }

    internal static int Query(nint source, in Guid iid, out nint result)
    {
        nint pointer = 0;
        fixed (Guid* id = &iid)
        {
            var hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(source)[0])(source, id, &pointer);
            result = pointer;
            return hr;
        }
    }

    internal static nint As(nint source, Guid iid) { Check(Query(source, iid, out var result)); return result; }
    internal static nint* Table(nint pointer) => *(nint**)pointer;
    internal static uint AddRef(nint pointer) => pointer == 0 ? 0 : ((delegate* unmanaged[Stdcall]<nint, uint>)Table(pointer)[1])(pointer);
    internal static uint Release(nint pointer) => pointer == 0 ? 0 : ((delegate* unmanaged[Stdcall]<nint, uint>)Table(pointer)[2])(pointer);
    internal static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
}
