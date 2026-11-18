using System.Collections.Concurrent;
using System.Runtime.InteropServices.Marshalling;

namespace Mizon.Windows.PreviewXaml;

[GeneratedComClass]
internal sealed unsafe partial class PreviewTap : IObjectWithSite, IVisualTreeCallback2, IAgileObject
{
    private readonly ConcurrentDictionary<ulong, nint> _cards = new();
    private readonly ConcurrentDictionary<ulong, byte> _pending = new();
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
        result = Native.Query(_site, iid);
        return result == 0 ? unchecked((int)0x80004002) : 0;
    }

    public int OnVisualTreeChange(ParentChildRelation relation, VisualElement element, int mutation)
    {
        if (Volatile.Read(ref _stopped) != 0) return 0;
        if (mutation == 1) _pending.TryRemove(element.Handle, out _);
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
            if (PreviewConfiguration.Read() is null) { ReleaseRows(); _pending.Clear(); return; }
            foreach (var handle in _pending.Keys)
            {
                _pending.TryRemove(handle, out _);
                nint element = 0;
                var hr = ((delegate* unmanaged[Stdcall]<nint, ulong, nint*, int>)Native.Slot(_site, 6))(_site, handle, &element);
                if (hr < 0 || element == 0) continue;
                try { UiDispatcher.Run(element, InspectElement); }
                finally { Native.Release(element); }
            }
            foreach (var pair in _cards.ToArray())
                UiDispatcher.Run(pair.Value, card => RefreshCard(pair.Key, card));
            _lastError = null;
        }
        catch (Exception error)
        {
            if (_lastError != error.Message) Log("Preview scan", error);
            _lastError = error.Message;
        }
    }

    private void InspectElement(nint element)
    {
        if (_stopped != 0 || PreviewConfiguration.Read() is not { } configuration) return;
        var thumbnail = PreviewRow.FindCard(element);
        if (thumbnail == 0) return;
        try
        {
            if (!PreviewRow.Matches(thumbnail, configuration.Title)) return;
            var identity = Native.Query(thumbnail, new Guid("00000000-0000-0000-C000-000000000046"));
            var key = (ulong)identity;
            Native.Release(identity);
            if (_cards.Count < 8 && !_cards.ContainsKey(key))
            {
                var retained = Native.Query(thumbnail, Native.Dependency);
                if (!_cards.TryAdd(key, retained)) Native.Release(retained);
            }
            if (_cards.ContainsKey(key)) RefreshCard(key, thumbnail);
        }
        finally { Native.Release(thumbnail); }
    }

    private void RefreshCard(ulong key, nint card)
    {
        if (_stopped != 0 || PreviewConfiguration.Read() is not { } configuration
            || !PreviewRow.Matches(card, configuration.Title))
        {
            RemoveCard(key);
            return;
        }
        if (_rows.TryGetValue(key, out var current))
        {
            if (current.Persian == configuration.Persian)
            {
                current.RefreshLoading();
                return;
            }
            if (_rows.TryRemove(key, out var old)) old.Dispose();
        }
        var row = PreviewRow.Attach(card, configuration.Persian, QueueRefresh);
        if (row is null) return;
        if (!_rows.TryAdd(key, row)) row.Dispose();
        else Log("XAML row attached to Mizon thumbnail.");
    }

    private void QueueRefresh() => MessageWindow.Queue(_messageWindow);

    private void RemoveCard(ulong key)
    {
        if (_rows.TryRemove(key, out var row)) row.Dispose();
        if (_cards.TryRemove(key, out var card)) Native.Release(card);
    }

    private void ReleaseRows()
    {
        foreach (var pair in _cards.ToArray())
            UiDispatcher.Run(pair.Value, _ => RemoveCard(pair.Key));
    }

    internal void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        ReleaseRows();
        _pending.Clear();
        var tree = _tree;
        var callback = _callback;
        var site = _site;
        _tree = _callback = _site = 0;
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
