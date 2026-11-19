using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static async Task MusicDetailsScopesAndLifetime()
    {
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

    private sealed class SyntheticMusicDetails : IMcpMusicDetailsPage
    {
        private readonly Guid _id = Guid.NewGuid();
        public bool Active { get; set; } = true;
        public bool HasCover { get; set; }
        public bool CoverOpen { get; private set; }
        public bool RepeatEnabled { get; private set; }

        public McpMusicDetailsState ReadState() => new()
        {
            MusicPublicId = _id, Title = "Synthetic track", Duration = "00:03:00",
            CoverAvailable = HasCover, CoverOpen = CoverOpen, RepeatEnabled = RepeatEnabled,
            ContributorCount = 2, ContributorsWithPortrait = 1
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
