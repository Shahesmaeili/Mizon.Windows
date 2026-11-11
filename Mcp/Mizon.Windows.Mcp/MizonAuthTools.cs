using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Mizon.Windows.Mcp;

internal sealed partial class MizonAuthTools(MizonAuthPipeClient bridge)
{
    private const string EmptySchema = """{"type":"object","properties":{},"additionalProperties":false}""";
    private static readonly Tool[] Catalog =
    [
        Define("mizon_get_vpn_state", "VPN page status", "Read the active signed-in VPN page: configuration, connection status, network availability, busy state and decorative particle preference. Requires independent VPN read consent. Does not navigate, start a tunnel, expose IP addresses or credentials, or query other applications. This implementation has no configured tunnel.", EmptySchema, readOnly: true),
        Define("mizon_set_vpn_particles", "Set VPN page animation", "Set decorative particles on the active VPN page. Requires independent VPN control consent. Windows reduced-motion preference still applies. Does not change network or VPN settings. Readback requires VPN read consent.",
            """{"type":"object","properties":{"enabled":{"type":"boolean","description":"Desired decorative-particle preference on the active VPN page. Explicit true or false, not a toggle."}},"required":["enabled"],"additionalProperties":false}"""),
        Define("mizon_open_vpn_settings", "Open Windows VPN settings", "Open only the system Windows VPN settings page through the active signed-in Mizon VPN page. Requires independent VPN control consent. Does not connect, disconnect, edit configuration or accept an arbitrary URI. Never automatically retry an interrupted launch. Readback requires VPN read consent.", EmptySchema, idempotent: false, openWorld: true),
        Define("mizon_get_presence_state", "Online presence state", "Read your own current Mizon presence: whether the main window is active, a session and SignalR connection exist, the online indicator, and whether its state is server-confirmed. Requires separate presence read consent. Reads the shared header/taskbar state without connecting, publishing presence, changing focus or reading another user. HasConfirmedState=false means the displayed state is not confirmed by the server.", EmptySchema, readOnly: true),
        Define("mizon_get_window_state", "Main-window presentation", "Read whether the Mizon main window is available, fullscreen, and currently showing its fullscreen controls. Requires separate window read consent. Does not reveal window coordinates/handles, other windows or page contents, and does not activate the window.", EmptySchema, readOnly: true),
        Define("mizon_set_fullscreen", "Set main-window fullscreen", "Enter or leave fullscreen for the existing Mizon main window through the same operation as its own controls. fullScreen is an explicit desired state, not a toggle. Requires separate window control consent; readback requires window read consent. Does not minimize, close, focus, resize or reposition other windows, and does not change video-only fullscreen or saved theme preferences.",
            """{"type":"object","properties":{"fullScreen":{"type":"boolean"}},"required":["fullScreen"],"additionalProperties":false}"""),
        Define("mizon_get_music_home", "Music home state", "Read the active music home page: bounded cards in each section, source (API or demo), loading/errors, story and featured-player state. Requires independent music home read permission. Does not navigate, fetch extra pages, expose media file paths, or reveal other pages' business data.", EmptySchema, readOnly: true),
        Define("mizon_refresh_music_home", "Refresh music home", "Refresh one declared music home section or all sections using the same loaders as the live page. Requires music home control. Demo sections remain explicitly marked as demo. Only the active signed-in page is available; no automatic navigation. State is returned only with music home read permission.", MusicHomeRefreshSchema, idempotent: false, openWorld: true),
        Define("mizon_music_home_action", "Operate music home", "Perform one allowlisted action on the active music home page and its visible items. Requires music home control; like and follow additionally require music home submit permission. Normal playback may record activity; opening a story automatically records its view through the same UI flow. Like/follow require enabled=true/false (never a toggle); do not retry after interruption. Follow controls currently affect demo data only. request_download waits for the app's native file picker and requires user interaction; it cannot select a path. Read current state/actions first; unavailable/demo actions may report unavailable. This does not control arbitrary tracks, other pages or backend operations. State is returned only with music home read permission.", MusicHomeActionSchema, idempotent: false, openWorld: true),
        Define("mizon_get_taskbar_pin_state", "Taskbar pin state", "Refresh whether Mizon is pinned to Windows taskbar, support, policy, busy and request availability. Requires separate taskbar read permission. Does not reveal other pinned apps or change pinning.", EmptySchema, readOnly: true),
        Define("mizon_request_taskbar_pin", "Request pin to taskbar", "Request the native Windows confirmation to pin Mizon to the taskbar, only at the user's request with Mizon in the foreground. Requires separate taskbar control permission. The user must confirm Windows' dialog; this tool cannot approve it, unpin apps or bypass Windows policy. Never automatically retry a cancelled/interrupted request. State is returned only with taskbar read permission.", EmptySchema, idempotent: false, openWorld: true),
        Define("mizon_get_connection", "Connection permission", "Read only this MCP connection's approval status and granted permissions. Available before approval. It cannot approve a connection.", EmptySchema, readOnly: true),
        Define("mizon_get_auth_state", "Authentication page state", "Read the current Mizon authentication page, busy/validation state and non-secret fields. Passwords and session tokens are never returned. Requires read permission in Mizon.", EmptySchema, readOnly: true),
        Define("mizon_open_auth_page", "Open authentication page", "Open the login, registration or welcome page. This may launch Mizon when it is closed; the user must still approve this connection in the app. Requires control permission.",
            """{"type":"object","properties":{"page":{"type":"string","enum":["login","register","welcome"]}},"required":["page"],"additionalProperties":false}"""),
        Define("mizon_set_auth_form", "Fill authentication form", "Set supplied fields on the visible login or registration form. Unspecified fields remain unchanged; an empty string clears a field. Credentials are write-only. This does not submit the form. Requires control permission.",
            """{"type":"object","properties":{"page":{"type":"string","enum":["login","register"]},"form":{"type":"object","properties":{"identifier":{"type":"string","maxLength":128},"password":{"type":"string","maxLength":128},"firstName":{"type":"string","maxLength":60},"lastName":{"type":"string","maxLength":60},"username":{"type":"string","maxLength":32},"email":{"type":"string","maxLength":128},"confirmPassword":{"type":"string","maxLength":128}},"minProperties":1,"additionalProperties":false}},"required":["page","form"],"additionalProperties":false}""", destructive: true),
        Define("mizon_submit_auth_form", "Submit authentication form", "Submit the current login or registration form through Mizon's existing validation and API logic. Registration creates an account. Requires submit permission and the user's request; never automatically retry an interrupted submission.",
            """{"type":"object","properties":{"page":{"type":"string","enum":["login","register"]}},"required":["page"],"additionalProperties":false}""", idempotent: false, openWorld: true),
        Define("mizon_clear_auth_form", "Clear authentication form", "Clear the login or registration form, including passwords. Requires control permission.",
            """{"type":"object","properties":{"page":{"type":"string","enum":["login","register"]}},"required":["page"],"additionalProperties":false}""", destructive: true),
        Define("mizon_retry_welcome", "Retry welcome initialization", "Retry the welcome page's existing initialization after a failure. The application may navigate onward when ready. Requires control permission.", EmptySchema, idempotent: false, openWorld: true),
        Define("mizon_set_app_language", "Change Mizon language", "Select Persian or English for the live application. Uses the same preference as Mizon's language picker. Requires control permission.",
            """{"type":"object","properties":{"language":{"type":"string","enum":["fa-IR","en-US"]}},"required":["language"],"additionalProperties":false}"""),
        Define("mizon_get_appearance", "Appearance state", "Read chosen and effective theme, window material, support, persistence and fallback status. Requires the separate appearance read permission; authentication permission does not authorize this tool.", EmptySchema, readOnly: true),
        Define("mizon_set_appearance", "Change appearance", "Apply and save the chosen theme and/or main-window material through Mizon Settings. Omitted preferences stay unchanged. Requires separate appearance control permission; state is returned only with appearance read permission. System accessibility and material support may require a solid fallback. Does not navigate or change other settings.",
            """{"type":"object","properties":{"theme":{"type":"string","enum":["system","light","dark"]},"backdrop":{"type":"string","enum":["acrylic","acrylicBase","acrylicThin","mica","micaAlt"]}},"minProperties":1,"additionalProperties":false}"""),
        Define("mizon_get_navigation", "Current destination", "Read the current canonical destination, pending deep link and back-navigation availability. Requires separate navigation read permission; authentication and appearance grants do not authorize this tool. Does not read page contents.", EmptySchema, readOnly: true),
        Define("mizon_open_destination", "Open Mizon destination", "Open one declared app destination through the same route handler as deep links. Requires navigation control permission. A signed-out content request returns requires_sign_in and is never queued for a later login. music-details, playlist, client and story require publicId; music-comment requires publicId (music) and commentPublicId; search requires searchQuery; settings optionally accepts settingsSection. This tool does not submit forms, start playback/downloads or edit settings. Navigation state and the typed invocation receipt (including opened/completed stage) are returned only with navigation read permission.",
            """{"type":"object","properties":{"destination":{"type":"string","enum":["login","register","home","music","movies","apps","vpn","playlists","playlist","music-details","music-comment","client","story","activities","settings","search"]},"publicId":{"type":"string","format":"uuid"},"commentPublicId":{"type":"string","format":"uuid"},"searchQuery":{"type":"string","minLength":1,"maxLength":120},"settingsSection":{"type":"string","enum":["account","security","language","appearance","notifications","storage","updates","platforms","ai-connections"]}},"required":["destination"],"additionalProperties":false}""", openWorld: true)
    ];

