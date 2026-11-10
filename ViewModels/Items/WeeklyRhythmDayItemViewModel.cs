using System.Globalization;

namespace Mizon.Windows.ViewModels.Items;

public sealed class WeeklyRhythmDayItemViewModel
{
    public int Index { get; }
    public string ShortName { get; }
    public string DayName { get; }
    public int MusicMinutes { get; }
    public int VideoMinutes { get; }
    public int TotalMinutes => MusicMinutes + VideoMinutes;
    public bool IsPeak { get; }
    public double MusicBarHeight { get; }
    public double VideoBarHeight { get; }
    public string TotalDurationText { get; }
    public string MusicDurationText { get; }
    public string VideoDurationText { get; }
    public string TooltipText { get; }

    internal WeeklyRhythmDayItemViewModel(int index, string shortName, string dayName,
        int musicMinutes, int videoMinutes, int maximumMinutes)
    {
        Index = index;
        ShortName = shortName;
        DayName = dayName;
        MusicMinutes = musicMinutes;
        VideoMinutes = videoMinutes;
        IsPeak = TotalMinutes == maximumMinutes && maximumMinutes > 0;
        MusicBarHeight = maximumMinutes > 0 ? musicMinutes * 84d / maximumMinutes : 0;
        VideoBarHeight = maximumMinutes > 0 ? videoMinutes * 84d / maximumMinutes : 0;
        TotalDurationText = FormatDuration(TotalMinutes);
        MusicDurationText = FormatDuration(musicMinutes);
        VideoDurationText = FormatDuration(videoMinutes);
        TooltipText = $"{dayName}؛ مجموع {TotalDurationText}، موسیقی {MusicDurationText}، موزیک‌ویدیو {VideoDurationText}";
    }

    internal static string FormatDuration(int minutes, bool padHours = false) =>
        (minutes / 60).ToString(padHours ? "00" : "0", CultureInfo.InvariantCulture)
        + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
}
