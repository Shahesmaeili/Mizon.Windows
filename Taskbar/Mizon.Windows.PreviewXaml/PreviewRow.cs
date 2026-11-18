using Mizon.Windows.Services;

namespace Mizon.Windows.PreviewXaml;

internal sealed unsafe class PreviewRow : IDisposable
{
    private static readonly Guid VectorId = new("B4C1E3AC-8768-5B9D-A661-F63330B8507B");
    private static readonly Guid ElementId = new("676D0BE9-B65C-41C6-BA40-58CF87F201C1");
    private static readonly PreviewActionRequest Playlists = new(PreviewActionRequest.LaunchAsync);
    private static readonly PreviewActionRequest Downloads = new(PreviewActionRequest.LaunchAsync);
    private readonly PreviewActionRequest _playlists;
    private readonly PreviewActionRequest _downloads;
    private Action? _refresh;
    private nint _card, _panel, _vector, _row, _definitions;
    private readonly List<PreviewInputBinding> _input = [];
    private uint _addedDefinitions;
    private double _oldCardHeight, _oldPanelHeight;
    private bool _disposed;
    private bool _layoutSaved;
    private PreviewConfiguration _owner;
    private string? _lastCaption;
    private long? _captionMismatchSince;
    internal bool Persian { get; private set; }

    private PreviewRow(PreviewActionRequest playlists, PreviewActionRequest downloads)
    {
        _playlists = playlists;
        _downloads = downloads;
    }

    private static nint Tree() => Native.Factory("Windows.UI.Xaml.Media.VisualTreeHelper",
        new Guid("E75758C4-D25D-4B1D-971F-596F17F12BAA"));