    public ValueTask<ListToolsResult> ListAsync(RequestContext<ListToolsRequestParams> context, CancellationToken cancellationToken)
    {
        // MCP clients can discover tools without using them, including discovery
        // in short-lived workers. This must not create an app consent session.
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ListToolsResult { Tools = Catalog });
    }

    public async ValueTask<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
    {
        try
        {
            var request = ParseRequest(context.Params);
            var response = await bridge.SendAsync(request, context.Server.ClientInfo,
                context.Params.Name == "mizon_open_auth_page", cancellationToken);
            return Result(response);
        }
        catch (ArgumentException)
        {
            // Do not echo invalid argument values: they may contain credentials.
            return Result(new McpResponse
            {
                Success = false,
                Code = "invalid_arguments",
                Message = "Arguments do not match this tool's schema. Use only the listed fields and values."
            });
        }
    }

    private static McpRequest ParseRequest(CallToolRequestParams call)
    {
        var args = call.Arguments;
        switch (call.Name)
        {
            case "mizon_get_vpn_state":
                CheckKeys(args);
                return new() { Operation = "get_vpn_state" };
            case "mizon_open_vpn_settings":
                CheckKeys(args);
                return new() { Operation = "open_vpn_settings" };
            case "mizon_set_vpn_particles":
                CheckKeys(args, "enabled");
                if (args is null || !args.TryGetValue("enabled", out var particlesEnabled)
                    || particlesEnabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException();
                return new() { Operation = "set_vpn_particles", Vpn = new() { Enabled = particlesEnabled.GetBoolean() } };
            case "mizon_get_window_state":
                CheckKeys(args);
                return new() { Operation = "get_window_state" };
            case "mizon_set_fullscreen":
                CheckKeys(args, "fullScreen");
                if (args is null || !args.TryGetValue("fullScreen", out var fullScreen)
                    || fullScreen.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException();
                return new() { Operation = "set_fullscreen", FullScreen = fullScreen.GetBoolean() };
            case "mizon_get_presence_state":
                CheckKeys(args);
                return new() { Operation = "get_presence_state" };
            case "mizon_get_music_home":
                CheckKeys(args);
                return new() { Operation = "get_music_home" };
            case "mizon_refresh_music_home":
            case "mizon_music_home_action":
                return ParseMusicHome(call.Name, args);
            case "mizon_get_taskbar_pin_state":
                CheckKeys(args);
                return new() { Operation = "get_taskbar_pin_state" };
            case "mizon_request_taskbar_pin":
                CheckKeys(args);
                return new() { Operation = "request_taskbar_pin" };
            case "mizon_get_connection":
                CheckKeys(args);
                return new() { Operation = "connection_status" };
            case "mizon_get_auth_state":
                CheckKeys(args);
                return new() { Operation = "get_state" };
            case "mizon_get_appearance":
                CheckKeys(args);
                return new() { Operation = "get_appearance" };
            case "mizon_get_navigation":
                CheckKeys(args);
                return new() { Operation = "get_navigation" };
            case "mizon_open_destination":
                return ParseNavigation(args);
            case "mizon_set_appearance":
                CheckKeys(args, "theme", "backdrop");
                if (args is null || args.Count == 0) throw new ArgumentException();
                McpAppearanceTheme? theme = args.ContainsKey("theme") ? RequiredString(args, "theme", 6) switch
                {
                    "system" => McpAppearanceTheme.System, "light" => McpAppearanceTheme.Light,
                    "dark" => McpAppearanceTheme.Dark, _ => throw new ArgumentException()
                } : null;
                McpAppearanceBackdrop? backdrop = args.ContainsKey("backdrop") ? RequiredString(args, "backdrop", 11) switch
                {
                    "acrylic" => McpAppearanceBackdrop.Acrylic, "acrylicBase" => McpAppearanceBackdrop.AcrylicBase,
                    "acrylicThin" => McpAppearanceBackdrop.AcrylicThin, "mica" => McpAppearanceBackdrop.Mica,
                    "micaAlt" => McpAppearanceBackdrop.MicaAlt, _ => throw new ArgumentException()
                } : null;
                return new() { Operation = "set_appearance", Appearance = new() { Theme = theme, Backdrop = backdrop } };
            case "mizon_retry_welcome":
                CheckKeys(args);
                return new() { Operation = "retry_welcome" };
            case "mizon_open_auth_page":
                CheckKeys(args, "page");
                return new() { Operation = "open_page", Page = Page(args, allowWelcome: true) };
            case "mizon_submit_auth_form":
                CheckKeys(args, "page");
                return new() { Operation = "submit", Page = Page(args) };
            case "mizon_clear_auth_form":
                CheckKeys(args, "page");
                return new() { Operation = "clear_form", Page = Page(args) };
            case "mizon_set_app_language":
                CheckKeys(args, "language");
                var language = RequiredString(args, "language", 5);
                if (language is not ("fa-IR" or "en-US")) throw new ArgumentException();
                return new() { Operation = "set_language", Language = language };
            case "mizon_set_auth_form":
                CheckKeys(args, "page", "form");
                var page = Page(args);
                if (args is null || !args.TryGetValue("form", out var form) || form.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException();
                var count = 0;
                foreach (var property in form.EnumerateObject())
                {
                    count++;
                    if (property.Name is not ("identifier" or "password" or "firstName" or "lastName" or "username" or "email" or "confirmPassword"))
                        throw new ArgumentException();
                    if (property.Value.ValueKind != JsonValueKind.String) throw new ArgumentException();
                }
                if (count == 0) throw new ArgumentException();
                return new()
                {
                    Operation = "set_form", Page = page,
                    Form = new()
                    {
                        Identifier = OptionalString(form, "identifier", 128),
                        Password = OptionalString(form, "password", 128),
                        FirstName = OptionalString(form, "firstName", 60),
                        LastName = OptionalString(form, "lastName", 60),
                        Username = OptionalString(form, "username", 32),
                        Email = OptionalString(form, "email", 128),
                        ConfirmPassword = OptionalString(form, "confirmPassword", 128)
                    }
                };
            default:
                throw new ArgumentException();
        }
    }

    private static McpRequest ParseNavigation(IDictionary<string, JsonElement>? args)
    {
        CheckKeys(args, "destination", "publicId", "commentPublicId", "searchQuery", "settingsSection");
        var destination = RequiredString(args, "destination", 20);
        if (destination is not ("login" or "register" or "home" or "music" or "movies" or "apps" or "vpn"
            or "playlists" or "playlist" or "music-details" or "music-comment" or "client" or "story" or "activities" or "settings" or "search"))
            throw new ArgumentException();
        var needsId = destination is "playlist" or "music-details" or "music-comment" or "client" or "story";
        if (args!.ContainsKey("publicId") != needsId || args.ContainsKey("commentPublicId") != (destination == "music-comment")
            || args.ContainsKey("searchQuery") != (destination == "search")
            || args.ContainsKey("settingsSection") && destination != "settings") throw new ArgumentException();
        Guid? publicId = needsId ? Identifier(args, "publicId") : null;
        Guid? comment = destination == "music-comment" ? Identifier(args, "commentPublicId") : null;
        var query = destination == "search" ? RequiredString(args, "searchQuery", 120).Trim() : null;
        if (query is not null && (query.Length == 0 || query.Any(char.IsControl))) throw new ArgumentException();
        var section = args.ContainsKey("settingsSection") ? RequiredString(args, "settingsSection", 20) : null;
        if (section is not null and not ("account" or "security" or "language" or "appearance" or "notifications"
            or "storage" or "updates" or "platforms" or "ai-connections")) throw new ArgumentException();
        return new() { Operation = "open_destination", Navigation = new()
        {
            Destination = destination, PublicId = publicId, CommentPublicId = comment,
            SearchQuery = query, SettingsSection = section
        } };
    }

    private static Guid Identifier(IDictionary<string, JsonElement> args, string key) =>
        Guid.TryParseExact(RequiredString(args, key, 36), "D", out var value) && value != Guid.Empty
            ? value : throw new ArgumentException();

    private static string Page(IDictionary<string, JsonElement>? args, bool allowWelcome = false)
    {
        var page = RequiredString(args, "page", 8);
        if (page is "login" or "register" || allowWelcome && page == "welcome") return page;
        throw new ArgumentException();
    }

    private static string RequiredString(IDictionary<string, JsonElement>? args, string key, int maxLength)
    {
        if (args is null || !args.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ArgumentException();
        var text = value.GetString();
        if (string.IsNullOrEmpty(text) || text.Length > maxLength) throw new ArgumentException();
        return text;
    }

    private static string? OptionalString(JsonElement form, string key, int maxLength)
    {
        if (!form.TryGetProperty(key, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException();
        var text = value.GetString();
        if (text is null || text.Length > maxLength) throw new ArgumentException();
        return text;
    }

    private static void CheckKeys(IDictionary<string, JsonElement>? args, params string[] allowed)
    {
        if (args is null) return;
        foreach (var key in args.Keys)
            if (!allowed.Contains(key, StringComparer.Ordinal)) throw new ArgumentException();
    }

    private static Tool Define(string name, string title, string description, string schema,
        bool readOnly = false, bool idempotent = true, bool openWorld = false, bool destructive = false)
    {
        return new Tool
        {
            Name = name, Title = title, Description = description,
            InputSchema = MizonAuthSchemas.Input(schema),
            OutputSchema = MizonAuthSchemas.Output,
            Annotations = new()
            {
                ReadOnlyHint = readOnly, DestructiveHint = destructive,
                IdempotentHint = idempotent, OpenWorldHint = openWorld
            }
        };
    }

    private static CallToolResult Result(McpResponse response)
    {
        var json = JsonSerializer.SerializeToElement(response, McpJsonContext.Default.McpResponse);
        return new CallToolResult
        {
            IsError = !response.Success,
            StructuredContent = json,
            Content = [new TextContentBlock { Text = json.GetRawText() }]
        };
    }
}
