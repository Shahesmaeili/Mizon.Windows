using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static async Task MusicDetailsScopesAndLifetime()
    {
        WaveformTimestampRoundTrip();
        var path = Path.Combine(StorageRoot, "music-details", "grants.dat");
        var proof = Proof();
        var open = new McpRequest { Operation = "set_music_details", MusicDetails = new() { CoverOpen = true } };
        var repeat = new McpRequest { Operation = "set_music_details", MusicDetails = new() { RepeatEnabled = true } };
        var live = new McpRequest { Operation = "set_music_details", MusicDetails = new() { Audience = "live" } };
        await using (var host = await Host.StartAsync(path))
        {
            await using var peer = await host.ConnectAsync(proof);
            var id = peer.Hello.Connection!.Id;
            Assert((await peer.Call("get_music_details")).Code == "approval_required", "Track details need in-app consent");
            await host.Approve(id, true, true, true, musicRead: true, musicControl: true, personRead: true);
            Assert(!(await peer.Call("get_music_details")).Success && !(await peer.Call(open)).Success,
                "Authentication, music home and artist grants do not authorize track details");

            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await host.Approve(id, false, false, false, detailsRead: true);
            Assert((await peer.Call("get_music_details")).MusicDetails is { ContributorCount: 2, ContributorsWithPortrait: 1, CoverOpen: false },
                "Read returns bounded metadata and loaded portrait count");
            Assert((await peer.Call("get_music_details")).MusicDetails is { MixerAvailable: true, MixerEnabled: true,
                MixerActive: true, MixerLoading: false, MixerBalance: 100, WaveformCommentCount: 1 },
                "The existing track read grant exposes the active mix and confirmed comment count");
            await host.Ui(() => host.MusicDetailsPage.Loading = true);
            Assert((await peer.Call("get_music_details")).MusicDetails is
                { MetadataLoading: true, WaveformLoading: true, WaveformCommentsLoading: true, MetadataLoadFailed: false },
                "Approved readers can distinguish pending track, waveform and comment data");
            await host.Ui(() => { host.MusicDetailsPage.Loading = false; host.MusicDetailsPage.LoadFailed = true; });
            Assert((await peer.Call("get_music_details")).MusicDetails is
                { MetadataLoading: false, WaveformLoading: false, WaveformCommentsLoading: false, MetadataLoadFailed: true },
                "A failed metadata read settles the loading stages with a bounded status");
            await host.Ui(() => host.MusicDetailsPage.Language = "en-US");
            Assert((await peer.Call("get_music_details")).MusicDetails is
                { Language: "en-US", PlaybackDeviceCount: 2, PlaybackDeviceSelected: true,
                    PlaybackDeviceVolume: 0, PlaybackDeviceSilent: true, MusicVideoAvailable: true },
                "Track readback reflects app language and bounded render-device availability without endpoint identifiers");
            Assert(!(await peer.Call(repeat)).Success && !host.MusicDetailsPage.RepeatEnabled, "Read cannot set repeat");
            Assert(!(await peer.Call(live)).Success, "Track read consent cannot open an audience panel");

            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await host.Approve(id, false, false, false, detailsControl: true);
            Assert((await peer.Call(open)).Code == "cover_unavailable", "Missing cover yields an actionable failure");
            await host.Ui(() => host.MusicDetailsPage.HasCover = true);
            Assert((await peer.Call(open)) is { Success: true, MusicDetails: null } && host.MusicDetailsPage.CoverOpen,
                "Control opens the cover without leaking read state");
            Assert((await peer.Call(open with { MusicDetails = new() { CoverOpen = false } })).Success
                && !host.MusicDetailsPage.CoverOpen, "The desired false closes the preview");
            Assert((await peer.Call(repeat)).Success && host.MusicDetailsPage.RepeatEnabled, "Repeat uses the page's playback state");
            Assert((await peer.Call(live)) is { Success: true, MusicDetails: null } && host.MusicDetailsPage.Audience == "live",
                "Track control can open live listeners without revealing a read snapshot");
            Assert((await peer.Call(live with { MusicDetails = new() { Audience = "arbitrary" } })).Code == "invalid_arguments",
                "Audience control accepts only the declared views");
            Assert((await peer.Call(live with { MusicDetails = new() { Audience = "closed" } })).Success &&
                host.MusicDetailsPage.Audience is null, "Audience control closes the current popup");
            Assert((await peer.Call(repeat with { MusicDetails = new() { RepeatEnabled = false, CoverOpen = false } })).Code == "invalid_arguments"
                && host.MusicDetailsPage.RepeatEnabled, "An ambiguous command changes nothing");
            Assert((await peer.Call(open with { Page = "login" })).Code == "invalid_arguments", "Unrelated arguments are rejected");
            await host.Ui(() => host.MusicDetailsPage.Active = false);
            Assert((await peer.Call(open)).Code == "page_unavailable", "Control stops when the track page leaves");
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            Assert(!(await peer.Call(repeat)).Success && !(await peer.Call(live)).Success, "Revocation blocks further track actions");
            await host.Approve(id, false, false, false, detailsRead: true);
        }

        await using (var host = await Host.StartAsync(path))
        {
            await using var peer = await host.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { IsRemembered: true, AllowMusicDetailsRead: true, AllowMusicDetailsControl: false },
                "Track permissions survive restart independently");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var observedCancellation = false;
            try { host.MusicDetails.Execute(repeat, cancelled.Token); }
            catch (OperationCanceledException) { observedCancellation = true; }
            Assert(observedCancellation && !host.MusicDetailsPage.RepeatEnabled, "Cancelled requests do not mutate playback");
        }
    }

    private static void WaveformTimestampRoundTrip()
    {
        var duration = TimeSpan.FromSeconds(241.37);
        // The local coordinate includes the scroller offset before submission.
        foreach (var contentX in new[] { 0d, 15.4d, 520.75d, 1210.32d, MusicWaveformTimeline.Width })
        {
            var timestamp = MusicWaveformTimeline.GetTimestamp(contentX, duration);
            var center = MusicWaveformTimeline.GetMarkerLeft(timestamp, duration, 0) + 14;
            Assert(timestamp.HasValue && Math.Abs(center - contentX) < 0.0001,
                "A confirmed timestamp draws the avatar centered on the original selection");
        }
        Assert(MusicWaveformTimeline.GetTimestamp(100d, TimeSpan.Zero) is null,
            "Posting is unavailable until the selected track's duration is known");
        Assert(new[] { 0d, 5.99d, 6d, 35.99d, 36d, 5.99d, 0d, 1500d, -0.01d }
                .Select(x => MusicWaveformTimeline.GetBarIndex(x, 250))
                .SequenceEqual(new[] { 0, 0, 1, 5, 6, 0, 0, 249, -1 }),
            "Fast forward and backward hover maps directly to its current bar across gaps and edges");
        Assert(MusicWaveformTimeline.GetBarIndex(42, 0) == -1,
            "Hover cannot target a waveform while it is being replaced");
        Assert(new[] { -0.01d, 0d, 1.99d, 2d, 6d, 7.99d, 8d, 528.01d, 7.99d, 1495.99d, 1496d, 1500d, 1500.01d }
                .Select(x => MusicWaveformTimeline.GetHoverBarIndex(x, 250))
                .SequenceEqual(new[] { -1, -1, -1, 0, 0, 0, 1, 87, 0, 248, 249, 249, -1 }),
            "Highlight reaches each visible stroke only after the pointer crosses the leading gap");
        var markerTops = Enumerable.Range(0, 5).Select(MusicWaveformTimeline.GetCommentMarkerTop).ToArray();
        var seekLaneEnd = MusicWaveformTimeline.Height * 2 / 3;
        Assert(markerTops.Distinct().Count() == 5 && markerTops.All(top => top - seekLaneEnd >= 2
                && top + MusicWaveformTimeline.MarkerSize <= MusicWaveformTimeline.Height),
            "Five coincident comments keep their distinct vertical hit targets inside the comment lane with a two-unit gap");
        var author = Guid.NewGuid();
        Assert(MusicWaveformTimeline.IsOwnComment(author, author) &&
                !MusicWaveformTimeline.IsOwnComment(author, Guid.NewGuid()) &&
                !MusicWaveformTimeline.IsOwnComment(author, null) &&
                !MusicWaveformTimeline.IsOwnComment(Guid.Empty, Guid.Empty),
            "Only a signed-in account matching the stored author marks a comment as its own");
    }

    private sealed class SyntheticMusicDetails : IMcpMusicDetailsPage
    {
        private readonly Guid _id = Guid.NewGuid();
        public bool Active { get; set; } = true;
        public bool HasCover { get; set; }
        public string Language { get; set; } = "fa-IR";
        public bool CoverOpen { get; private set; }
        public bool RepeatEnabled { get; private set; }
        public bool Loading { get; set; }
        public bool LoadFailed { get; set; }
        public string? Audience { get; private set; }

        public McpMusicDetailsState ReadState() => new()
        {
            MusicPublicId = _id, Title = "Synthetic track", Duration = "00:03:00",
            MetadataLoading = Loading, WaveformLoading = Loading,
            WaveformCommentsLoading = Loading, MetadataLoadFailed = LoadFailed,
            AudiencePopup = Audience, ViewsCount = 15, OnlineViewersCount = 4, LiveListenerCount = 2,
            Language = Language, PlaybackDeviceCount = 2, PlaybackDeviceSelected = true,
            PlaybackDeviceVolume = 0, PlaybackDeviceSilent = true, MusicVideoAvailable = true,
            CoverAvailable = HasCover, CoverOpen = CoverOpen, RepeatEnabled = RepeatEnabled,
            ContributorCount = 2, ContributorsWithPortrait = 1,
            MixerAvailable = true, MixerEnabled = true, MixerActive = true, MixerBalance = 100, WaveformCommentCount = 1
        };

        public bool SetCoverOpen(bool open)
        {
            if (open && !HasCover) return false;
            CoverOpen = open;
            return true;
        }

        public void SetRepeatEnabled(bool enabled) => RepeatEnabled = enabled;

        public bool SetAudience(string view)
        {
            Audience = view == "closed" ? null : view;
            return true;
        }
    }
}
