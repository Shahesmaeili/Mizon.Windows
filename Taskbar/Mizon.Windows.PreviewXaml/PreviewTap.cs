using System.Collections.Concurrent;
using System.Runtime.InteropServices.Marshalling;

namespace Mizon.Windows.PreviewXaml;

[GeneratedComClass]
internal sealed unsafe partial class PreviewTap : IObjectWithSite, IVisualTreeCallback2, IAgileObject
{
    private readonly Lock _lifetimeGate = new();
    private readonly ConcurrentDictionary<ulong, Card> _cards = new();
    private sealed record Card(nint Element, ulong DiagnosticHandle);
    private readonly ConcurrentDictionary<ulong, byte> _pending = new();
    private readonly ConcurrentDictionary<ulong, Card> _removed = new();
    private readonly ConcurrentDictionary<ulong, PreviewRow> _rows = new();
    private nint _site, _tree, _callback, _messageWindow;
    private int _scanQueued, _stopped;
    private Task _subscription = Task.CompletedTask;
    private string? _lastError;
    private static readonly Guid DiagnosticsId = new("18C9E2B6-3F43-4116-9F2B-FF935D7770D2");
    private static readonly Guid TreeId = new("0E79C6E0-85A0-4BE8-B41A-655CF1FD19BD");

    public int SetSite(nint site)
    {
        if (site == 0) { Stop(); return 0; }
        if (_site != 0) return 0;
        try
        {
            _stopped = 0;
            _site = Native.Query(site, DiagnosticsId);
            _tree = Native.Query(site, TreeId);
            if (_site == 0 || _tree == 0) throw new InvalidOperationException("XAML diagnostics interfaces are unavailable.");
            _messageWindow = MessageWindow.Create(this);
            _callback = (nint)ComInterfaceMarshaller<IVisualTreeCallback>.ConvertToUnmanaged(this);
            // Advise blocks if called inside SetSite's XAML callback.
            var tree = Native.Query(_tree, TreeId);
            var callback = Native.Query(_callback, new Guid("AA7A8931-80E4-4FEC-8F3B-553F87B4966E"));
            _subscription = Task.Run(() => Subscribe(tree, callback));
            return 0;
        }
        catch (Exception error) { Log("SetSite", error); Stop(); return error.HResult; }
    }

    private static void Subscribe(nint tree, nint callback)
    {
        var initialized = Native.RoInitialize(1);
        try
        {
            var hr = ((delegate* unmanaged[Stdcall]<nint, nint, int>)Native.Slot(tree, 3))(tree, callback);
            // Some shell islands fail initial replay but still send later mutations.
            Log("Subscribe result 0x" + hr.ToString("X8"));
        }
        catch (Exception error) { Log("Subscribe", error); }
        finally
        {
            Native.Release(tree); Native.Release(callback);
            if (initialized >= 0) Native.RoUninitialize();
        }
    }

    public int GetSite(in Guid iid, out nint result)
    {
        lock (_lifetimeGate) result = Native.Query(_site, iid);
        return result == 0 ? unchecked((int)0x80004002) : 0;
    }

    public int OnVisualTreeChange(ParentChildRelation relation, VisualElement element, int mutation)
    {
        if (Volatile.Read(ref _stopped) != 0) return 0;
        if (mutation == 1)
        {
            _pending.TryRemove(element.Handle, out _);
            foreach (var (key, card) in _cards)
                if (card.DiagnosticHandle == element.Handle) _removed.TryAdd(key, card);
        }
        else if (_pending.Count < 256) _pending.TryAdd(element.Handle, 0);
        if (Interlocked.Exchange(ref _scanQueued, 1) == 0) MessageWindow.Queue(_messageWindow);
        return 0;
    }

    public int OnElementStateChanged(ulong element, int state, nint context) => 0;

    internal void Scan()
    {
        Interlocked.Exchange(ref _scanQueued, 0);
        if (_stopped != 0) return;
        try
        {
            if (PreviewConfiguration.Read() is null) { ReleaseRows(); _pending.Clear(); _removed.Clear(); return; }
            foreach (var key in _removed.Keys)
                if (_removed.TryRemove(key, out var removed)) DispatchCard(key, removed, _ => RemoveCard(key));
            InspectPending();
            foreach (var pair in _cards.ToArray())
                DispatchCard(pair.Key, pair.Value, card => RefreshCard(pair.Key, card));
            _lastError = null;
        }
        catch (Exception error)
        {
            if (_lastError != error.Message) Log("Preview scan", error);
            _lastError = error.Message;
        }
    }

    private void InspectPending()
    {
        if (_pending.IsEmpty || GetSite(in DiagnosticsId, out var site) < 0) return;
        try
        {
            foreach (var handle in _pending.Keys)
            {
                if (!_pending.TryRemove(handle, out _)) continue;
                nint element = 0;
                var hr = ((delegate* unmanaged[Stdcall]<nint, ulong, nint*, int>)Native.Slot(site, 6))(site, handle, &element);
                if (hr < 0 || element == 0) continue;
                try { UiDispatcher.Run(element, InspectElement); }
                finally { Native.Release(element); }
            }
        }
        finally { Native.Release(site); }
    }

