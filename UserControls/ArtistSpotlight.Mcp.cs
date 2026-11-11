namespace Mizon.Windows.UserControls;

public sealed partial class ArtistSpotlight
{
    internal int McpSelectedIndex => _index;
    internal void McpMove(int direction) => SelectRelative(direction);
    internal bool McpSelect(int index)
    {
        if (!_loaded || ItemsSource is not { } items || index < 0 || index >= items.Count) return false;
        if (index == _index) return true;
        SelectRelative(index - _index);
        return true;
    }
    internal void McpResetModel() => ArtistModel.ResetView();
}
