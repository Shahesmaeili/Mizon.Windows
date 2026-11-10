using Mizon.Windows.ViewModels.Items;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mizon.Windows.ViewModels;

/// <summary>Consistent local preview statistics for the home page's weekly listening widget.</summary>
public sealed class WeeklyRhythmViewModel
{
    public IReadOnlyList<WeeklyRhythmDayItemViewModel> Days { get; }
    public string TotalDurationText { get; }
    public string TotalAccessibleText { get; }
    public string MusicDurationText { get; }
    public string VideoDurationText { get; }
    public string ComparisonText { get; }
    public string ComparisonDescription { get; } = "بیشتر از هفتهٔ قبل";
    public string BusiestDayText { get; }
    public bool IsDemo => true;

    public WeeklyRhythmViewModel()
    {
        // The existing 08:42 total and +18% preview now come from the same seven daily samples.
        (string ShortName, string DayName, int Music, int Video)[] samples =
        [
            ("ش", "شنبه", 38, 10),
            ("ی", "یکشنبه", 54, 18),
            ("د", "دوشنبه", 46, 10),
            ("س", "سه‌شنبه", 66, 26),
            ("چ", "چهارشنبه", 52, 16),
            ("پ", "پنج‌شنبه", 72, 32),
            ("ج", "جمعه", 62, 20)
        ];
        const int previousWeekMinutes = 442;
        var maximumMinutes = samples.Max(day => day.Music + day.Video);
        Days = samples.Select((day, index) => new WeeklyRhythmDayItemViewModel(
            index, day.ShortName, day.DayName, day.Music, day.Video, maximumMinutes)).ToArray();
        var musicMinutes = samples.Sum(day => day.Music);
        var videoMinutes = samples.Sum(day => day.Video);
        var totalMinutes = musicMinutes + videoMinutes;
        TotalDurationText = WeeklyRhythmDayItemViewModel.FormatDuration(totalMinutes, padHours: true);
        TotalAccessibleText = $"{totalMinutes / 60} ساعت و {totalMinutes % 60} دقیقه موسیقی و موزیک‌ویدیو در هفت روز";
        MusicDurationText = WeeklyRhythmDayItemViewModel.FormatDuration(musicMinutes);
        VideoDurationText = WeeklyRhythmDayItemViewModel.FormatDuration(videoMinutes);
        ComparisonText = "+" + ((totalMinutes - previousWeekMinutes) * 100d / previousWeekMinutes)
            .ToString("0", CultureInfo.InvariantCulture) + "٪";
        BusiestDayText = Days.First(day => day.IsPeak).DayName + "؛ پرشنیدن‌ترین روز";
    }
}
