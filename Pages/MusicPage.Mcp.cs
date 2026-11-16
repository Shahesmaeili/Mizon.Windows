using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Common;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using Mizon.Windows.UserControls;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace Mizon.Windows.Pages;

public sealed partial class MusicPage : IMcpMusicHomePage
{
    private CancellationTokenSource? _mcpHomeLifetime;
    private ContentDialog? _mcpPreviewDialog;
    private bool _mcpPlaybackHandoff;
    private MusicPlaylistPlaybackService? _mcpPlaylistPlayer;
    bool IMcpMusicHomePage.IsPlaybackHandoff => _mcpPlaybackHandoff || _mcpPlaylistPlayer?.IsNavigatingToPlayback == true;
    private StoryViewer? ActiveMcpStory => _storyViewer ?? _followingStoryPresenter?.ActiveViewer;
    bool IMcpMusicHomePage.IsActive => IsLoaded && _homeWidgetsActive
        && ReferenceEquals(ViewModel.NavigationService.CurrentInternalPage, this);
    CancellationToken IMcpMusicHomePage.Lifetime => _mcpHomeLifetime?.Token ?? new(true);

    private void ActivateMcpHome()
    {
        _mcpHomeLifetime?.Dispose();
        _mcpHomeLifetime = new();
        _mcpPlaybackHandoff = false;
        _mcpPlaylistPlayer = null;
    }

    private void DeactivateMcpHome()
    {
        _mcpPreviewDialog?.Hide();
        _mcpPreviewDialog = null;
        _mcpPlaybackHandoff |= _mcpPlaylistPlayer?.IsNavigatingToPlayback == true;
        _mcpHomeLifetime?.Cancel();
        _mcpPlaylistPlayer = null;
        _mcpHomeLifetime?.Dispose();
        _mcpHomeLifetime = null;
    }

