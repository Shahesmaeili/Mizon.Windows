using System.Linq;

namespace Mizon.Windows.UserControls;

public sealed partial class WeeklyRhythmPanel
{
    internal int? McpSelectedIndex => _selectedDay?.Index;
    internal bool McpSelect(int index)
    {
        if (!_isPanelLoaded || Summary?.Days.FirstOrDefault(day => day.Index == index) is not { } day) return false;
        _selectedDay = day;
        ShowSelectedDay();
        return true;
    }
}
