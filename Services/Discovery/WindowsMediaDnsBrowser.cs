using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mizon.Windows.Services.Discovery;

// Windows' device association provider does not always surface every resolved
// service. The DNS-SD API is an independent source of endpoints for media.
internal sealed partial class WindowsMediaDnsBrowser(INearbyMediaSession media,
    Action<string, string?, IEnumerable<string>?> publish) : IDisposable
{
    private const uint DnsPending = 9506;
    private static readonly ConcurrentDictionary<nint, WindowsMediaDnsBrowser> Browsers = new();
    private static readonly ConcurrentDictionary<nint, ResolveQuery> Resolvers = new();
    private static int _nextContext;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _lastQuery = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResolveQuery> _pending = new(StringComparer.OrdinalIgnoreCase);
    private nint _id;
    private nint _name;
    private nint _cancel;
    private bool _disposed;

    internal unsafe void Start()
    {
        _id = NewContext();
        _name = Marshal.StringToHGlobalUni(NearbyDeviceProtocol.ServiceType + ".local");
        Browsers.TryAdd(_id, this);
        var request = new Request
        {
            Version = 1, Name = _name, Context = _id,
            Callback = (nint)(delegate* unmanaged[Stdcall]<uint, nint, nint, void>)&BrowseResult
        };
        var status = Browse(ref request, ref _cancel);
        if (status is not (0 or DnsPending)) Dispose();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void BrowseResult(uint status, nint context, nint records)
    {
        try
        {
            if (status != 0 || !Browsers.TryGetValue(context, out var browser)) return;
            var count = 0;
            for (var record = (Record*)records; record != null && ++count <= 128; record = (Record*)record->Next)
            {
                if (record->Type != 12 || record->Target == 0) continue;
                var name = Marshal.PtrToStringUni(record->Target);
                if (name is not { Length: > 42 and <= 100 } || !name.StartsWith("Mizon-", StringComparison.OrdinalIgnoreCase))
                    continue;
                var update = new PointerUpdate(browser, name.TrimEnd('.'), record->Ttl == 0);
                ThreadPool.UnsafeQueueUserWorkItem(static item => item.Owner.OnPointer(item.Name, item.Remove), update, preferLocal: false);
            }
        }
        catch (Exception) { }
        finally { if (records != 0) FreeRecords(records, 1); }
    }

    private unsafe void OnPointer(string name, bool remove)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (remove)
            {
                _lastQuery.Remove(name);
                if (_pending.Remove(name, out var old)) Cancel(old);
                Withdraw(name);
                return;
            }

            var now = Environment.TickCount64;
            if (_pending.ContainsKey(name) || (_lastQuery.TryGetValue(name, out var prior) && now - prior < 15_000))
                return;
            if (_pending.Count >= 8) return;
            if (_lastQuery.Count >= 128) _lastQuery.Clear();
            _lastQuery[name] = now;

            var query = new ResolveQuery(this, name, NewContext(), Marshal.StringToHGlobalUni(name));
            _pending[name] = query;
            Resolvers.TryAdd(query.Id, query);
            var request = new Request
            {
                Version = 1, Name = query.NativeName, Context = query.Id,
                Callback = (nint)(delegate* unmanaged[Stdcall]<uint, nint, nint, void>)&ResolveResult
            };
            var status = Resolve(ref request, ref query.CancelHandle);
            if (status is not (0 or DnsPending))
            {
                _pending.Remove(name);
                Cancel(query);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void ResolveResult(uint status, nint context, nint instance)
    {
        if (!Resolvers.TryRemove(context, out var query))
        {
            if (instance != 0) FreeInstance(instance);
            return;
        }

        Resolution? resolved = null;
        try
        {
            if (status == 0 && instance != 0)
            {
                var service = (ServiceInstance*)instance;
                if (service->PropertyCount <= 24 && service->Keys != 0 && service->Values != 0)
                {
                    var txt = new List<string>((int)service->PropertyCount);
                    for (var index = 0; index < service->PropertyCount; index++)
                    {
                        var key = Marshal.PtrToStringUni(((nint*)service->Keys)[index]);
                        var value = Marshal.PtrToStringUni(((nint*)service->Values)[index]);
                        if (key is { Length: > 0 and <= 24 } && value is not null && value.Length <= 255 - key.Length - 1)
                            txt.Add($"{key}={value}");
                    }
                    var addresses = new List<string>(2);
                    if (service->Ipv4 != 0)
                        addresses.Add(new IPAddress(new ReadOnlySpan<byte>((void*)service->Ipv4, 4)).ToString());
                    if (service->Ipv6 != 0)
                    {
                        var address = new IPAddress(new ReadOnlySpan<byte>((void*)service->Ipv6, 16));
                        if (address.IsIPv6LinkLocal) address.ScopeId = service->InterfaceIndex;
                        addresses.Add(address.ToString());
                    }
                    resolved = new(txt, addresses, service->Port, Marshal.PtrToStringUni(service->Host));
                }
            }
        }
        catch (Exception) { }
        finally
        {
            if (instance != 0) FreeInstance(instance);
            ThreadPool.UnsafeQueueUserWorkItem(static item => item.Query.Owner.OnResolved(item.Query, item.Resolution),
                (Query: query, Resolution: resolved), preferLocal: false);
        }
    }

    private void OnResolved(ResolveQuery query, Resolution? result)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || !_pending.TryGetValue(query.Name, out var active) || active != query) return;
                _pending.Remove(query.Name);
                if (result is not null)
                {
                    var sourceId = SourceId(query.Name);
                    publish(sourceId, query.Name, result.Txt);
                    media.UpdateEndpoint(sourceId, query.Name, result.Txt, result.Addresses, result.Port, result.Host);
                }
            }
        }
        finally { query.FreeName(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Browsers.TryRemove(_id, out _);
            if (_cancel != 0) BrowseCancel(ref _cancel);
            if (_name != 0) Marshal.FreeHGlobal(_name);
            foreach (var query in _pending.Values) Cancel(query);
            foreach (var name in _lastQuery.Keys) Withdraw(name);
            _pending.Clear();
            _lastQuery.Clear();
        }
    }

    private void Withdraw(string name)
    {
        var sourceId = SourceId(name);
        publish(sourceId, null, null);
        media.UpdateEndpoint(sourceId, null, null, null, null, null);
    }

    private static void Cancel(ResolveQuery query)
    {
        if (!Resolvers.TryRemove(query.Id, out _)) return; // The queued completion owns the query name.
        if (query.CancelHandle != 0) ResolveCancel(ref query.CancelHandle);
        query.FreeName();
    }

    private static nint NewContext() => Interlocked.Increment(ref _nextContext);
    private static string SourceId(string name) => "dnsapi:" + name.ToLowerInvariant();

    private sealed record PointerUpdate(WindowsMediaDnsBrowser Owner, string Name, bool Remove);
    private sealed record Resolution(List<string> Txt, List<string> Addresses, ushort Port, string? Host);
    private sealed class ResolveQuery(WindowsMediaDnsBrowser owner, string name, nint id, nint nativeName)
    {
        internal readonly WindowsMediaDnsBrowser Owner = owner;
        internal readonly string Name = name;
        internal readonly nint Id = id;
        internal readonly nint NativeName = nativeName;
        internal nint CancelHandle;
        internal void FreeName() => Marshal.FreeHGlobal(NativeName);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Request { internal uint Version, InterfaceIndex; internal nint Name, Callback, Context; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Record
    {
        internal nint Next, Name;
        internal ushort Type, DataLength;
        internal uint Flags, Ttl, Reserved;
        internal nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceInstance
    {
        internal nint Name, Host, Ipv4, Ipv6;
        internal ushort Port, Priority, Weight;
        internal uint PropertyCount;
        internal nint Keys, Values;
        internal uint InterfaceIndex;
    }

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceBrowse")]
    private static partial uint Browse(ref Request request, ref nint cancellation);
    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceBrowseCancel")]
    private static partial uint BrowseCancel(ref nint cancellation);
    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceResolve")]
    private static partial uint Resolve(ref Request request, ref nint cancellation);
    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceResolveCancel")]
    private static partial uint ResolveCancel(ref nint cancellation);
    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceFreeInstance")]
    private static partial void FreeInstance(nint instance);
    [LibraryImport("dnsapi.dll", EntryPoint = "DnsFree")]
    private static partial void FreeRecords(nint records, uint type);
}
