using Mizon.Windows.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// Standard output belongs exclusively to MCP. Never write credentials or request
// bodies to diagnostics; failures are returned as sanitized structured results.
using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    lifetime.Cancel();
};

string? clientProfile = null;
if (args.Length != 0)
{
    if (args is not ["--client-profile", var configuredProfile]
        || (!Guid.TryParseExact(configuredProfile, "N", out var parsedProfile)
            && !Guid.TryParseExact(configuredProfile, "D", out parsedProfile)) || parsedProfile == Guid.Empty)
    {
        Console.Error.WriteLine("Mizon MCP requires --client-profile followed by the configured profile identifier.");
        Environment.ExitCode = 2;
        return;
    }
    clientProfile = parsedProfile.ToString("N");
}
await using var bridge = new MizonAuthPipeClient(clientProfile: clientProfile);
var tools = new MizonAuthTools(bridge);
var options = new McpServerOptions
{
    ServerInfo = new Implementation
    {
        Name = "Mizon.Authentication",
        Title = "Mizon — Authentication, appearance, navigation and music home",
        Version = "1.0.0"
    },
    ServerInstructions = "This server exposes Mizon's welcome, sign-in and registration pages, main-window appearance preferences, declared app destinations and the active music home page. "
        + "Mizon requires the user to approve a client identity inside the app before any account state or operation is accessible. "
        + "Approved permissions survive app/client restarts until the user revokes them in Settings; a client name alone never authorizes access. "
        + "Use mizon_get_connection to inspect this connection's approval status; pending approval cannot be bypassed by a tool. "
        + "Appearance read and control require their own explicit permissions; authentication approval grants neither. "
        + "Use mizon_get_appearance and mizon_set_appearance for the listed theme and backdrop choices only. "
        + "Navigation requires separate read/control permissions. Use mizon_get_navigation and mizon_open_destination only for declared pages/popups; those navigation tools do not authorize playback or downloads. "
        + "Music home requires independent read/control/submit grants. Use its typed tools on the active signed-in home page only; snapshots identify API and demo sections. Like/follow additionally require submit permission. Opening stories and playback can record normal activity. Native save pickers remain user-owned. No raw paths, arbitrary URI, SQL or backend invocation is exposed. "
        + "A signed-out content navigation returns requires_sign_in without queueing the action for later. Navigation grants do not grant access to page contents or edits. "
        + "Taskbar pin status and requesting pinning require independent taskbar read/control grants. Request pinning only at the user's request while Mizon is foreground; Windows confirmation belongs to the user and cannot be automated. Never retry interrupted pin requests automatically. "
        + "Open an authentication page only when requested. Credentials are write-only: never request password values back, "
        + "repeat them in your answer, or submit a registration without the user's instruction. "
        + "Only tool execution confirms an action; describing an action does not execute it.",
    Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
    Handlers = new McpServerHandlers
    {
        ListToolsHandler = tools.ListAsync,
        CallToolHandler = tools.CallAsync
    }
};

try
{
    await using var transport = new StdioServerTransport(options);
    await using var server = McpServer.Create(transport, options);
    await server.RunAsync(lifetime.Token);
}
catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
catch (Exception)
{
    // Avoid serializing exception objects: transport and tool inputs can contain
    // secrets. The caller still gets a nonzero process exit on startup failure.
    Console.Error.WriteLine("Mizon MCP could not continue. Restart the MCP connection.");
    Environment.ExitCode = 1;
}
