using Mizon.API.Client.Responses.Search;
using System.Net;
using System.Runtime.CompilerServices;

namespace Mizon.Windows.PaletteProvider;

internal sealed class PaletteSearchSession : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PaletteResultItem> _items = new(StringComparer.Ordinal);
    private readonly HashSet<string> _currentKeys = new(StringComparer.Ordinal);
    private readonly Func<string, CancellationToken, IAsyncEnumerable<GlobalSearchItemResponse>>? _search;
    private readonly TimeSpan _searchTimeout;
    private nint _factory;
    private PendingQueryOperation? _query;
    private PaletteCapabilityDemoItem? _demo;
    private Guid _authenticationRevision;
    private long _generation;
    private bool _closed;

    internal PaletteSearchSession(nint factory,
        Func<string, CancellationToken, IAsyncEnumerable<GlobalSearchItemResponse>>? search = null,
        TimeSpan? searchTimeout = null)
    {
        NativeMethods.AddRef(factory); _factory = factory;
        _search = search; _searchTimeout = searchTimeout ?? TimeSpan.FromSeconds(8);
    }
    internal nint Start(nint query, string text)
    {
        PendingQueryOperation operation = new();
        PendingQueryOperation? previous; nint factory; long generation;
        lock (_gate)
        {
            if (_closed) { NativeMethods.Release(operation.Identity); throw new ObjectDisposedException(nameof(PaletteSearchSession)); }
            previous = _query; _query = operation; generation = ++_generation;
            _currentKeys.Clear();
            factory = _factory; NativeMethods.AddRef(factory); NativeMethods.AddRef(query);
        }
        previous?.RequestCancellation();
        operation.Start(async (pending, token) =>
        {
            var views = new List<nint>();
            try
            {
                if (PaletteCapabilityDemoItem.MatchesQuery(text))
                {
                    token.ThrowIfCancellationRequested();
                    using var apartment = new ComApartment();
                    lock (_gate)
                    {
                        if (_closed || _generation != generation) throw new OperationCanceledException(token);
                        _demo ??= new PaletteCapabilityDemoItem(factory);
                        views.Add(_demo.AcquireView());
                    }
                    Log.Write("Local capability demo result supplied; no API request.");
                    return PaletteData.BuildResults(query, views.ToArray());
                }
                if (PaletteSearchFallback.Create(text) is null) return Results(query, views);
                if (text.Length < 2) return Fallback(query, factory, text, null, generation, token);
                await Task.Delay(180, token).ConfigureAwait(false);
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestCancellation.CancelAfter(_searchTimeout);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                await foreach (var response in SearchAsync(text, generation, requestCancellation.Token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    var data = PaletteSearchRelevance.Apply(Map(response), text);
                    if (!seen.Add(data.Key)) continue;
                    PaletteResultItem item;
                    using (var apartment = new ComApartment())
                    {
                        lock (_gate)
                        {
                            if (_closed || _generation != generation) throw new OperationCanceledException(token);
                            if (!_items.TryGetValue(data.Key, out item!))
                            {
                                item = PaletteData.BuildResult(factory, new PaletteResultState(data));
                                _items.Add(data.Key, item);
                            }
                            _currentKeys.Add(data.Key);
                            views.Add(item.AcquireView());
                        }
                        var mediaId = response.ItemType == "Music" ? response.CoverMediaPublicId : response.ProfileMediaPublicId;
                        item.State.UpdateSearchSummary(data, mediaId);
                        // RunUX replaces the provider's result set on each progress
                        // event. Send cumulative snapshots, never append-only deltas.
                        var progress = PaletteData.BuildProgress(views.ToArray());
                        try { pending.Report(progress); }
                        finally { NativeMethods.Release(progress); }
                    }
                    TrimCache(seen, generation);
                    if (views.Count == 20) break;
                }
                TrimCache(seen, generation);
                Log.Write($"Live API search complete: results={views.Count}; generation={generation}");
                return views.Count != 0 ? Results(query, views)
                    : Fallback(query, factory, text, "ادامهٔ جستجو در برنامه", generation, token);
            }
            catch (PaletteLoginRequiredException)
            {
                return views.Count != 0 ? Results(query, views)
                    : Fallback(query, factory, text, "ورود به حساب و ادامهٔ جستجو", generation, token);
            }
            catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
            {
                return views.Count != 0 ? Results(query, views)
                    : Fallback(query, factory, text, "ورود دوباره و ادامهٔ جستجو", generation, token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return views.Count != 0 ? Results(query, views)
                    : Fallback(query, factory, text, "پاسخ سرور دیر رسید؛ تلاش در برنامه", generation, token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log.Write($"Live API search unavailable: {error.GetType().Name}");
                return views.Count != 0 ? Results(query, views)
                    : Fallback(query, factory, text, "جستجو کامل نشد؛ تلاش در برنامه", generation, token);
            }
            finally
            {
                using var apartment = new ComApartment();
                foreach (var view in views) NativeMethods.Release(view);
                NativeMethods.Release(factory); NativeMethods.Release(query);
            }
        });
        return operation.Identity;
    }
    private async IAsyncEnumerable<GlobalSearchItemResponse> SearchAsync(string text, long generation,
        [EnumeratorCancellation] CancellationToken token)
    {
        if (_search is not null)
        {
            await foreach (var item in _search(text, token).ConfigureAwait(false)) yield return item;
            yield break;
        }
        var authentication = PaletteApiSession.RequireAuthentication();
        ResetAuthentication(authentication.Revision, generation, token);
        using var api = new PaletteApiSession(authentication);
        await foreach (var item in api.SearchAsync(text, token).ConfigureAwait(false)) yield return item;
    }
    private void ResetAuthentication(Guid revision, long generation, CancellationToken token)
    {
        PaletteResultItem[] old = [];
        lock (_gate)
        {
            if (_closed || _generation != generation) throw new OperationCanceledException(token);
            if (_authenticationRevision != revision)
            { old = [.._items.Values]; _items.Clear(); _authenticationRevision = revision; }
        }
        if (old.Length == 0) return;
        using var apartment = new ComApartment();
        foreach(var item in old) item.Dispose();
    }
    private void TrimCache(HashSet<string> current, long generation)
    {
        var removed = new List<PaletteResultItem>();
        lock (_gate)
        {
            if (_closed || _generation != generation) return;
            if (_items.Count <= 80) return;
            foreach (var key in _items.Keys.ToArray())
            {
                if (_items.Count <= 80) break;
                if (current.Contains(key) || _currentKeys.Contains(key) || _items[key].State.GetSnapshot().IsBusy) continue;
                removed.Add(_items[key]); _items.Remove(key);
            }
        }
        using var apartment = new ComApartment();
        foreach (var item in removed) item.Dispose();
    }
    internal nint GetItem(string key)
    {
        PaletteResultState state;
        nint view;
        long generation;
        lock (_gate)
        {
            if (_closed) return 0;
            if (key == PaletteCapabilityDemoItem.Key)
            {
                _demo ??= new PaletteCapabilityDemoItem(_factory);
                return _demo.AcquireView();
            }
            if (_items.TryGetValue(key, out var existing)) return existing.AcquireView();
            if (PaletteSearchFallback.FromKey(key) is { } search) return search.BuildView(_factory);
            var parts = key.Split('/');
            if (parts.Length != 2 || parts[0] is not ("music" or "client") || !Guid.TryParse(parts[1],out var id) || id == Guid.Empty) return 0;
            state = new PaletteResultState(new() { Kind=parts[0] == "music" ? PaletteResultKind.Music : PaletteResultKind.Client, PublicId=id });
            var item = PaletteData.BuildResult(_factory,state);
            _items[key]=item;
            view = item.AcquireView();
            generation = _generation;
        }
        TrimCache(new HashSet<string>(StringComparer.Ordinal) { key }, generation);
        state.RequestDetails();
        return view;
    }
    private static PaletteResultData Map(GlobalSearchItemResponse response) => new()
    {
        Kind = response.ItemType == "Music" ? PaletteResultKind.Music : PaletteResultKind.Client,
        PublicId=response.PublicId, Title=response.Title, PersianTitle=response.PersianTitle,
        Username=response.Username, FirstName=response.FirstName, LastName=response.LastName, NickName=response.NickName
    };
    private static nint Results(nint query, List<nint> views)
    { using var apartment = new ComApartment(); return PaletteData.BuildResults(query, views.ToArray()); }
    private nint Fallback(nint query, nint factory, string text, string? reason, long generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
            if (_closed || _generation != generation) throw new OperationCanceledException(token);
        using var apartment = new ComApartment();
        var item = PaletteSearchFallback.Create(text)?.BuildView(factory, reason) ?? 0;
        try { return PaletteData.BuildResults(query,item); }
        finally { NativeMethods.Release(item); }
    }
    public void Dispose()
    {
        PaletteResultItem[] items; nint factory; PendingQueryOperation? query; PaletteCapabilityDemoItem? demo;
        lock (_gate)
        {
            if (_closed) return; _closed=true; ++_generation;
            items=[.._items.Values]; _items.Clear(); factory=_factory; _factory=0; query=_query; _query=null;
            demo = _demo; _demo = null;
            _currentKeys.Clear();
        }
        query?.RequestCancellation();
        using var apartment = new ComApartment();
        demo?.Dispose();
        foreach(var item in items) item.Dispose();
        NativeMethods.Release(factory);
    }
}
