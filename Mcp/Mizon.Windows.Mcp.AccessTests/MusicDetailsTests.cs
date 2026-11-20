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
            await host.Ui(() => host.MusicDetailsPage.Language = "en-US");
            Assert((await peer.Call("get_music_details")).MusicDetails is
                { Language: "en-US", PlaybackDeviceCount: 2, PlaybackDeviceSelected: true,
                    PlaybackDeviceVolume: 0, PlaybackDeviceSilent: true, MusicVideoAvailable: true },
                "Track readback reflects app language and bounded render-device availability without endpoint identifiers");
            Assert(!(await peer.Call(repeat)).Success && !host.MusicDetailsPage.RepeatEnabled, "Read cannot set repeat");

            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await host.Approve(id, false, false, false, detailsControl: true);
            Assert((await peer.Call(open)).Code == "cover_unavailable", "Missing cover yields an actionable failure");
            await host.Ui(() => host.MusicDetailsPage.HasCover = true);
            Assert((await peer.Call(open)) is { Success: true, MusicDetails: null } && host.MusicDetailsPage.CoverOpen,
                "Control opens the cover without leaking read state");
            Assert((await peer.Call(open with { MusicDetails = new() { CoverOpen = false } })).Success
                && !host.MusicDetailsPage.CoverOpen, "The desired false closes the preview");
            Assert((await peer.Call(repeat)).Success && host.MusicDetailsPage.RepeatEnabled, "Repeat uses the page's playback state");
            Assert((await peer.Call(repeat with { MusicDetails = new() { RepeatEnabled = false, CoverOpen = false } })).Code == "invalid_arguments"
                && host.MusicDetailsPage.RepeatEnabled, "An ambiguous command changes nothing");
            Assert((await peer.Call(open with { Page = "login" })).Code == "invalid_arguments", "Unrelated arguments are rejected");
            await host.Ui(() => host.MusicDetailsPage.Active = false);
            Assert((await peer.Call(open)).Code == "page_unavailable", "Control stops when the track page leaves");
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            Assert(!(await peer.Call(repeat)).Success, "Revocation blocks further track actions");
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
        var stackedOffsets = Enumerable.Range(0, 5).Select(MusicWaveformTimeline.GetCommentStackOffset).ToArray();
        Assert(stackedOffsets[0] == 0 && stackedOffsets.Distinct().Count() == 5 &&
                stackedOffsets.All(offset => offset is >= -18 and <= 4),
            "Up to five coincident comments have separate vertical hit targets without moving their timestamps");
    }

    private sealed class SyntheticMusicDetails : IMcpMusicDetailsPage
    {
        private readonly Guid _id = Guid.NewGuid();
        public bool Active { get; set; } = true;
        public bool HasCover { get; set; }
        public string Language { get; set; } = "fa-IR";
        public bool CoverOpen { get; private set; }
        public bool RepeatEnabled { get; private set; }

        public McpMusicDetailsState ReadState() => new()
        {
            MusicPublicId = _id, Title = "Synthetic track", Duration = "00:03:00",
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
    }
}
