using Microsoft.UI.Xaml;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Pages;
using System.Globalization;
using System.Linq;

namespace Mizon.Windows.UserControls;

public sealed partial class LatestMusicNewsPanel
{
    internal int McpSelectedIndex => _selectedIndex;
    internal bool McpDialogOpen => _dialog is not null;
    internal McpMusicHomeItemState[] McpItems => _items.Select((item, index) => new McpMusicHomeItemState
    {
        Id = "news:" + index.ToString(CultureInfo.InvariantCulture), Kind = "news", Title = item.Title,
        Subtitle = item.Category, Description = item.Summary, IsDemo = true, IsAvailable = true,
        CanOpen = true, ImageAvailable = true, IsSelected = index == _selectedIndex, Actions = ["open", "select"]
    }).ToArray();
    internal void McpDismiss() => _dialog?.Hide();
    internal McpResponse ExecuteMcp(McpMusicHomeRequest request)
    {
        if (!_loaded || _items.Length == 0) return MusicPage.Missing();
        switch (request.Action)
        {
            case "previous": SelectRelative(-1); break;
            case "next": SelectRelative(1); break;
            case "select":
                if (request.Index is not { } index || index >= _items.Length) return MusicPage.Missing();
                Select(index, true); break;
            case "pause": _paused = true; UpdateRotationButton(); ScheduleRotation(); break;
            case "resume": _paused = false; UpdateRotationButton(); ScheduleRotation(); break;
            case "open":
                var selected = request.Id is null ? _selectedIndex : System.Array.FindIndex(McpItems, item => item.Id == request.Id);
                if (selected < 0) return MusicPage.Missing();
                if (_dialog is not null) return new() { Code = "busy", Message = "A news preview is already open." };
                StopTransition();
                Select(selected, false);
                FinishTransition();
                Read_Click(this, new RoutedEventArgs());
                return _dialog is null ? MusicPage.Unavailable("The preview could not be opened.") : MusicPage.Done("preview_opened");
            default: return MusicPage.Unavailable("This action is not supported by the news widget.");
        }
        return MusicPage.Done("updated");
    }
}
