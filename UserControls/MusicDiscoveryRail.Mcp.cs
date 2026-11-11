using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;

namespace Mizon.Windows.UserControls;

public sealed partial class MusicDiscoveryRail
{
    internal void McpDismissProfile() => CloseProfile();
    internal bool McpShowProfile(DiscoveryMusicItemViewModel music, int contributorIndex)
    {
        if (!IsLoaded || contributorIndex < 0 || contributorIndex >= music.Contributors.Count) return false;
        var person = music.Contributors[contributorIndex];
        ResetInteraction();
        if (_profilePopup is null)
        {
            _profilePopup = new ProfilePreviewPopup();
            _profilePopup.ActionRequested += Profile_ActionRequested;
            _profilePopup.Closed += Profile_Closed;
        }
        _profileMusic = music;
        _profilePopup.ShowAt(this, new ProfilePreviewViewModel
        {
            PublicId = person.PersonPublicId, Kind = ProfilePreviewKind.Person, DisplayName = person.DisplayName,
            SubtitleResourceKey = "MusicSurface/Artist", Avatar = person.ProfileImage,
            Banner = music.CoverImage, DetailResourceKey = "MusicHome/InTrack", DetailArgument = music.Title, PersianDetailArgument = music.PersianTitle,
            FooterResourceKey = music.PublicId == System.Guid.Empty ? "MusicSurface/Demo" : null,
            PrimaryActionResourceKey = music.PublicId == System.Guid.Empty ? null : "MusicSurface/OpenMusic",
            PrimaryActionSymbol = FluentIcons.Common.Symbol.MusicNote1
        });
        return true;
    }
}
