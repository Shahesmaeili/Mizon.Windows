using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Mizon.Windows.PreviewXaml;

[GeneratedComInterface, Guid("D1F276C4-98D8-4636-BF49-EB79507548E9")]
internal partial interface IUiAction
{
    [PreserveSig] int Invoke();
}

[GeneratedComClass]
internal sealed unsafe partial class UiDispatcher : IUiAction, IAgileObject
{
    private Action<nint>? _action;
    private nint _element;
    private UiDispatcher(nint element, Action<nint> action)
    {
        _element = Native.Query(element, Native.Dependency);
        _action = action;
    }

    internal static void Run(nint element, Action<nint> action)
    {
        var dependency = Native.Query(element, Native.Dependency);
        if (dependency == 0) return;
        nint dispatcher = 0;
        try
        {
            dispatcher = Native.GetObject(dependency, 11);
            byte access;
            Native.Check(((delegate* unmanaged[Stdcall]<nint, byte*, int>)Native.Slot(dispatcher, 6))(dispatcher, &access));
            if (access != 0) { action(element); return; }
            var callback = new UiDispatcher(element, action);
            var pointer = ComInterfaceMarshaller<IUiAction>.ConvertToUnmanaged(callback);
            try
            {
                nint work = 0;
                var hr = ((delegate* unmanaged[Stdcall]<nint, int, void*, nint*, int>)Native.Slot(dispatcher, 8))
                    (dispatcher, -1, pointer, &work);
                Native.Release(work);
                if (hr < 0) { callback.Clear(); Native.Check(hr); }
            }
            finally { ComInterfaceMarshaller<IUiAction>.Free(pointer); }
        }
        finally { Native.Release(dispatcher); Native.Release(dependency); }
    }

    public int Invoke()
    {
        try { _action?.Invoke(_element); }
        catch (Exception error) { PreviewTap.Log("XAML dispatcher action", error); }
        finally { Clear(); }
        return 0;
    }

    private void Clear()
    {
        _action = null;
        Native.Release(_element);
        _element = 0;
    }
}
