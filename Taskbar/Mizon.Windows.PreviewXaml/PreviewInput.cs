using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Mizon.Windows.PreviewXaml;

[GeneratedComInterface, Guid("A856E674-B0B6-4BC3-BBA8-1BA06E40D4B5")]
internal partial interface IClickCallback { [PreserveSig] int Invoke(nint sender, nint args); }
[GeneratedComInterface, Guid("E4385929-C004-4BCF-8970-359486E39F88")]
internal partial interface IPointerCallback { [PreserveSig] int Invoke(nint sender, nint args); }
[GeneratedComInterface, Guid("68D940CC-9FF0-49CE-B141-3F07EC477B97")]
internal partial interface ITapCallback { [PreserveSig] int Invoke(nint sender, nint args); }

[GeneratedComClass]
internal sealed partial class PreviewInput(string? destination, bool pointer = false,
    PreviewActionRequest? request = null, PreviewButtonProgress? progress = null, Action? refresh = null)
    : IClickCallback, IPointerCallback, ITapCallback
{
    private bool _disposed;
    public unsafe int Invoke(nint sender, nint args)
    {
        if (_disposed) return 0;
        try
        {
            if (destination is null)
            {
                var iid = new Guid(pointer ? "DA628F0A-9752-49E2-BDE2-49ECCAB9194D" : "A099E6BE-E624-459A-BB1D-E05C73E2CC66");
                var eventArgs = Native.Query(args, iid);
                try
                {
                    if (eventArgs != 0)
                        Native.Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)Native.Slot(eventArgs, pointer ? 9 : 8))(eventArgs, 1));
                }
                finally { Native.Release(eventArgs); }
            }
            else if (request?.Start(destination, refresh) is not null)
                RefreshLoading();
        }
        catch (Exception error) { PreviewTap.Log("Preview input", error); }
        return 0;
    }

    internal void RefreshLoading()
    {
        if (!_disposed && request is not null) progress?.Apply(request.IsPending);
    }

    internal void VerifyLoading(bool pending) =>
        (progress ?? throw new InvalidOperationException("No button progress attached.")).Verify(pending);

    internal void Stop()
    {
        _disposed = true;
        progress?.Dispose();
    }
}

internal sealed unsafe class PreviewInputBinding : IDisposable
{
    private nint _target;
    private readonly int _remove;
    private readonly long _token;
    private readonly PreviewInput _handler;

    private PreviewInputBinding(nint target, int remove, long token, PreviewInput handler)
    { _target = target; _remove = remove; _token = token; _handler = handler; }

    internal static PreviewInputBinding Click(nint button, string destination, PreviewActionRequest request,
        PreviewButtonProgress progress, Action? refresh)
    {
        var handler = new PreviewInput(destination, request: request, progress: progress, refresh: refresh);
        try
        {
            var callback = ComInterfaceMarshaller<IClickCallback>.ConvertToUnmanaged(handler);
            try
            {
                var target = Native.Query(button, new Guid("FA002C1A-494E-46CF-91D4-E14A8D798674"));
                return Register(target, 14, (nint)callback, handler);
            }
            finally { ComInterfaceMarshaller<IClickCallback>.Free(callback); }
        }
        catch { handler.Stop(); throw; }
    }

    internal void RefreshLoading() => _handler.RefreshLoading();
    internal void VerifyLoading(bool pending) => _handler.VerifyLoading(pending);

    internal static PreviewInputBinding Pointer(nint root, int slot)
    {
        var target = Native.Query(root, new Guid("676D0BE9-B65C-41C6-BA40-58CF87F201C1"));
        var handler = new PreviewInput(null, true);
        var callback = ComInterfaceMarshaller<IPointerCallback>.ConvertToUnmanaged(handler);
        try { return Register(target, slot, (nint)callback, handler); }
        finally { ComInterfaceMarshaller<IPointerCallback>.Free(callback); }
    }

    internal static PreviewInputBinding Tapped(nint root)
    {
        var target = Native.Query(root, new Guid("676D0BE9-B65C-41C6-BA40-58CF87F201C1"));
        var handler = new PreviewInput(null);
        var callback = ComInterfaceMarshaller<ITapCallback>.ConvertToUnmanaged(handler);
        try { return Register(target, 73, (nint)callback, handler); }
        finally { ComInterfaceMarshaller<ITapCallback>.Free(callback); }
    }

    private static PreviewInputBinding Register(nint target, int add, nint callback, PreviewInput handler)
    {
        if (target == 0) throw new InvalidOperationException("XAML input target is unavailable.");
        try
        {
            long token;
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Native.Slot(target, add))(target, callback, &token));
            return new(target, add + 1, token, handler);
        }
        catch { Native.Release(target); throw; }
    }

    public void Dispose()
    {
        var target = _target;
        if (target == 0) return;
        _target = 0;
        try { _handler.Stop(); }
        finally
        {
            try { Native.Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Native.Slot(target, _remove))(target, _token)); }
            finally { Native.Release(target); }
        }
    }
}