    McpMusicHomeState IMcpMusicHomePage.GetState()
    {
        var sections = new List<McpMusicHomeSectionState>(12);
        Add("featured", Features?.IsLoading == true, Features?.HasError == true,
            Features?.StatusText, Features?.Items.Select(item => new McpMusicHomeItemState
            {
                Id = item.PublicId.ToString("D"), Kind = item.ContentTypeCode, Title = item.DisplayTitle,
                Subtitle = item.ArtistText, TargetId = item.MusicPublicId.ToString("D"), TargetKind = "music",
                ImageAvailable = item.PreviewImage is not null, IsAvailable = true, CanOpen = true,
                Count = item.LikesCount, DurationSeconds = item.DurationSeconds,
                Actions = ["open", "play", "copy_link", "like", "request_download"]
            }) ?? [], "api", actions: ["previous", "next", "select"]);
        var selectedArtist = FeaturedArtists.Spotlights.ElementAtOrDefault(FeaturedArtist.McpSelectedIndex);
        Add("spotlight", FeaturedArtists.IsLoading, FeaturedArtists.HasError, FeaturedArtists.StatusText,
            FeaturedArtists.Spotlights.Select(item => ArtistItem(item, ReferenceEquals(item, selectedArtist))), "api",
            actions: ["previous", "next", "select", "reset_model", "open", "play", "profile", "follow"]);
        Add("stories", ViewModel.IsHomeStoriesLoading, ViewModel.HomeStoriesRetryVisibility == Visibility.Visible,
            ViewModel.HomeStoriesMessage, ViewModel.HomeStories.Select(item => new McpMusicHomeItemState
            {
                Id = item.PublicId.ToString("D"), Kind = "story", Title = item.Title ?? string.Empty,
                Subtitle = item.CreatorDisplayName, ImageAvailable = item.PreviewImage is not null,
                IsSeen = item.ViewedByMe, IsAvailable = true, CanOpen = true, Actions = ["open"]
            }), "api", actions: ["add_story"]);
        Add("genres", ViewModel.IsHomeGenresLoading, ViewModel.HomeGenresRetryVisibility == Visibility.Visible,
            ViewModel.HomeGenresMessage, ViewModel.MusicGenreTypes.Select(item => new McpMusicHomeItemState
            {
                Id = item.PublicId.ToString("D"), Kind = "genre",
                Title = LocalizationService.Current.IsPersian ? item.PersianTitle ?? item.Title : item.Title,
                Count = item.MusicsCount, IsAvailable = true, CanOpen = false
            }), "api");
        Add("artists", FeaturedArtists.IsLoading, FeaturedArtists.HasError, FeaturedArtists.StatusText,
            FeaturedArtists.Spotlights.Select(item => ArtistItem(item, false)), "api", actions: ["previous", "next", "select"]);
        AddMusics("recommended", Discovery.RecommendedMusics);
        AddMusics("recent", Discovery.RecentMusics);
        AddMusics("trending", Discovery.TrendingMusics);
        Add("library", Library.IsLoading, Library.HasError, Library.StatusText,
            Library.VisiblePlaylists.Select(item => new McpMusicHomeItemState
            {
                Id = item.PublicId.ToString("D"), Kind = "playlist", Title = item.Title,
                Count = item.MusicsCount, Subtitle = item.UpdatedText, ImageAvailable = item.CoverImage is not null,
                IsAvailable = true, CanOpen = true,
                Actions = item.CanPlay ? ["open", "play", "copy_link"] : ["open", "copy_link"]
            }), "api", Library.NextCursor is not null, ["open_all", "show_recent"]);
        Add("weekly", false, false, Discovery.WeeklyRhythm.TotalAccessibleText,
            Discovery.WeeklyRhythm.Days.Select(day => new McpMusicHomeItemState
            {
                Id = "weekly:" + day.Index.ToString(CultureInfo.InvariantCulture), Kind = "day",
                Title = day.DayName, Subtitle = day.TooltipText, DurationSeconds = day.TotalMinutes * 60,
                IsDemo = true, IsAvailable = true, IsSelected = WeeklyRhythm.McpSelectedIndex == day.Index,
                Actions = ["select"]
            }), "demo", actions: ["select"]);
        Add("news", false, false, null, LatestNews.McpItems, "demo", actions: ["previous", "next", "select", "pause", "resume"]);
        Add("following", FollowingActivities?.IsLoading == true, FollowingActivities?.HasError == true,
            FollowingActivities is { IsEmpty: true } activity ? activity.EmptyDescription : FollowingActivities?.HasError == true ? FollowingActivities.StatusText : null,
            FollowingActivities?.Items.Select(item => new McpMusicHomeItemState
            {
                Id = item.Id, Kind = "activity", Title = item.ContentTitle,
                Subtitle = item.ActivityText, Description = item.Subtitle,
                ActorId = item.ActorPublicId.ToString("D"), ActivityKind = item.ActivityKind,
                TargetKind = item.TargetKind.ToString(), TargetId = (item.StoryPublicId ?? item.MusicCommentPublicId ?? item.MusicPlaylistPublicId ?? item.MusicPublicId)?.ToString("D"),
                IsSeen = item.StoryViewedByMe, ImageAvailable = item.CoverImage is not null,
                IsAvailable = true, CanOpen = item.CanOpen, Actions = item.CanOpen ? ["open", "profile"] : ["profile"]
            }) ?? [], "api", FollowingActivities?.HasMore == true, ["open_all"]);
        return new()
        {
            IsAvailable = true, HasSession = true, Language = LocalizationService.Current.LanguageTag,
            Layout = _layoutState ?? "unavailable", IsBusy = sections.Any(section => section.IsLoading),
            Sections = sections.ToArray(), Hero = FeatureHero.GetMcpState(),
            Story = ActiveMcpStory?.GetMcpState(), ActiveStoryId = ActiveMcpStory?.GetMcpState().Id,
            ActiveArtistId = selectedArtist?.Artist.PublicId.ToString("D"),
            ActiveNewsIndex = LatestNews.McpSelectedIndex, SelectedDayIndex = WeeklyRhythm.McpSelectedIndex,
            ActiveDialog = _mcpPreviewDialog is not null ? "preview" : LatestNews.McpDialogOpen ? "news" : ActiveMcpStory?.McpDialogOpen == true ? "story_profile" : null,
            Actions = ["set_language", "dismiss"]
        };

        void Add(string id, bool loading, bool error, string? message, IEnumerable<McpMusicHomeItemState> items,
            string source, bool more = false, string[]? actions = null)
        {
            var bounded = items.Take(McpMusicHomeContract.MaxItemsPerSection + 1).ToArray();
            sections.Add(new()
            {
                Id = id, Title = HomeSectionTitle(id), Source = source, IsLoading = loading, HasError = error,
                ErrorCode = error ? "load_failed" : null, Message = message,
                HasMore = more || bounded.Length > McpMusicHomeContract.MaxItemsPerSection,
                Items = bounded.Take(McpMusicHomeContract.MaxItemsPerSection).ToArray(), Actions = actions ?? []
            });
        }
        void AddMusics(string section, IEnumerable<DiscoveryMusicItemViewModel> items) =>
            Add(section, false, false, null, items.Select((item, index) => MusicItem(item, section, index)), "demo");
    }

