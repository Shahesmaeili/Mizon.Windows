using Mizon.Windows.Services;

namespace Mizon.Windows.PreviewXaml;

internal sealed unsafe class PreviewRow : IDisposable
{
    private static readonly Guid VectorId = new("B4C1E3AC-8768-5B9D-A661-F63330B8507B");
    private static readonly Guid ElementId = new("676D0BE9-B65C-41C6-BA40-58CF87F201C1");
    private const int PresenceRowHeight = 66;
    private PreviewOnlinePanel? _presence;
    private nint _card, _panel, _vector, _row, _definitions;
    private uint _addedDefinitions;
    private double _oldCardHeight, _oldPanelHeight;
    private bool _disposed;
    private bool _layoutSaved;
    private PreviewConfiguration _owner;
    private string? _lastCaption;
    private long? _captionMismatchSince;
    internal bool Persian { get; private set; }

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

    internal static PreviewRow? Attach(nint card, PreviewConfiguration configuration)
    {
        var framework = Native.Query(card, Native.Framework);
        if (framework == 0) return null;
        PreviewRow? result = null;
        try
        {
            var height = Native.GetDouble(framework, 14);
            if (height < 40 || height > 800) return null;
            result = new PreviewRow
            {
                _card = framework, Persian = configuration.Persian,
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
        AppendDefinition(PresenceRowHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _row = LoadMarkup(PreviewMarkup.Create(Persian, rowCount));
        _presence = new PreviewOnlinePanel(_row, Persian);
        _presence.Attach();
        uint size;
        Native.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Native.Slot(_vector, 7))(_vector, &size));
        if (size > 40) throw new InvalidOperationException("Unexpected thumbnail child count.");
        Native.PutDouble(_card, 18, height + PresenceRowHeight);
        Native.PutDouble(_panel, 18, height + PresenceRowHeight);
        var uiElement = Native.Query(_row, ElementId);
        try
        {
            Native.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, int>)Native.Slot(_vector, 11))(_vector, size, uiElement));
        }
        finally { Native.Release(uiElement); }
    }

    internal bool RefreshOnline(TaskbarOnlineSnapshot snapshot, long now) => _presence?.Apply(snapshot, now) == true;
    internal void AnimateOnline(long now) => _presence?.Frame(now);

    internal static nint FindName(nint framework, string name)
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
            var original = new PreviewConfiguration(1, 42, persian, "Mizon preview fixture");
            var updated = original with { Title = "Mizon - Track" };
            using (var row = new PreviewRow
            {
                _card = Native.Query(fixture, Native.Framework), Persian = persian, _owner = original, _lastCaption = original.Title
            })
            {
                row.AttachCore(fixture, 200);
                if (Native.GetDouble(row._card, 17) != 200 + PresenceRowHeight)
                    throw new InvalidOperationException("The two-row presence height was not reserved.");
                var connected = new TaskbarOnlineSnapshot(TaskbarOnlinePhase.Connected, 18, 4, true);
                row.RefreshOnline(connected, 0);
                row._presence!.VerifyDisplayed(18, 4);
                if (!row.RefreshOnline(connected with { OnlineCount = 32, OnlineFollowerCount = 2 }, 100))
                    throw new InvalidOperationException("Changing real counts must schedule an animation.");
                row.AnimateOnline(506);
                row._presence.VerifyDisplayed(25, 2);
                row.AnimateOnline(1_500);
                row._presence.VerifyDisplayed(32, 2);
                row.RefreshOnline(new(TaskbarOnlinePhase.NoInternet, null, null, false), 1_600);
                row._presence.VerifyDisplayed(null, null);
                if (!row.RetainFor(fixture, updated, 2_000))
                    throw new InvalidOperationException("The presence row did not remain attached across caption changes.");
                SetFixtureCaption(fixture, updated.Title);
                if (!row.RetainFor(fixture, updated, 2_010) ||
                    row.RetainFor(fixture, updated with { ProcessId = 43 }, 2_010))
                    throw new InvalidOperationException("Presence attachment lost the original window ownership.");
            }
            var restored = Native.Query(fixture, Native.Framework);
            try
            {
                if (Native.GetDouble(restored, 17) != 200)
                    throw new InvalidOperationException("Original preview height was not restored.");
            }
            finally { Native.Release(restored); }
            var motion = new PreviewCountMotion();
            motion.Set(18, false, 0);
            motion.Set(32, true, 0);
            var samples = Enumerable.Range(0, 27).Select(index => motion.Read(index * 32)!.Value).Distinct().ToArray();
            if (!samples.SequenceEqual(Enumerable.Range(18, 15)) ||
                motion.Read(100) - 18 >= motion.Read(456) - motion.Read(356) ||
                32 - motion.Read(712) >= motion.Read(456) - motion.Read(356))
                throw new InvalidOperationException("The count animation did not ease through all steps.");
            if (TaskbarOnlineSnapshot.Decode("1|42|4|32|2|1", 1, 42) is not { OnlineCount: 32, OnlineFollowerCount: 2 } ||
                TaskbarOnlineSnapshot.Decode("1|43|4|32|2|1", 1, 42) is not null)
                throw new InvalidOperationException("Preview IPC must validate the owner and both counts.");
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
            _presence?.Dispose();
            _presence = null;
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
