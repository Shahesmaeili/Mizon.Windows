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
        var marker = new McpRequest { Operation = "set_music_details", MusicDetails = new() { WaveformComment = SyntheticMusicDetails.CommentId.ToString("D") } };
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
            Assert((await peer.Call("get_music_details")).MusicDetails is { WaveformCommentPublicIds: [var loadedId], WaveformCommentPreviewPublicId: null } &&
                loadedId == SyntheticMusicDetails.CommentId && !(await peer.Call(marker)).Success,
                "Track read discovers loaded comment IDs but cannot open a comment bubble.");

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
            Assert((await peer.Call(marker)) is { Success: true, MusicDetails: null } &&
                host.MusicDetailsPage.Preview == SyntheticMusicDetails.CommentId,
                "Track control opens the loaded comment bubble without requiring read consent or changing playback.");
            Assert((await peer.Call(marker with { MusicDetails = new() { WaveformComment = Guid.NewGuid().ToString("D") } })).Code == "waveform_comment_unavailable",
                "An ID absent from the active track cannot select a comment.");
            Assert((await peer.Call(marker with { MusicDetails = new() { WaveformComment = "closed" } })).Success && host.MusicDetailsPage.Preview is null,
                "The waveform preview can be dismissed through the same action.");
            Assert((await peer.Call(repeat with { MusicDetails = new() { RepeatEnabled = false, CoverOpen = false } })).Code == "invalid_arguments"
                && host.MusicDetailsPage.RepeatEnabled, "An ambiguous command changes nothing");
            Assert((await peer.Call(open with { Page = "login" })).Code == "invalid_arguments", "Unrelated arguments are rejected");
            await host.Ui(() => host.MusicDetailsPage.Active = false);
            Assert((await peer.Call(open)).Code == "page_unavailable", "Control stops when the track page leaves");
            Assert((await peer.Call(marker)).Code == "page_unavailable", "An inactive page cannot open a waveform comment.");
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            Assert(!(await peer.Call(repeat)).Success && !(await peer.Call(live)).Success && !(await peer.Call(marker)).Success,
                "Revocation blocks further track actions.");
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

    private static async Task MusicDiscussionScopesAndLifetime()
    {
        var path = Path.Combine(StorageRoot, "music-discussion", "grants.dat");
        var proof = Proof();
        var vote = new McpRequest
        {
            Operation = "manage_music_discussion",
            MusicDiscussion = new() { Action = "vote", CommentPublicId = SyntheticMusicDetails.CommentId, Vote = 1 }
        };
        await using (var host = await Host.StartAsync(path))
        {
            await using var peer = await host.ConnectAsync(proof);
            var id = peer.Hello.Connection!.Id;
            await host.Approve(id, true, true, true, detailsRead: true, detailsControl: true);
            Assert(!(await peer.Call("get_music_discussion")).Success && !(await peer.Call(vote)).Success,
                "Existing grants never authorize the track discussion");
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await host.Approve(id, false, false, false, discussionControl: true);
            Assert((await peer.Call(vote)) is { Success: true, MusicDiscussion: null },
                "Control performs a scoped action without disclosing comments");
            Assert((await peer.Call(vote with { MusicDiscussion = vote.MusicDiscussion! with { Vote = 9 } })).Code == "invalid_arguments",
                "Invalid desired vote is rejected before dispatch");
            Assert((await peer.Call(vote with { MusicDiscussion = vote.MusicDiscussion! with { CommentPublicId = Guid.NewGuid() } })).Code == "comment_unavailable",
                "A comment from another track cannot receive a vote");
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await host.Approve(id, false, false, false, discussionRead: true);
            Assert((await peer.Call("get_music_discussion")).MusicDiscussion is { Comments: [{ MyVote: 1 }] },
                "Read consent returns the active track's actual synthetic vote");
            Assert(!(await peer.Call(vote)).Success, "Read cannot modify votes");
            await host.Ui(() => host.MusicDetailsPage.Active = false);
            Assert((await peer.Call("get_music_discussion")).Code == "page_unavailable", "Leaving the track stops discussion access");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var observed = false;
            try { await host.MusicDetails.ExecuteDiscussionAsync(vote, cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            Assert(observed, "Cancellation is observed before dispatch");
        }
        await using var restarted = await Host.StartAsync(path);
        await using var remembered = await restarted.ConnectAsync(proof);
        Assert(remembered.Hello.Connection is { IsRemembered: true, AllowMusicDiscussionRead: true, AllowMusicDiscussionControl: false },
            "Only the approved discussion grant persists after restart");
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
        Assert(markerTops.Distinct().Count() == 5 && markerTops.All(top => top - seekLaneEnd >= 6
                && top + MusicWaveformTimeline.MarkerSize <= MusicWaveformTimeline.Height),
            "Five coincident comments keep distinct hit targets inside the comment lane with a six-unit top gap.");
        var author = Guid.NewGuid();
        Assert(MusicWaveformTimeline.IsOwnComment(author, author) &&
                !MusicWaveformTimeline.IsOwnComment(author, Guid.NewGuid()) &&
                !MusicWaveformTimeline.IsOwnComment(author, null) &&
                !MusicWaveformTimeline.IsOwnComment(Guid.Empty, Guid.Empty),
            "Only a signed-in account matching the stored author marks a comment as its own");
    }

    private sealed class SyntheticMusicDetails : IMcpMusicDetailsPage
    {
        public static readonly Guid CommentId = Guid.NewGuid();
        private readonly Guid _id = Guid.NewGuid();
        public bool Active { get; set; } = true;
        public bool HasCover { get; set; }
        public string Language { get; set; } = "fa-IR";
        public bool CoverOpen { get; private set; }
        public bool RepeatEnabled { get; private set; }
        public bool Loading { get; set; }
        public bool LoadFailed { get; set; }
        public string? Audience { get; private set; }
        public Guid? Preview { get; private set; }
        private int _discussionVote;

        public McpMusicDiscussionState ReadDiscussion() => new(_id, true, "newest", null, false, false,
            [new(CommentId, "Synthetic comment", "Synthetic author", "Windows", 1234, _discussionVote, 0, _discussionVote, 0, null, [])]);

        public Task<McpResponse> ExecuteDiscussionAsync(McpMusicDiscussionRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.Action != "vote" || request.CommentPublicId != CommentId)
                return Task.FromResult(new McpResponse { Code = "comment_unavailable" });
            _discussionVote = request.Vote!.Value;
            return Task.FromResult(new McpResponse { Success = true, Code = "ok" });
        }

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
            MixerAvailable = true, MixerEnabled = true, MixerActive = true, MixerBalance = 100, WaveformCommentCount = 1,
            WaveformCommentPublicIds = [CommentId], WaveformCommentPreviewPublicId = Preview
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

        public bool SetWaveformCommentPreview(Guid? commentPublicId)
        {
            if (commentPublicId is not null && commentPublicId != CommentId) return false;
            Preview = commentPublicId;
            return true;
        }
    }
}