    private static string HomeSectionTitle(string section) => LocalizationService.Current.GetString("MusicSurface/Section_" + section);
    private static string CardId(Guid id, string section, int index) => id != Guid.Empty ? id.ToString("D") : section + ":" + index.ToString(CultureInfo.InvariantCulture);
    private static McpMusicHomeItemState MusicItem(DiscoveryMusicItemViewModel item, string section, int index) => new()
    {
        Id = CardId(item.PublicId, section, index), Kind = "music", Title = item.DisplayTitle, Subtitle = item.Artist,
        ImageAvailable = item.CoverImage is not null, IsDemo = item.PublicId == Guid.Empty,
        IsAvailable = true, CanOpen = true, Count = item.LikesCount, DurationSeconds = item.Duration.TotalSeconds,
        Actions = item.PublicId == Guid.Empty ? ["open", "profile"] : ["open", "play", "profile"]
    };
    private static McpMusicHomeItemState ArtistItem(ArtistSpotlightItemViewModel item, bool selected)
    {
        var artist = item.Artist;
        var id = artist.PublicId.ToString("D");
        return new()
        {
            Id = id, Kind = "artist", Title = artist.DisplayName, Subtitle = artist.DisplayGenre,
            Count = artist.FollowersCount, TargetKind = "person", TargetId = id, IsAvailable = true,
            CanOpen = true, ImageAvailable = artist.ProfileMediaId is not null || artist.CoverMediaId is not null,
            IsSelected = selected, Actions = artist.CanFollow ? ["open", "profile", "follow"] : ["open", "profile"],
            Artist = new()
            {
                CanFollow = artist.CanFollow, IsFollowing = artist.IsFollowed, HasModel = item.ModelMediaId is not null,
                MusicCount = item.SongCount, MonthlyListenersCount = artist.MonthlyListenersCount, DailyPlays = item.DailyPlays,
                Tracks = item.LatestMusics.Select(track => new McpMusicHomeArtistTrack(track.PublicId.ToString("D"),
                    track.DisplayTitle, track.HasKnownDuration ? track.Duration.TotalSeconds : null)).ToArray()
            }
        };
    }

    async Task IMcpMusicHomePage.RefreshAsync(string section, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // These are the same bounded requests used by the page and each Retry button.
        var tasks = new List<Task>(4);
        if (section is "all" or "featured" && Features is not null) tasks.Add(Features.RefreshAsync(token));
        if (section is "all" or "library") tasks.Add(Library.RefreshAsync(token));
        if (section is "all" or "following" && FollowingActivities is not null) tasks.Add(FollowingActivities.RefreshAsync(token));
        if (section is "all" or "stories" or "genres") tasks.Add(ViewModel.RefreshHomeWidgetsAsync(token));
        if (section is "all" or "artists" or "spotlight") tasks.Add(FeaturedArtists.RefreshAsync(token));
        await Task.WhenAll(tasks);
        token.ThrowIfCancellationRequested();
    }