    private void InspectElement(nint element)
    {
        if (_stopped != 0 || PreviewConfiguration.Read() is not { } configuration) return;
        var thumbnail = PreviewRow.FindCard(element);
        if (thumbnail == 0) return;
        try
        {
            var identity = Native.Query(thumbnail, new Guid("00000000-0000-0000-C000-000000000046"));
            var key = (ulong)identity;
            Native.Release(identity);
            if (key == 0 || _cards.ContainsKey(key) || _cards.Count >= 8
                || !PreviewRow.Matches(thumbnail, configuration.Title)) return;

            ulong handle = 0;
            if (GetSite(in DiagnosticsId, out var site) < 0) return;
            try
            {
                // IXamlDiagnostics::GetHandleFromIInspectable, xamlOM.h.
                var hr = ((delegate* unmanaged[Stdcall]<nint, nint, ulong*, int>)Native.Slot(site, 7))(site, thumbnail, &handle);
                if (hr < 0) handle = 0;
            }
            finally { Native.Release(site); }
            var retained = Native.Query(thumbnail, Native.Dependency);
            if (retained == 0) return;
            if (_cards.TryAdd(key, new(retained, handle))) RefreshCard(key, thumbnail);
            else Native.Release(retained);
        }
        finally { Native.Release(thumbnail); }
    }

    private void RefreshCard(ulong key, nint card)
    {
        if (_stopped != 0 || PreviewConfiguration.Read() is not { } configuration)
        {
            RemoveCard(key);
            return;
        }
        if (_rows.TryGetValue(key, out var current))
        {
            if (!current.RetainFor(card, configuration, Environment.TickCount64))
            {
                RemoveCard(key);
                return;
            }
            if (current.Persian == configuration.Persian)
            {
                current.RefreshLoading();
                return;
            }
            if (_rows.TryRemove(key, out var old)) old.Dispose();
        }
        else if (!PreviewRow.Matches(card, configuration.Title))
        {
            RemoveCard(key);
            return;
        }
        var row = PreviewRow.Attach(card, configuration, QueueRefresh);
        if (row is null) return;
        if (!_rows.TryAdd(key, row)) row.Dispose();
        else Log("XAML row attached to Mizon thumbnail.");
    }

    private void QueueRefresh() => MessageWindow.Queue(_messageWindow);

    private void RemoveCard(ulong key)
    {
        if (_rows.TryRemove(key, out var row)) row.Dispose();
        Card? card;
        lock (_lifetimeGate) _cards.TryRemove(key, out card);
        if (card is not null) Native.Release(card.Element);
    }

    private bool IsTracked(ulong key, Card card) =>
        _cards.TryGetValue(key, out var current) && ReferenceEquals(current, card);

    private void DispatchCard(ulong key, Card card, Action<nint> action)
    {
        nint element;
        lock (_lifetimeGate)
        {
            if (!IsTracked(key, card)) return;
            element = Native.Query(card.Element, Native.Dependency);
        }
        try
        {
            UiDispatcher.Run(element, dispatched =>
            {
                if (IsTracked(key, card)) action(dispatched);
            });
        }
        finally { Native.Release(element); }
    }

    private void ReleaseRows()
    {
        foreach (var pair in _cards.ToArray())
            DispatchCard(pair.Key, pair.Value, _ => RemoveCard(pair.Key));
    }

    internal void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        ReleaseRows();
        _pending.Clear();
        _removed.Clear();
        var tree = _tree;
        var callback = _callback;
        nint site;
        lock (_lifetimeGate)
        {
            site = _site;
            _site = 0;
        }
        _tree = _callback = 0;
        _ = _subscription.ContinueWith(_ =>
        {
            try
            {
                if (tree != 0 && callback != 0)
                    ((delegate* unmanaged[Stdcall]<nint, nint, int>)Native.Slot(tree, 4))(tree, callback);
            }
            finally
            {
                Native.Release(tree);
                Native.Release(site);
                if (callback != 0) ComInterfaceMarshaller<IVisualTreeCallback>.Free((void*)callback);
            }
        }, TaskScheduler.Default);
        MessageWindow.Close(_messageWindow);
        _messageWindow = 0;
    }

    internal static void Log(string stage, Exception? error = null)
    {
        try
        {
            var path = PreviewConfiguration.DirectoryPath;
            Directory.CreateDirectory(path);
            var message = error is null ? stage : stage + ": " + error.GetType().Name + " 0x" + error.HResult.ToString("X8") + " " + error.Message;
            var file = Path.Combine(path, "preview.log");
            if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) return;
            File.AppendAllText(file, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch { /* Diagnostic output must never escape an Explorer callback. */ }
    }
}
