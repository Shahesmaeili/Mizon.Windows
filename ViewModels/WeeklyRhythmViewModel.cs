using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System.Collections.Generic;
using System.Linq;

namespace Mizon.Windows.ViewModels;

/// <summary>Consistent local preview statistics for the home page's weekly listening widget.</summary>
public sealed partial class WeeklyRhythmViewModel : ViewModelBase
{
    public LocalizationService Localization => LocalizationService.Current;
    private readonly int _totalMinutes;
    private readonly double _comparisonPercent;
    public IReadOnlyList<WeeklyRhythmDayItemViewModel> Days { get; }
    public string TotalDurationText { get; }
    public string TotalAccessibleText => Localization.Format("MusicWidgets/RhythmTotal", _totalMinutes / 60, _totalMinutes % 60);
    public string MusicDurationText { get; }
    public string VideoDurationText { get; }
    public string ComparisonText => Localization.Format("MusicWidgets/RhythmPercent", _comparisonPercent);
    public string ComparisonDescription => Localization.GetString("MusicWidgets/RhythmComparison");
    public string BusiestDayText => Localization.Format("MusicWidgets/RhythmPeak", Days.First(day => day.IsPeak).DayName);
    public bool IsDemo => true;

    public void RefreshLocalization()
    {
        foreach (var day in Days) day.RefreshLocalization();
        OnPropertyChanged(nameof(TotalAccessibleText));
        OnPropertyChanged(nameof(ComparisonText));
        OnPropertyChanged(nameof(ComparisonDescription));
        OnPropertyChanged(nameof(BusiestDayText));
    }

    public WeeklyRhythmViewModel()
    {
        // The existing 08:42 total and +18% preview now come from the same seven daily samples.
        (int Music, int Video)[] samples =
        [
            (38, 10), (54, 18), (46, 10), (66, 26), (52, 16), (72, 32), (62, 20)
        ];
        const int previousWeekMinutes = 442;
        var maximumMinutes = samples.Max(day => day.Music + day.Video);
        Days = samples.Select((day, index) => new WeeklyRhythmDayItemViewModel(
            index, day.Music, day.Video, maximumMinutes)).ToArray();
        var musicMinutes = samples.Sum(day => day.Music);
        var videoMinutes = samples.Sum(day => day.Video);
        var totalMinutes = musicMinutes + videoMinutes;
        TotalDurationText = WeeklyRhythmDayItemViewModel.FormatDuration(totalMinutes, padHours: true);
        _totalMinutes = totalMinutes;
        MusicDurationText = WeeklyRhythmDayItemViewModel.FormatDuration(musicMinutes);
        VideoDurationText = WeeklyRhythmDayItemViewModel.FormatDuration(videoMinutes);
        _comparisonPercent = (totalMinutes - previousWeekMinutes) * 100d / previousWeekMinutes;
    }
}