    async Task<McpResponse> IMcpMusicHomePage.ExecuteActionAsync(McpMusicHomeRequest input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (input.Action == "set_language")
        {
            LocalizationService.Current.SetLanguage(input.Language!);
            return Done("language_updated");
        }
        if (input.Action == "dismiss")
        {
            if (ActiveMcpStory is { } viewer) await viewer.CloseAsync();
            if (input.Section == "all") { _mcpPreviewDialog?.Hide(); LatestNews.McpDismiss(); RecommendedRail.McpDismissProfile(); RecentRail.McpDismissProfile(); }
            return Done("dismissed");
        }
        if (input.Action is "open" or "profile" or "add_story" && (_dialogOpen || LatestNews.McpDialogOpen || ActiveMcpStory?.McpDialogOpen == true))
            return new() { Code = "busy", Message = "Dismiss the current preview before opening another one." };
        if (input.Section == "featured")
        {
            if (input.Id is { } id && FeatureHero.GetMcpState().Id != id)
            {
                var index = Features?.Items.ToList().FindIndex(item => item.PublicId.ToString("D") == id) ?? -1;
                if (index < 0) return Missing();
                FeatureHero.McpSelect(index);
            }
            if (input.Action is "open" or "play")
            {
                if (FeatureHero.McpSelectedItem is not { } item) return Missing();
                // Pause the featured video before the shared audio pipeline takes ownership.
                FeatureHero.McpPause();
                _mcpPlaybackHandoff = input.Action == "play";
                MainFeature_OpenMusicRequested(this, item);
                if (input.Action == "play")
                    return await ViewModel.ActivateMusicAsync(item.MusicPublicId, MusicActivationAction.Play, token)
                        ? Done("playing") : Unavailable("Music playback could not start.");
                return Done("opened");
            }
            return await FeatureHero.ExecuteMcpAsync(input, token);
        }
        if (input.Section == "stories") return await ExecuteStoryMcpAsync(input, token);
        if (input.Section is "artists" or "spotlight") return await ExecuteArtistMcpAsync(input, token);
        if (input.Section == "weekly") return WeeklyRhythm.McpSelect(input.Index!.Value) ? Done("selected") : Missing();
        if (input.Section == "news") return LatestNews.ExecuteMcp(input);
        if (input.Section == "library")
        {
            if (input.Action == "open_all") { ViewModel.NavigationService.OpenMusicPlaylists(); return Done("opened"); }
            if (input.Action == "show_recent") { ScrollTo(RecentSection); return Done("revealed"); }
            var item = Library.VisiblePlaylists.FirstOrDefault(item => item.PublicId.ToString("D") == input.Id);
            if (item is null) return Missing();
            if (input.Action == "open") { ViewModel.NavigationService.OpenMusicPlaylist(item.PublicId); return Done("opened"); }
            if (input.Action == "copy_link") return CopyMcpLink(MusicPlaylistMenu.Link(item.PublicId));
            if (!item.CanPlay) return Unavailable("This playlist has no playable tracks.");
            var player = _mcpPlaylistPlayer = ((App)Application.Current).Services.GetRequiredService<MusicPlaylistPlaybackService>();
            try { return await player.StartAsync(item.PublicId, token) ? Done("playing") : Unavailable("Playlist playback could not start."); }
            finally { _mcpPlaylistPlayer = null; }
        }
        if (input.Section == "following")
        {
            if (input.Action == "open_all") { ViewModel.NavigationService.OpenFollowingActivities(); return Done("opened"); }
            var item = FollowingActivities?.Items.FirstOrDefault(item => item.Id == input.Id);
            if (item is null) return Missing();
            if (input.Action == "profile")
            {
                ViewModel.NavigationService.InternalNavigate(typeof(ClientProfilePage), new() { [PageConstants.CLIENT_ID] = item.ActorPublicId });
                return Done("opened");
            }
            if (!item.CanOpen) return Unavailable("This activity has no supported destination.");
            if (item.TargetKind == FollowingActivityTargetKind.Story) CloseStory();
            return await FollowingActivityNavigation.TryOpenAsync(item, ViewModel.NavigationService,
                () => _followingStoryPresenter ??= new(), cancellationToken: token)
                ? Done("opened") : Unavailable("The activity destination could not be opened.");
        }
        var musics = input.Section switch
        {
            "recommended" => Discovery.RecommendedMusics,
            "recent" => Discovery.RecentMusics,
            "trending" => Discovery.TrendingMusics,
            _ => null
        };
        var music = musics?.Select((item, index) => (Item: item, Id: CardId(item.PublicId, input.Section!, index)))
            .FirstOrDefault(pair => pair.Id == input.Id).Item;
        if (music is null) return Missing();
        if (input.Action == "profile")
        {
            var rail = input.Section == "recent" ? RecentRail : RecommendedRail;
            return rail.McpShowProfile(music, input.Index ?? 0) ? Done("opened") : Unavailable("This contributor is not available.");
        }
        if (input.Action == "play")
        {
            if (music.PublicId == Guid.Empty) return Unavailable("This is demo artwork; no playable track is attached.");
            _mcpPlaybackHandoff = true;
            await OpenMusicAsync(music);
            return await ViewModel.ActivateMusicAsync(music.PublicId, MusicActivationAction.Play, token)
                ? Done("playing") : Unavailable("Music playback could not start.");
        }
        // Demo cards intentionally show the same preview dialog as a UI click.
        if (music.PublicId == Guid.Empty) { _ = OpenMusicAsync(music); return PreviewResult(); }
        await OpenMusicAsync(music);
        return Done("opened");
    }

