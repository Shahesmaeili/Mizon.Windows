using Mizon.Windows.Services;
using FluentSymbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Xaml.Media;
using System;
using System.Globalization;
using Windows.UI;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicGenreTypeItemViewModel : ViewModelBase
{
    public LocalizationService Localization => LocalizationService.Current;

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private string _accentColorHex = "#EC4899";

    public Guid PublicId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? PersianTitle { get; init; }
    public string IconSymbolName { get; init; } = nameof(FluentSymbol.MusicNote1);
    public int MusicsCount { get; init; }

    public string AccentColorHex
    {
        get => _accentColorHex;
        init
        {
            _accentColorHex = value;
            var color = ParseColor(value);
            AccentBrush = new SolidColorBrush(color);
            AccentColor = color;
            TransparentAccentColor = Color.FromArgb(0, color.R, color.G, color.B);
        }
    }

    public SolidColorBrush AccentBrush { get; private set; } = new(ParseColor("#EC4899"));
    public Color AccentColor { get; private set; } = ParseColor("#EC4899");
    public Color TransparentAccentColor { get; private set; } = Color.FromArgb(0, 236, 72, 153);
    public string MusicsCountText => MusicsCount.ToString("N0", CultureInfo.GetCultureInfo(Localization.LanguageTag));
    public string DisplayTitle => Localization.IsPersian && !string.IsNullOrWhiteSpace(PersianTitle) ? PersianTitle : Title;
    public string AutomationName => Localization.Format("MusicHome/GenreName", DisplayTitle, MusicsCount);

    public FluentSymbol IconSymbol => Enum.TryParse<FluentSymbol>(IconSymbolName, true, out var symbol)
        ? symbol
        : FluentSymbol.MusicNote1;

    private static Color ParseColor(string? color)
    {
        if (color is not { Length: 7 } || color[0] != '#')
            return Color.FromArgb(255, 236, 72, 153);

        if (!uint.TryParse(color[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return Color.FromArgb(255, 236, 72, 153);

        return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
