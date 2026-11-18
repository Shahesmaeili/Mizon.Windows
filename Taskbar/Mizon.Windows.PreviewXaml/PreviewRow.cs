namespace Mizon.Windows.PreviewXaml;

internal sealed unsafe class PreviewRow : IDisposable
{
    private static readonly Guid VectorId = new("B4C1E3AC-8768-5B9D-A661-F63330B8507B");
    private static readonly Guid ElementId = new("676D0BE9-B65C-41C6-BA40-58CF87F201C1");
    private nint _card, _panel, _vector, _row, _definitions;
    private readonly List<PreviewInputBinding> _input = [];
    private uint _addedDefinitions;
    private double _oldCardHeight, _oldPanelHeight;
    private bool _disposed;
    private bool _layoutSaved;
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

    internal static bool Matches(nint card, string title)
    {
        var tree = Tree();
        var remaining = 200;
        try { return ContainsTitle(tree, card, title, 0, ref remaining); }
        finally { Native.Release(tree); }
    }

    private static bool ContainsTitle(nint tree, nint element, string title, int depth, ref int remaining)
    {
        if (depth > 14 || remaining-- <= 0) return false;
        var text = Native.Query(element, Native.TextBlock);
        if (text != 0)
        {
            try { if (Native.GetText(text, 26) == title) return true; }
            finally { Native.Release(text); }
        }
        var dependency = Native.Query(element, Native.Dependency);
        if (dependency == 0) return false;
        try
        {
            int count;
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, int*, int>)Native.Slot(tree, 11))(tree, dependency, &count));
            for (var index = 0; index < count && remaining > 0; index++)
            {
                nint child = 0;
                Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Native.Slot(tree, 10))(tree, dependency, index, &child));
                try { if (ContainsTitle(tree, child, title, depth + 1, ref remaining)) return true; }
                finally { Native.Release(child); }
            }
            return false;
        }
        finally { Native.Release(dependency); }
    }

    internal static PreviewRow? Attach(nint card, bool persian)
    {
        var framework = Native.Query(card, Native.Framework);
        if (framework == 0) return null;
        PreviewRow? result = null;
        try
        {
            var height = Native.GetDouble(framework, 14);
            if (height < 40 || height > 800) return null;
            result = new PreviewRow { _card = framework, Persian = persian };
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
        BindClick("MizonPreviewPlaylists", "mizon://playlists");
        BindClick("MizonPreviewDownloads", "mizon://settings/storage");
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

    private void BindClick(string name, string destination)
    {
        var framework = Native.Query(_row, Native.Framework);
        var text = Native.HString(name);
        nint button = 0;
        try
        {
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Native.Slot(framework, 51))(framework, text, &button));
            if (button == 0) throw new InvalidOperationException("Missing named preview action.");
            _input.Add(PreviewInputBinding.Click(button, destination));
        }
        finally { Native.Release(button); Native.WindowsDeleteString(text); Native.Release(framework); }
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

    internal static void VerifyAttachment()
    {
        var fixture = LoadMarkup("""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Height="200" Width="360">
                <Grid><Border Name="BackgroundBorder"/><StackPanel><TextBlock Text="Mizon preview fixture"/></StackPanel></Grid>
            </Grid>
            """);
        try
        {
            if (!Matches(fixture, "Mizon preview fixture")) throw new InvalidOperationException("Fixture title matching failed.");
            var framework = Native.Query(fixture, Native.Framework);
            var row = new PreviewRow { _card = framework, Persian = true };
            row.AttachCore(fixture, 200);
            if (Native.GetDouble(framework, 17) != 250) throw new InvalidOperationException("Preview space was not reserved.");
            row.Dispose();
            var restored = Native.Query(fixture, Native.Framework);
            try
            {
                if (Native.GetDouble(restored, 17) != 200) throw new InvalidOperationException("Original preview height was not restored.");
            }
            finally { Native.Release(restored); }
        }
        finally { Native.Release(fixture); }
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