    private async Task<McpResponse> ExecuteArtistMcpAsync(McpMusicHomeRequest input, CancellationToken token)
    {
        var items = FeaturedArtists.Spotlights;
        if (items.Count == 0) return Missing();
        var current = items.ElementAtOrDefault(FeaturedArtist.McpSelectedIndex);
        if (input.Action == "reset_model")
        {
            if (current?.ModelMediaId is null) return Unavailable("This artist has no 3D model.");
            FeaturedArtist.McpResetModel();
            return Done("reset");
        }
        if (input.Action is "previous" or "next")
        {
            var direction = input.Action == "next" ? 1 : -1;
            if (input.Section == "spotlight") FeaturedArtist.McpMove(direction); else ScrollArtists(direction);
            return Done("selected");
        }
        if (input.Action == "select")
        {
            var index = input.Index!.Value;
            if (input.Section == "spotlight") return FeaturedArtist.McpSelect(index) ? Done("selected") : Missing();
            if (index >= items.Count) return Missing();
            TopArtistsScroll.ChangeView(Math.Clamp(index * (_artistCardWidth + 12), 0, TopArtistsScroll.ScrollableWidth), null, null, !_uiSettings.AnimationsEnabled);
            return Done("selected");
        }
        var idValid = Guid.TryParse(input.Id, out var id);
        if (input is { Section: "spotlight", Action: "open" or "play" } && idValid
            && current?.LatestMusics.FirstOrDefault(item => item.PublicId == id) is { } track)
        {
            _mcpPlaybackHandoff = input.Action == "play";
            await OpenMusicAsync(track);
            return input.Action == "open" ? Done("opened")
                : await ViewModel.ActivateMusicAsync(track.PublicId, MusicActivationAction.Play, token)
                    ? Done("playing") : Unavailable("Music playback could not start.");
        }
        if (input.Action == "play") return Unavailable("Choose one of the selected artist's latest tracks.");
        var target = idValid
            ? items.FirstOrDefault(item => item.Artist.PublicId == id)
            : input.Id is null && input.Section == "spotlight" ? current : null;
        if (target is null) return Missing();
        if (input.Action == "follow")
        {
            var updated = await FeaturedArtists.SetFollowingAsync(target.Artist, input.Enabled!.Value, token);
            token.ThrowIfCancellationRequested();
            return updated ? Done("follow_updated") : Unavailable("The artist follow status could not be updated.");
        }
        TopArtist_ProfileRequested(this, target.Artist);
        return Done("opened");
    }

    private async Task<McpResponse> ExecuteStoryMcpAsync(McpMusicHomeRequest input, CancellationToken token)
    {
        if (input.Action == "add_story") { Stories_AddRequested(this, EventArgs.Empty); return PreviewResult(); }
        if (input.Action == "open")
        {
            var stories = ViewModel.HomeStories.ToList();
            var index = stories.FindIndex(item => item.PublicId.ToString("D") == input.Id);
            if (index < 0 || App.MainMizonWindow is not MainWindow window) return Missing();
            CloseStory();
            _followingStoryPresenter?.Dispose();
            _followingStoryPresenter = null;
            var viewer = _storyViewer = new StoryViewer();
            _storyWindow = window;
            viewer.CloseRequested += Story_Closed;
            window.ShowWindowOverlay(viewer);
            try { await viewer.OpenWithTransitionAsync(ViewModel, stories, index, null, token); }
            catch { if (ReferenceEquals(_storyViewer, viewer)) CloseStory(); throw; }
            return viewer.GetMcpState().HasError ? Unavailable("The story could not be loaded.") : Done("opened");
        }
        if (ActiveMcpStory is not { } active) return Unavailable("Open a story before controlling it.");
        if (input.Action != "open_widget" && input.Id is { } storyId && active.GetMcpState().Id != storyId) return Missing();
        return await active.ExecuteMcpAsync(input, token);
    }

    private McpResponse PreviewResult() => _mcpPreviewDialog is not null ? Done("preview_opened") : Unavailable("The preview could not be opened.");

    private static McpResponse CopyMcpLink(string link)
    {
        var data = new DataPackage(); data.SetText(link); Clipboard.SetContent(data);
        return Done("copied");
    }
    internal static McpResponse Done(string code) => new() { Success = true, Code = code, Message = "The music home action completed." };
    internal static McpResponse Missing() => new() { Code = "item_not_found", Message = "The item is no longer in this home section. Refresh its state." };
    internal static McpResponse Unavailable(string message) => new() { Code = "action_unavailable", Message = message };
}
