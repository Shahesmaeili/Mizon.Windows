using Mizon.Windows.Services;
using System.Globalization;

namespace Mizon.Windows.ViewModels.Items;

public sealed partial class WeeklyRhythmDayItemViewModel : ViewModelBase
{
    public LocalizationService Localization => LocalizationService.Current;
    public int Index { get; }
    public string ShortName => Localization.GetString($"MusicWidgets/Day{Index}Short");
    public string DayName => Localization.GetString($"MusicWidgets/Day{Index}");
    public int MusicMinutes { get; }
    public int VideoMinutes { get; }
    public int TotalMinutes => MusicMinutes + VideoMinutes;
    public bool IsPeak { get; }
    public double MusicBarHeight { get; }
    public double VideoBarHeight { get; }
    public string TotalDurationText { get; }
    public string MusicDurationText { get; }
    public string VideoDurationText { get; }
    public string TooltipText => Localization.Format("MusicWidgets/RhythmDayTooltip", DayName, TotalDurationText, MusicDurationText, VideoDurationText);

    internal WeeklyRhythmDayItemViewModel(int index, int musicMinutes, int videoMinutes, int maximumMinutes)
    {
        Index = index;
        MusicMinutes = musicMinutes;
        VideoMinutes = videoMinutes;
        IsPeak = TotalMinutes == maximumMinutes && maximumMinutes > 0;
        MusicBarHeight = maximumMinutes > 0 ? musicMinutes * 84d / maximumMinutes : 0;
        VideoBarHeight = maximumMinutes > 0 ? videoMinutes * 84d / maximumMinutes : 0;
        TotalDurationText = FormatDuration(TotalMinutes);
        MusicDurationText = FormatDuration(musicMinutes);
        VideoDurationText = FormatDuration(videoMinutes);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ShortName));
        OnPropertyChanged(nameof(DayName));
        OnPropertyChanged(nameof(TooltipText));
    }

    internal static string FormatDuration(int minutes, bool padHours = false) =>
        (minutes / 60).ToString(padHours ? "00" : "0", CultureInfo.InvariantCulture)
        + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
}
