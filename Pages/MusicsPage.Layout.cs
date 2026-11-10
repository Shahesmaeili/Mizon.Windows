using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage
{
    private bool? _detailWide;

    private void ApplyDetailLayout(double width)
    {
        if (TopGrid is null || !double.IsFinite(width) || width <= 0) return;
        width = Math.Min(width, MainGrid.MaxWidth);
        var wide = width > 1000;
        var inset = width < 680 ? 16 : 32;
        var contentWidth = Math.Max(1, width - 64 - inset * 2);
        var artwork = Math.Min(280, contentWidth);
        TopGrid.Margin = new Thickness(inset, 32, inset, 0);
        MusicBorder.Width = MusicBorder.Height = artwork;
        // A vertical slider inside the page's ScrollViewer must not determine the row height.
        VolumeSlider.Height = 280;
        VolumeSlider.Width = 32;
        var informationWidth = Math.Max(1, contentWidth - 48 - (wide ? artwork + 32 : 0));

        if (_detailWide != wide)
        {
            _detailWide = wide;
            ArtworkColumn.Width = wide ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            InformationColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            VolumeColumn.Width = new GridLength(48);
            Grid.SetRow(MusicBorder, 0);
            Grid.SetColumn(MusicBorder, 0);
            Grid.SetColumnSpan(MusicBorder, wide ? 1 : 3);
            MusicBorder.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(ShadowCastGrid, 0);
            Grid.SetColumn(ShadowCastGrid, 0);
            Grid.SetColumnSpan(ShadowCastGrid, wide ? 1 : 3);
            Grid.SetRow(MusicInforationGrid, wide ? 0 : 1);
            Grid.SetColumn(MusicInforationGrid, wide ? 1 : 0);
            Grid.SetColumnSpan(MusicInforationGrid, wide ? 1 : 2);
            MusicInforationGrid.Margin = new Thickness(wide ? 32 : 0, wide ? 16 : 24, 0, 0);
            Grid.SetRow(VolumeSlider, wide ? 0 : 1);
            Grid.SetColumn(VolumeSlider, 2);
            VolumeSlider.HorizontalAlignment = HorizontalAlignment.Right;
            VolumeSlider.Margin = new Thickness(16, wide ? 0 : 24, 0, 0);
        }

        // Keep the original positions when they fit; wrap only the groups that would overlap.
        var sharedHeading = informationWidth >= 1000;
        HeaderDock.ColumnDefinitions[1].Width = sharedHeading ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(UtilityActions, sharedHeading ? 0 : 1);
        Grid.SetColumn(UtilityActions, sharedHeading ? 1 : 0);
        Grid.SetColumnSpan(UtilityActions, sharedHeading ? 1 : 2);
        UtilityActions.Margin = new Thickness(0, sharedHeading ? 0 : 8, 0, sharedHeading ? 0 : 8);
        UtilityActions.VerticalAlignment = VerticalAlignment.Center;
        var titleWidth = Math.Max(1, informationWidth - 76 - (sharedHeading ? 260 : 0));
        TrackTitleText.MaxWidth = titleWidth;
        TrackPersianTitleText.MaxWidth = TrackAlbumText.MaxWidth = Math.Max(1, titleWidth - 8);
        GenreRail.MaxWidth = LabelLink.MaxWidth = titleWidth;
        ViewsStatusButton.MaxWidth = Math.Max(1, titleWidth - 16);

        var sharedSocial = informationWidth >= 640;
        SocialDock.ColumnDefinitions[1].Width = sharedSocial ? GridLength.Auto : new GridLength(0);
        SocialDock.ColumnSpacing = sharedSocial ? 16 : 0;
        Grid.SetRow(ReactionRail, sharedSocial ? 0 : 1);
        Grid.SetColumn(ReactionRail, sharedSocial ? 1 : 0);
        Grid.SetColumnSpan(ReactionRail, sharedSocial ? 1 : 2);
        ReactionRail.HorizontalAlignment = HorizontalAlignment.Right;
        ReactionRail.MaxWidth = sharedSocial ? Math.Min(300, informationWidth * .30) : informationWidth;

        var sharedLyrics = width >= 850;
        LyricsHeader.ColumnDefinitions[2].Width = sharedLyrics ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(MixerControls, sharedLyrics ? 0 : 1);
        Grid.SetColumn(MixerControls, sharedLyrics ? 2 : 0);
        Grid.SetColumnSpan(MixerControls, sharedLyrics ? 1 : 3);
        MixerControls.Margin = new Thickness(0, sharedLyrics ? 0 : 8, 0, 0);
        CurrentLyricText.MaxWidth = Math.Clamp(width - (sharedLyrics ? 484 : 224), 1, 420);

        var available = Math.Max(1, width - 64);
        foreach (var child in InsightsRail.Children)
        {
            if (child is not FrameworkElement card) continue;
            var minimum = Math.Min(300, available);
            if (card.MinWidth > minimum) card.MinWidth = minimum;
            card.MaxWidth = available;
            card.MinWidth = minimum;
        }
    }
}