    internal static nint FindCard(nint element)
    {
        var tree = Tree();
        var current = Native.Query(element, Native.Dependency);
        try
        {
            for (var depth = 0; depth < 24 && current != 0; depth++)
            {
                var name = Native.ClassName(current);
                if (name == "Taskbar.TaskItemThumbnailView")
                {
                    var result = current;
                    current = 0;
                    return result;
                }
                nint parent = 0;
                var hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Native.Slot(tree, 12))(tree, current, &parent);
                Native.Release(current);
                current = 0;
                if (hr < 0) break;
                current = parent;
            }
            return 0;
        }
        finally { Native.Release(current); Native.Release(tree); }
    }

    internal static bool Matches(nint card, string title) => MatchCaption(card, title, null) == CaptionMatch.Current;

    internal bool RetainFor(nint card, PreviewConfiguration configuration, long now)
    {
        if (_owner.Window != configuration.Window || _owner.ProcessId != configuration.ProcessId) return false;
        switch (MatchCaption(card, configuration.Title, _lastCaption))
        {
            case CaptionMatch.Current:
                _lastCaption = configuration.Title;
                _captionMismatchSince = null;
                return true;
            case CaptionMatch.Previous:
                // Explorer can keep showing the old title for this exact card
                // until it closes, even though the HWND already has its new title.
                _captionMismatchSince = null;
                return true;
            default:
                if (configuration.Title == _lastCaption) return false;
                _captionMismatchSince ??= now;
                return now - _captionMismatchSince.Value < 2_000;
        }
    }

    private enum CaptionMatch { None, Previous, Current }

    private static CaptionMatch MatchCaption(nint card, string current, string? previous)
    {
        var tree = Tree();
        var remaining = 200;
        try { return FindCaption(tree, card, current, previous, 0, ref remaining); }
        finally { Native.Release(tree); }
    }

    private static CaptionMatch FindCaption(nint tree, nint element, string current, string? previous, int depth, ref int remaining)
    {
        if (depth > 14 || remaining-- <= 0) return CaptionMatch.None;
        var text = Native.Query(element, Native.TextBlock);
        if (text != 0)
        {
            try
            {
                var value = Native.GetText(text, 26);
                if (value == current) return CaptionMatch.Current;
                if (value == previous) return CaptionMatch.Previous;
            }
            finally { Native.Release(text); }
        }
        var dependency = Native.Query(element, Native.Dependency);
        if (dependency == 0) return CaptionMatch.None;
        try
        {
            int count;
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, int*, int>)Native.Slot(tree, 11))(tree, dependency, &count));
            var match = CaptionMatch.None;
            for (var index = 0; index < count && remaining > 0; index++)
            {
                nint child = 0;
                Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Native.Slot(tree, 10))(tree, dependency, index, &child));
                try
                {
                    var found = FindCaption(tree, child, current, previous, depth + 1, ref remaining);
                    if (found == CaptionMatch.Current) return found;
                    if (found == CaptionMatch.Previous) match = found;
                }
                finally { Native.Release(child); }
            }
            return match;
        }
        finally { Native.Release(dependency); }
    }

    internal static PreviewRow? Attach(nint card, PreviewConfiguration configuration, Action refresh)
    {
        var framework = Native.Query(card, Native.Framework);
        if (framework == 0) return null;
        PreviewRow? result = null;
        try
        {
            var height = Native.GetDouble(framework, 14);
            if (height < 40 || height > 800) return null;
            result = new PreviewRow(Playlists, Downloads)
            {
                _card = framework, Persian = configuration.Persian, _refresh = refresh,
                _owner = configuration, _lastCaption = configuration.Title
            };
            framework = 0;
            result.AttachCore(card, height);
            return result;
        }
        catch { result?.Dispose(); throw; }
        finally { Native.Release(framework); }
    }

    private void AttachCore(nint card, double height)
    {
        var tree = Tree();
        var dependency = Native.Query(card, Native.Dependency);
        nint root = 0;
        try
        {
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Native.Slot(tree, 10))(tree, dependency, 0, &root));
            _panel = Native.Query(root, Native.Framework);
            var panel = Native.Query(root, Native.Panel);
            if (panel == 0 || _panel == 0) throw new InvalidOperationException("Unsupported thumbnail template root.");
            try
            {
                var children = Native.GetObject(panel, 6);
                try { _vector = Native.Query(children, VectorId); }
                finally { Native.Release(children); }
            }
            finally { Native.Release(panel); }
            var grid = Native.Query(root, new Guid("FD104460-2E15-4BA3-8B8F-FA693A4161E9"));
            if (grid == 0) throw new InvalidOperationException("The thumbnail template is not a Grid.");
            try
            {
                var definitions = Native.GetObject(grid, 6);
                try { _definitions = Native.Query(definitions, new Guid("62225981-7A0D-5D38-8088-7C6BB6D959BE")); }
                finally { Native.Release(definitions); }
            }
            finally { Native.Release(grid); }
        }
        finally { Native.Release(tree); Native.Release(dependency); Native.Release(root); }
        if (_vector == 0) throw new InvalidOperationException("Thumbnail children are not an editable UIElement vector.");

        _oldCardHeight = Native.GetDouble(_card, 17);
        _oldPanelHeight = Native.GetDouble(_panel, 17);
        _layoutSaved = true;
        if (_definitions == 0) throw new InvalidOperationException("The thumbnail Grid rows are unavailable.");
        uint rowCount;
        Native.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Native.Slot(_definitions, 7))(_definitions, &rowCount));
        if (rowCount > 20) throw new InvalidOperationException("Unexpected thumbnail Grid row count.");
        if (rowCount == 0) { AppendDefinition("*"); rowCount = 1; }
        AppendDefinition("50");
        _row = LoadMarkup(PreviewMarkup.Create(Persian, rowCount));
        // Custom input must not bubble into Explorer's thumbnail activation handlers.
        _input.Add(PreviewInputBinding.Pointer(_row, 57));
        _input.Add(PreviewInputBinding.Pointer(_row, 61));
        _input.Add(PreviewInputBinding.Tapped(_row));
        BindClick("Playlists", TaskbarPreviewDeepLink.Playlists, _playlists);
        BindClick("Downloads", TaskbarPreviewDeepLink.Storage, _downloads);
        RefreshLoading();
        uint size;
        Native.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Native.Slot(_vector, 7))(_vector, &size));
        if (size > 40) throw new InvalidOperationException("Unexpected thumbnail child count.");
        Native.PutDouble(_card, 18, height + 50);
        Native.PutDouble(_panel, 18, height + 50);
        var uiElement = Native.Query(_row, ElementId);
        try
        {
            Native.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, int>)Native.Slot(_vector, 11))(_vector, size, uiElement));
        }
        finally { Native.Release(uiElement); }
    }

    internal void RefreshLoading()
    {
        foreach (var binding in _input) binding.RefreshLoading();
    }

    private void BindClick(string name, string destination, PreviewActionRequest request)
    {
        var framework = Native.Query(_row, Native.Framework);
        nint button = 0, icon = 0, ring = 0;
        try
        {
            button = FindName(framework, "MizonPreview" + name);
            icon = FindName(framework, name + "Icon");
            ring = FindName(framework, name + "Loading");
            var progress = new PreviewButtonProgress(button, icon, ring, Persian);
            _input.Add(PreviewInputBinding.Click(button, destination, request, progress, _refresh));
        }
        finally
        {
            Native.Release(button);
            Native.Release(icon);
            Native.Release(ring);
            Native.Release(framework);
        }
    }

    private static nint FindName(nint framework, string name)
    {
        var text = Native.HString(name);
        try
        {
            nint element = 0;
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Native.Slot(framework, 51))(framework, text, &element));
            if (element == 0) throw new InvalidOperationException("Missing named preview element.");
            return element;
        }
        finally { Native.WindowsDeleteString(text); }
    }

    private void AppendDefinition(string height)
    {
        var definition = LoadMarkup("<RowDefinition xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Height=\"" + height + "\"/>");
        var row = Native.Query(definition, new Guid("4ABAE829-D80C-4A5E-A48C-F8B3D3B6533D"));
        try
        {
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Native.Slot(_definitions, 13))(_definitions, row));
            _addedDefinitions++;
        }
        finally { Native.Release(row); Native.Release(definition); }
    }

    internal static nint LoadMarkup(string markup)
    {
        var factory = Native.Factory("Windows.UI.Xaml.Markup.XamlReader", new Guid("9891C6BD-534F-4955-B85A-8A8DC0DCA602"));
        var text = Native.HString(markup);
        try
        {
            nint result = 0;
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Native.Slot(factory, 6))(factory, text, &result));
            return result;
        }
        finally { Native.WindowsDeleteString(text); Native.Release(factory); }
    }

    internal static void VerifyAttachment(bool persian)
    {
        var fixture = LoadMarkup("""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Height="200" Width="360">
                <Grid><Border Name="BackgroundBorder"/><StackPanel><TextBlock Name="FixtureCaption" Text="Mizon preview fixture"/></StackPanel></Grid>
            </Grid>
            """);
        try
        {
            if (!Matches(fixture, "Mizon preview fixture")) throw new InvalidOperationException("Fixture title matching failed.");
            var original = new PreviewConfiguration(1, 42, persian, "Mizon preview fixture");
            var updated = original with { Title = "Mizon - Playlists" };
            var playlistReply = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var downloadReply = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var playlists = new PreviewActionRequest(_ => playlistReply.Task);
            var downloads = new PreviewActionRequest(_ => downloadReply.Task);

            PreviewRow MakeRow()
            {
                var framework = Native.Query(fixture, Native.Framework);
                var row = new PreviewRow(playlists, downloads)
                {
                    _card = framework, Persian = persian, _owner = original, _lastCaption = original.Title
                };
                try
                {
                    row.AttachCore(fixture, 200);
                    if (Native.GetDouble(framework, 17) != 250)
                        throw new InvalidOperationException("Preview space was not reserved.");
                    return row;
                }
                catch { row.Dispose(); throw; }
            }

            try
            {
                var pendingPlaylist = playlists.Start(TaskbarPreviewDeepLink.Playlists, null)
                    ?? throw new InvalidOperationException("The playlist request was not started.");
                using (var first = MakeRow())
                {
                    first._input[3].VerifyLoading(true);
                    first._input[4].VerifyLoading(false);
                    if (playlists.Start(TaskbarPreviewDeepLink.Playlists, null) is not null)
                        throw new InvalidOperationException("A second click started a duplicate request.");
                }
                using var reopened = MakeRow();
                reopened._input[3].VerifyLoading(true);
                var pendingDownload = downloads.Start(TaskbarPreviewDeepLink.Storage, null)
                    ?? throw new InvalidOperationException("The storage request was not started.");
                reopened.RefreshLoading();
                reopened._input[4].VerifyLoading(true);
                playlistReply.SetResult(0);
                pendingPlaylist.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                if (!reopened.RetainFor(fixture, updated, 0) || !reopened.RetainFor(fixture, updated, 3_000))
                    throw new InvalidOperationException("The row detached while Explorer still showed the last caption.");
                SetFixtureCaption(fixture, "");
                if (!reopened.RetainFor(fixture, updated, 3_100) || reopened.RetainFor(fixture, updated, 5_100))
                    throw new InvalidOperationException("A temporarily missing caption had the wrong retention window.");
                SetFixtureCaption(fixture, updated.Title);
                if (!reopened.RetainFor(fixture, updated, 5_200)
                    || reopened.RetainFor(fixture, updated with { Window = 2 }, 5_200)
                    || reopened.RetainFor(fixture, updated with { ProcessId = 43 }, 5_200))
                    throw new InvalidOperationException("Caption retention did not stay bound to the original window.");
                SetFixtureCaption(fixture, "Another window");
                if (reopened.RetainFor(fixture, updated, 5_300))
                    throw new InvalidOperationException("An unrelated caption could keep the row after the new caption was acknowledged.");
                reopened.RefreshLoading();
                if (Native.GetDouble(reopened._card, 17) != 250)
                    throw new InvalidOperationException("A progress response changed the reserved preview height.");
                reopened._input[3].VerifyLoading(false);
                reopened._input[4].VerifyLoading(true);
                downloadReply.SetResult(3);
                pendingDownload.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                reopened.RefreshLoading();
                reopened._input[4].VerifyLoading(false);
            }
            finally
            {
                playlistReply.TrySetResult(2);
                downloadReply.TrySetResult(2);
            }
            var restored = Native.Query(fixture, Native.Framework);
            try
            {
                if (Native.GetDouble(restored, 17) != 200) throw new InvalidOperationException("Original preview height was not restored.");
            }
            finally { Native.Release(restored); }
        }
        finally { Native.Release(fixture); }
    }

    private static void SetFixtureCaption(nint fixture, string caption)
    {
        var framework = Native.Query(fixture, Native.Framework);
        nint element = 0, text = 0, value = 0;
        try
        {
            element = FindName(framework, "FixtureCaption");
            text = Native.Query(element, Native.TextBlock);
            value = Native.HString(caption);
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Native.Slot(text, 27))(text, value));
        }
        finally
        {
            Native.WindowsDeleteString(value);
            Native.Release(text);
            Native.Release(element);
            Native.Release(framework);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            foreach (var input in _input)
            {
                try { input.Dispose(); }
                catch (Exception error) { PreviewTap.Log("Remove preview input handler", error); }
            }
            _input.Clear();
            if (_vector != 0 && _row != 0)
            {
                var uiElement = Native.Query(_row, ElementId);
                try
                {
                    uint index;
                    byte found;
                    Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, uint*, byte*, int>)Native.Slot(_vector, 9))(_vector, uiElement, &index, &found));
                    if (found != 0) Native.Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Native.Slot(_vector, 12))(_vector, index));
                }
                finally { Native.Release(uiElement); }
            }
            while (_addedDefinitions != 0)
            {
                Native.Check(((delegate* unmanaged[Stdcall]<nint, int>)Native.Slot(_definitions, 14))(_definitions));
                _addedDefinitions--;
            }
            if (_card != 0 && _layoutSaved) Native.PutDouble(_card, 18, _oldCardHeight);
            if (_panel != 0 && _layoutSaved) Native.PutDouble(_panel, 18, _oldPanelHeight);
        }
        catch (Exception error) { PreviewTap.Log("Restore thumbnail", error); }
        finally
        {
            Native.Release(_definitions);
            Native.Release(_row); Native.Release(_vector); Native.Release(_panel); Native.Release(_card);
            _definitions = _row = _vector = _panel = _card = 0;
        }
    }
}
