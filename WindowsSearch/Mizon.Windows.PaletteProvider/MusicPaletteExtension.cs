using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mizon.Windows.PaletteProvider;

internal sealed unsafe class MusicPaletteExtension : NativeObject
{
    internal static readonly Guid InterfaceId = new("2F89BC97-3238-5E81-8E02-5DAA9C563F81");
    private static readonly nint Vtable = CreateTable();
    private static int _activeCount;
    internal static int ActiveCount => Volatile.Read(ref _activeCount);
    private readonly Lock _gate = new();
    private readonly Dictionary<long, nint> _handlers = [];
    private long _nextToken;
    private nint _factory;
    private nint _host;
    private PaletteSearchSession? _session;
    private bool _counted;

    internal MusicPaletteExtension()
    {
        try
        {
            AddInterface(InterfaceId, Vtable);
            EnableMarshaling();
            Interlocked.Increment(ref _activeCount);
            _counted = true;
            Program.Activity();
        }
        catch { Dispose(); throw; }
    }

    internal override string RuntimeClassName => "Mizon.Windows.PaletteProvider.MusicPaletteExtension";

    private static nint CreateTable()
    {
        var table = (nint*)NewTable(14);
        table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, int>)&Initialize;
        table[7] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)&GetItem;
        table[8] = (nint)(delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)&GetItems;
        table[9] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)&ExecuteQueryAsync;
        table[10] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint*, nint*, int>)&ReportAnalyticsAsync;
        table[11] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint*, nint*, int>)&ReportAnalyticsAsync;
        table[12] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, long*, int>)&AddItemsChanged;
        table[13] = (nint)(delegate* unmanaged[Stdcall]<nint, long, int>)&RemoveItemsChanged;
        return (nint)table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Initialize(nint self, nint factory, nint host)
    {
        if (factory == 0 || host == 0) return NativeMethods.InvalidArgument;
        try
        {
            var owner = Owner<MusicPaletteExtension>(self);
            NativeMethods.AddRef(factory); NativeMethods.AddRef(host);
            nint oldFactory; nint oldHost; PaletteSearchSession? oldSession;
            lock (owner._gate)
            {
                oldFactory = owner._factory; oldHost = owner._host; oldSession = owner._session;
                owner._factory = factory; owner._host = host; owner._session = new PaletteSearchSession(factory);
            }
            oldSession?.Dispose(); NativeMethods.Release(oldHost); NativeMethods.Release(oldFactory);
            Program.Activity();
            Log.Write("Initialize: native item factory and extension host received.");
            return 0;
        }
        catch (Exception error) { return Log.Error("Initialize", error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetItem(nint self, nint id, nint* item)
    {
        if (item == null) return NativeMethods.PointerError;
        *item = 0;
        try
        {
            var value = NativeMethods.ReadString(id);
            Log.Write($"GetItem: {value}");
            var owner = Owner<MusicPaletteExtension>(self);
            PaletteSearchSession? session;
            lock (owner._gate) session = owner._session;
            *item = session?.GetItem(value) ?? 0;
            Program.Activity();
            return 0;
        }
        catch (Exception error) { return Log.Error("GetItem", error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetItems(nint self, uint* count, nint** items)
    {
        if (count == null || items == null) return NativeMethods.PointerError;
        *count = 0; *items = null;
        Program.Activity();
        Log.Write("GetItems: no static catalogue; results are supplied by ExecuteQueryAsync.");
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ExecuteQueryAsync(nint self, nint query, nint* operation)
    {
        if (operation == null) return NativeMethods.PointerError;
        *operation = 0;
        if (query == 0) return NativeMethods.InvalidArgument;
        try
        {
            Program.Activity();
            var owner = Owner<MusicPaletteExtension>(self);
            var text = PaletteData.ReadQuery(query).Trim();
            PaletteSearchSession? session;
            lock (owner._gate) session = owner._session;
            *operation = (session ?? throw new InvalidOperationException("Provider has not been initialized.")).Start(query,text);
            return 0;
        }
        catch (Exception error) { return Log.Error("ExecuteQueryAsync", error); }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ReportAnalyticsAsync(nint self, uint count, nint* analytics, nint* operation)
    {
        if (operation == null) return NativeMethods.PointerError;
        *operation = 0;
        try
        {
            Program.Activity();
            Log.Write($"Analytics received: count={count}");
            *operation = new CompletedOperation(action: true).Identity;
            return 0;
        }
        catch (Exception error) { return Log.Error("Analytics", error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int AddItemsChanged(nint self, nint handler, long* token)
    {
        if (token == null) return NativeMethods.PointerError;
        *token = 0;
        if (handler == 0) return NativeMethods.InvalidArgument;
        try
        {
            var owner = Owner<MusicPaletteExtension>(self);
            lock (owner._gate)
            {
                var value = ++owner._nextToken;
                owner._handlers.Add(value, handler);
                NativeMethods.AddRef(handler);
                *token = value;
            }
            return 0;
        }
        catch (Exception error) { return Log.Error("AddItemsChanged", error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int RemoveItemsChanged(nint self, long token)
    {
        try
        {
            var owner = Owner<MusicPaletteExtension>(self);
            nint handler;
            lock (owner._gate) owner._handlers.Remove(token, out handler);
            NativeMethods.Release(handler);
            return 0;
        }
        catch (Exception error) { return Log.Error("RemoveItemsChanged", error); }
    }

    protected override void ReleaseResources()
    {
        PaletteSearchSession? session; nint factory; nint host; nint[] handlers;
        lock (_gate)
        {
            session = _session; factory = _factory; host = _host;
            _session = null; _factory = 0; _host = 0;
            handlers = [.. _handlers.Values];
            _handlers.Clear();
        }
        foreach (var handler in handlers) NativeMethods.Release(handler);
        session?.Dispose(); NativeMethods.Release(host); NativeMethods.Release(factory);
        if (_counted) { _counted = false; Interlocked.Decrement(ref _activeCount); }
        Program.Activity();
    }
}
