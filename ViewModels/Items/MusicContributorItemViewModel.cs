using Mizon.Windows.Services;
using System;
using Microsoft.UI.Xaml.Media;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicContributorItemViewModel : ViewModelBase
{
    public Guid PersonPublicId { get; init; }
    public Guid? ProfileMediaPublicId { get; init; }
    public ImageSource? Portrait
    {
        get => field;
        set => SetField(ref field, value);
    }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => string.IsNullOrWhiteSpace(FirstName) ? LastName : $"{FirstName} {LastName}";
    public string Initials => $"{(FirstName.Length > 0 ? FirstName[0] : ' ')}{(LastName.Length > 0 ? LastName[0] : ' ')}".Trim().ToUpperInvariant();
    public Guid MusicContributorTypePublicId { get; set; }
    public string MusicContributorTypeTitle { get; set; } = string.Empty;
    public string? MusicContributorTypePersianTitle { get; set; }
    public LocalizationService Localization => LocalizationService.Current;
    public string ContributorTypeDisplayName(int revision)
    {
        if (Localization.IsPersian && !string.IsNullOrWhiteSpace(MusicContributorTypePersianTitle))
            return MusicContributorTypePersianTitle;

        return MusicContributorTypeTitle switch
        {
            "LeadVocal" => Localization.GetString("MusicDetails/RoleLeadVocal"),
            "FeaturedVocal" => Localization.GetString("MusicDetails/RoleFeaturedVocal"),
            "MixingEngineer" => Localization.GetString("MusicDetails/RoleMixingEngineer"),
            "MasteringEngineer" => Localization.GetString("MusicDetails/RoleMasteringEngineer"),
            "RecordingEngineer" => Localization.GetString("MusicDetails/RoleRecordingEngineer"),
            _ => MusicContributorTypeTitle
        };
    }

    public FluentIcons.Common.Symbol ContributorSymbol => MusicContributorTypeTitle switch
    {
        "LeadVocal" or "FeaturedVocal" => FluentIcons.Common.Symbol.PersonVoice,
        "Composer" or "Arranger" or "Remixer" => FluentIcons.Common.Symbol.MusicNote1,
        "Lyricist" => FluentIcons.Common.Symbol.Edit,
        "Producer" or "MixingEngineer" or "MasteringEngineer" or "RecordingEngineer" => FluentIcons.Common.Symbol.HeadphonesSoundWave,
        "Instrumentalist" => FluentIcons.Common.Symbol.MusicNote2,
        "Guitarist" => FluentIcons.Common.Symbol.Guitar,
        "Director" => FluentIcons.Common.Symbol.Video,
        "Artwork" => FluentIcons.Common.Symbol.Camera,
        "Publisher" => FluentIcons.Common.Symbol.Building,
        _ => FluentIcons.Common.Symbol.Record
    };
}
