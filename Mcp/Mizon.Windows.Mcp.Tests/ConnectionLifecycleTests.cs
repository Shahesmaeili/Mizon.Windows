using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using Mizon.Windows.Mcp.Contracts;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Mizon.Windows.Mcp.Tests;

internal static partial class ConnectionLifecycleTests
{
    private static readonly Implementation Identity = new() { Name = "Synthetic transport fixture", Version = "1.0" };

    public static async Task<int> RunIsolatedHostAsync(string suffix)
    {
        WindowsIdentity.UseIsolatedPipe(suffix);
        await using var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        var tools = new MizonAuthTools(companion);
        var options = new McpServerOptions
        {
            ServerInfo = new() { Name = "Mizon isolated transport fixture", Version = "1.0" },
            Capabilities = new() { Tools = new() },
            Handlers = new() { ListToolsHandler = tools.ListAsync, CallToolHandler = tools.CallAsync }
        };
        await using var transport = new StdioServerTransport(options);
        await using var server = McpServer.Create(transport, options);
        await server.RunAsync();
        return 0;
    }

    public static async Task DiscoveryCreatesNoConnection()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var first = await CreateFixtureClientAsync(timeout.Token);
        await first.ListToolsAsync(cancellationToken: timeout.Token);
        await first.ListToolsAsync(cancellationToken: timeout.Token);
        await using var second = await CreateFixtureClientAsync(timeout.Token);
        await second.ListToolsAsync(cancellationToken: timeout.Token);
        await Task.Delay(100, timeout.Token);
        Check.That(app.Connections == 0, "Discovery-only MCP workers created app consent sessions.");
        var result = await first.CallToolAsync(new CallToolRequestParams { Name = "mizon_get_connection" }, cancellationToken: timeout.Token);
        Check.That(result.StructuredContent?.GetProperty("connection").GetProperty("status").GetString() == "pending",
            "The first explicit call did not create a pending connection.");
        await first.ListToolsAsync(cancellationToken: timeout.Token);
        await second.ListToolsAsync(cancellationToken: timeout.Token);
        Check.That(app.Connections == 1 && app.Disconnections == 0, "Discovery recreated or disconnected a live connection.");
    }

    private static Task<McpClient> CreateFixtureClientAsync(CancellationToken token) => McpClient.CreateAsync(
        new StdioClientTransport(new()
        {
            Command = Environment.ProcessPath!,
            Arguments = ["--isolated-mcp-host", WindowsIdentity.PipeSuffix],
            Name = "Mizon isolated SDK fixture"
        }), cancellationToken: token);

    public static async Task CompanionCatalogIsolated()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        await CompanionTests.Run(Environment.ProcessPath!, ["--isolated-mcp-host", WindowsIdentity.PipeSuffix]);
    }

    public static async Task AppearanceTypedDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        foreach (var (backdrop, expected) in new[]
        {
            ("acrylic", McpAppearanceBackdrop.Acrylic), ("acrylicBase", McpAppearanceBackdrop.AcrylicBase),
            ("acrylicThin", McpAppearanceBackdrop.AcrylicThin), ("mica", McpAppearanceBackdrop.Mica), ("micaAlt", McpAppearanceBackdrop.MicaAlt)
        })
        {
            using var arguments = System.Text.Json.JsonDocument.Parse("{\"theme\":\"dark\",\"backdrop\":\"" + backdrop + "\"}");
            var result = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = "mizon_set_appearance",
                Arguments = arguments.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
            }, cancellationToken: timeout.Token);
            Check.That(result.IsError == true && result.StructuredContent?.GetProperty("code").GetString() == "approval_required",
                "Synthetic pending identity unexpectedly mutated appearance.");
            Check.That(app.LastRequest is { Operation: "set_appearance", Appearance.Theme: McpAppearanceTheme.Dark }
                && app.LastRequest.Appearance.Backdrop == expected, "SDK appearance inputs did not reach the pipe as typed preferences.");
        }
    }

    public static async Task CallSurvivesSlowHandshake()
    {
        await using var app = new SyntheticApp(TimeSpan.FromMilliseconds(900));
        await using var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        var result = await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
        await Task.Delay(1100);
        Console.WriteLine($"  Synthetic delayed call: code={result.Code}; {app.Connections} connection rows, {app.Disconnections} disconnects.");
        Check.That(result.Connection?.Status == "pending" && app.Connections == 1 && app.Disconnections == 0,
            "An already-running app with a delayed handshake was reported as unavailable and its connection was closed.");
    }

    public static async Task RepeatedCallsRetainConnection()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        await using var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        for (var index = 0; index < 4; index++)
        {
            var result = await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
            Check.That(result.Connection?.Status == "pending", "Synthetic connection did not remain pending.");
        }
        Check.That(app.Connections == 1 && app.Disconnections == 0, "Routine calls unnecessarily recreated a connected pipe.");
    }

    public static async Task UnconfiguredProfileStaysSessionOnly()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        var directory = Path.Combine(WindowsIdentity.StorageDirectory, "unconfigured");
        await using var companion = new MizonAuthPipeClient(directory);
        var result = await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
        Check.That(result.Connection?.Status == "pending", "An unconfigured legacy client did not connect normally.");
        Check.That(!Directory.Exists(directory), "Client metadata alone created a persistent identity without a configured profile.");
    }

    public static async Task HandshakeCancellationClosesConnection()
    {
        await using var app = new SyntheticApp(TimeSpan.FromMilliseconds(900));
        await using var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Check.Throws<OperationCanceledException>(async () =>
            await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, cancelled.Token));
        var result = await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
        Check.That(result.Connection?.Id == "2" && app.Connections == 2 && app.Disconnections == 1,
            "A cancelled handshake was retained or leaked its old reply into a subsequent call.");
    }

    public static async Task HandshakeEofDoesNotRetry()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero, dropFirstHello: true);
        await using var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        var failed = await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
        Check.That(failed.Code == "connection_lost" && app.Connections == 1,
            "An incomplete handshake was retried or mistaken for an absent app.");
        var result = await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
        Check.That(result.Connection?.Id == "2", "The next explicit call did not establish a fresh connection after EOF.");
    }

    public static async Task MutationIsNotReplayed()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero, dropMutationReply: true);
        await using var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        // The fixture never grants permissions or performs form changes. It only
        // counts receipt of a synthetic mutation packet before dropping the pipe.
        var result = await companion.SendAsync(new() { Operation = "set_form", Page = "login", Form = new() { Identifier = "synthetic" } }, Identity, false, CancellationToken.None);
        Check.That(result.Code == "connection_lost" && app.MutationAttempts == 1 && app.Connections == 1,
            "An interrupted mutation was automatically replayed.");
    }

    public static async Task CancellationCannotMixReplies()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        await using var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Check.Throws<OperationCanceledException>(async () =>
            await companion.SendAsync(new() { Operation = "get_state" }, Identity, false, cancelled.Token));
        var result = await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
        Check.That(result.Code == "pending" && result.State is null && result.Connection?.Id == "2",
            "A cancelled exchange reused a connection that could still return its previous reply.");
        Check.That(app.Connections == 2, "Cancellation must close an in-flight framed exchange before reconnection.");
    }

    public static async Task DisposalCancelsActiveExchange()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
        var active = companion.SendAsync(new() { Operation = "get_state" }, Identity, false, CancellationToken.None).AsTask();
        await Task.Delay(100);
        var stopwatch = Stopwatch.StartNew();
        await companion.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await Check.Throws<OperationCanceledException>(async () => await active);
        Check.That(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "Disposal waited for the request timeout instead of cancelling active I/O.");
    }

    public static async Task QueuedCancellationKeepsActiveConnection()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        await using var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, CancellationToken.None);
        var active = companion.SendAsync(new() { Operation = "get_state" }, Identity, false, CancellationToken.None).AsTask();
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Check.Throws<OperationCanceledException>(async () =>
            await companion.SendAsync(new() { Operation = "connection_status" }, Identity, false, cancelled.Token));
        var result = await active;
        Check.That(result.Code == "approval_required" && app.Connections == 1 && app.Disconnections == 0,
            "Cancelling a request waiting for the gate interrupted the active exchange.");
    }

    public static async Task DisposalIsIdempotent()
    {
        var companion = new MizonAuthPipeClient(WindowsIdentity.CredentialDirectory, WindowsIdentity.ClientProfile);
        await companion.DisposeAsync();
        await companion.DisposeAsync();
    }

    private sealed class SyntheticApp : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ConcurrentDictionary<NamedPipeServerStream, byte> _pipes = new();
        private readonly ConcurrentBag<Task> _sessions = [];
        private readonly TimeSpan _helloDelay;
        private readonly TimeSpan _requestDelay;
        private readonly bool _dropFirstHello;
        private readonly bool _dropMutationReply;
        private readonly Task _listener;
        private int _connections;
        private int _disconnections;
        private int _mutationAttempts;
        public int Connections => Volatile.Read(ref _connections);
        public int Disconnections => Volatile.Read(ref _disconnections);
        public int MutationAttempts => Volatile.Read(ref _mutationAttempts);
        public McpRequest? LastRequest { get; private set; }
        public McpResponse? NavigationResponse { get; set; }

        public SyntheticApp(TimeSpan helloDelay, TimeSpan requestDelay = default, bool dropFirstHello = false, bool dropMutationReply = false)
        {
            _helloDelay = helloDelay;
            _requestDelay = requestDelay;
            _dropFirstHello = dropFirstHello;
            _dropMutationReply = dropMutationReply;
            _listener = ListenAsync();
        }

        private async Task ListenAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    // Never use the current Windows SID or the application's pipe.
                    var pipe = new NamedPipeServerStream(McpPipeProtocol.PipePrefix + WindowsIdentity.PipeSuffix,
                        PipeDirection.InOut, 8, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    _pipes.TryAdd(pipe, 0);
                    await pipe.WaitForConnectionAsync(_lifetime.Token);
                    _sessions.Add(HandleAsync(pipe));
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or IOException) { }
        }

        private async Task HandleAsync(NamedPipeServerStream pipe)
        {
            var rowCreated = false;
            try
            {
                var hello = await McpPipeProtocol.ReadAsync(pipe, McpJsonContext.Default.McpRequest, _lifetime.Token);
                Check.That(hello?.Operation == "connect", "The companion omitted its handshake.");
                var id = Interlocked.Increment(ref _connections).ToString(System.Globalization.CultureInfo.InvariantCulture);
                rowCreated = true;
                if (_dropFirstHello && id == "1") return;
                // Simulate dispatcher/backpressure delay only, not user consent.
                await Task.Delay(_helloDelay, _lifetime.Token);
                await ReplyAsync(pipe, id, "pending");
                while (!_lifetime.IsCancellationRequested)
                {
                    var request = await McpPipeProtocol.ReadAsync(pipe, McpJsonContext.Default.McpRequest, _lifetime.Token);
                    if (request is null) return;
                    LastRequest = request;
                    if (request.Operation is not ("connection_status" or "get_state" or "get_appearance"))
                    {
                        Interlocked.Increment(ref _mutationAttempts);
                        if (_dropMutationReply) return;
                    }
                    if (request.Operation != "connection_status") await Task.Delay(_requestDelay, _lifetime.Token);
                    if (request.Operation == "open_destination" && NavigationResponse is { } navigationResponse)
                        await McpPipeProtocol.WriteAsync(pipe, navigationResponse, McpJsonContext.Default.McpResponse, _lifetime.Token);
                    else
                        await ReplyAsync(pipe, id, request.Operation == "connection_status" ? "pending" : "approval_required");
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or IOException) { }
            finally
            {
                if (rowCreated) Interlocked.Increment(ref _disconnections);
                _pipes.TryRemove(pipe, out _);
                pipe.Dispose();
            }
        }

        private Task ReplyAsync(NamedPipeServerStream pipe, string id, string code) =>
            McpPipeProtocol.WriteAsync(pipe, new McpResponse
            {
                Success = code == "pending", Code = code, Message = "Synthetic pending connection only.",
                Connection = new() { Id = id, ClientName = "Synthetic fixture", Status = "pending" }
            }, McpJsonContext.Default.McpResponse, _lifetime.Token);

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            foreach (var pipe in _pipes.Keys) pipe.Dispose();
            await _listener;
            await Task.WhenAll(_sessions);
            _lifetime.Dispose();
        }
    }
}
