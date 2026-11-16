using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

internal static partial class Program
{
    private static async Task PersonProfileScopesAndLifetime()
    {
        var path = Path.Combine(StorageRoot, "person-profile", "grants.dat");
        var proof = Proof();
        await using (var host = await Host.StartAsync(path))
        {
            await using var peer = await host.ConnectAsync(proof);
            var id = peer.Hello.Connection!.Id;
            var follow = new McpRequest { Operation = "set_person_following", PersonProfile = new() { Following = true } };
            Assert((await peer.Call("get_person_profile")).Code == "approval_required", "Pending artist access is gated");
            await host.Approve(id, true, true, true, musicRead: true, musicControl: true, musicSubmit: true);
            Assert(!(await peer.Call("get_person_profile")).Success && !(await peer.Call(follow)).Success,
                "Old authentication and music permissions confer no artist access");

            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await host.Approve(id, false, false, false, personRead: true);
            Assert((await peer.Call("get_person_profile")).PersonProfile is { FollowersCount: 2, IsFollowing: false },
                "Artist read grant reports bounded current-page state");
            Assert(!(await peer.Call(follow)).Success && !host.PersonPage.Following, "Read grant cannot follow");

            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await host.Approve(id, false, false, false, personControl: true);
            Assert((await peer.Call(follow)) is { Success: true, PersonProfile: null } && host.PersonPage.Following,
                "Control invokes the shared follow action without returning read state");
            Assert((await peer.Call("load_person_tracks")).Success && host.PersonPage.Loads == 1,
                "Control loads another displayed page");
            Assert((await peer.Call(new McpRequest
            {
                Operation = "open_person_track", PersonProfile = new() { MusicPublicId = Guid.NewGuid() }
            })).Code == "track_unavailable", "Control cannot open an unrelated track");
            Assert((await peer.Call(new McpRequest
            {
                Operation = "open_person_track", PersonProfile = new() { MusicPublicId = host.PersonPage.MusicId }
            })).Success, "Control opens an already displayed track");
            Assert((await peer.Call(follow with { Page = "login" })).Code == "invalid_arguments", "Artist mutations reject unrelated fields");
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));

            await host.Approve(id, false, false, false, personRead: true, personControl: true);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.PersonPage.SetFollow = async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return true;
            };
            var pending = peer.Call(follow);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            Assert((await pending) is { Code: "cancelled", PersonProfile: null }, "Revocation cancels a pending artist action");
            await host.Approve(id, false, false, false, personRead: true);
        }
        await using (var host = await Host.StartAsync(path))
        {
            await using var peer = await host.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { IsRemembered: true, AllowPersonProfileRead: true, AllowPersonProfileControl: false },
                "Independent artist read grant survives restart");
            host.PersonPage.IsActive = false;
            Assert((await peer.Call("get_person_profile")).Code == "page_unavailable", "MCP does not revive an inactive artist page");
        }
    }

    private sealed class SyntheticPersonPage : IMcpPersonProfilePage, IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();
        public Guid MusicId { get; } = Guid.NewGuid();
        public bool IsActive { get; set; } = true;
        public bool Following { get; private set; }
        public int Loads { get; private set; }
        public Func<CancellationToken, Task<bool>>? SetFollow { get; set; }
        public CancellationToken Lifetime => _lifetime.Token;

        public McpPersonProfileState ReadState() => new()
        {
            IsFollowing = Following, FollowersCount = Following ? 3 : 2, CanFollow = true,
            Tracks = [new(MusicId, "Test track")]
        };

        public Task<bool> SetFollowingAsync(bool following, CancellationToken token)
        {
            if (SetFollow is not null) return SetFollow(token);
            Following = following;
            return Task.FromResult(true);
        }
        public Task<bool> LoadNextTracksAsync(CancellationToken token) { Loads++; return Task.FromResult(true); }
        public bool TryOpenTrack(Guid id) => id == MusicId;
        public void Dispose() => _lifetime.Dispose();
    }
}
