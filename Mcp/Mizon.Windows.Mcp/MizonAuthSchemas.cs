using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp;

internal static class MizonAuthSchemas
{
    // Export the same generated metadata used to write structured tool results.
    // This keeps the discovery contract synchronized without reflection scanning.
    public static JsonElement Output { get; } = CreateOutput();

    public static JsonElement Input(string json)
    {
        var schema = JsonNode.Parse(json)!.AsObject();
        if (schema["properties"] is JsonObject properties)
        {
            Describe(properties, "page", "Authentication page only. login and register identify the exact visible form; welcome is supported only by open_auth_page. Other application pages are outside this MCP scope.");
            Describe(properties, "language", "Live application language: fa-IR is Persian and en-US is English. This also updates the saved language preference.");
            Describe(properties, "theme", "Saved theme preference: system follows Windows; light or dark overrides the app theme immediately. Omit to keep the existing preference.");
            Describe(properties, "fullScreen", "Required desired presentation of the main application window. true enters fullscreen; false restores windowed presentation. This does not toggle or alter a video player's fullscreen state.");
            Describe(properties, "backdrop", "Saved main-window material: acrylic uses the default Acrylic style; acrylicBase and acrylicThin select its variants; mica and micaAlt select Mica styles. Unsupported or accessibility-disabled materials use a solid fallback. Omit to keep the preference.");
            Describe(properties, "destination", "An allowlisted page or popup, never a raw URL. Opening a music detail does not automatically play it. Content destinations require a signed-in shell; login and register are account destinations. Welcome is an internal session-validation page and has no deep link.");
            Describe(properties, "publicId", "Nonempty public UUID in dashed D format. Required only for playlist, music-details, music-comment, client and story. For music-comment this identifies the music.");
            Describe(properties, "commentPublicId", "Nonempty public UUID in dashed D format, required only for music-comment.");
            Describe(properties, "searchQuery", "Search text of 1 to 120 characters without control characters, required only for search. Opens the existing in-app search.");
            Describe(properties, "settingsSection", "Optional section when destination is settings. Selects the existing settings section without changing any setting or permission.");
            Describe(properties, "section", "Music home section key. Refresh accepts all or any listed section. For actions, all supports set_language and dismiss; genres are display-only. Other actions must apply to the selected section and its current visible items.");
            Describe(properties, "action", "Music-home-only action. open/play/profile/copy_link use a current item id (optional for current featured/spotlight/news). pause/resume: featured/stories/news. previous/next: featured/spotlight/news/artists/stories. select: those sections or weekly, with index. mute/fullscreen: featured with enabled. seek/volume: featured with value. like: featured/stories, follow: spotlight/artists, each with enabled and submit permission. open_all: library/following; show_recent: library; add_story: stories; open_widget: stories with a visible widget id; request_download: featured; retry: featured/stories; reset_model: spotlight; set_language: all with language. profile on recommended/recent/trending may specify contributor index 0 or 1. Do not send unrelated optional fields.");
            Describe(properties, "id", "Opaque identifier copied from the current music home state, maximum 100 characters. Required for item open/play/profile/copy_link/follow/open_widget except the current featured/spotlight/news item. Never a file path, arbitrary URL or backend database key.");
            Describe(properties, "index", "Zero-based current-item index for select. For profile on a recommended/recent/trending music card, optionally choose contributor 0 or 1. Otherwise omit. The live page additionally validates bounds.");
            Describe(properties, "value", "Only for featured seek (seconds, 0 to actual duration) or featured volume (0 to 1). Omit for other actions.");
            Describe(properties, "enabled", "Required only for mute, fullscreen, like and follow. Sets the desired state explicitly instead of toggling. Like/follow require additional music home submit permission.");
            if (properties["form"] is JsonObject form)
            {
                form["description"] = "Fields to update on the visible form. Omitted fields stay unchanged; an empty string clears the field. Filling never submits. Use identifier/password for login and firstName/lastName/username/email/password/confirmPassword for registration.";
                var fields = form["properties"]!.AsObject();
                Describe(fields, "identifier", "Login identifier (username or email). Applies to the login form.");
                Describe(fields, "firstName", "Given name for the registration form.");
                Describe(fields, "lastName", "Family name for the registration form.");
                Describe(fields, "username", "Requested username for the registration form.");
                Describe(fields, "email", "Email address for the registration form.");
                Describe(fields, "password", "Write-only password for the selected form. Never returned in state, tool results or logs. Empty clears it.");
                Describe(fields, "confirmPassword", "Write-only password confirmation for registration. Never returned in state, tool results or logs. Empty clears it.");
                fields["password"]!["writeOnly"] = true;
                fields["confirmPassword"]!["writeOnly"] = true;
            }
        }
        return ToElement(schema);
    }

    private static JsonElement CreateOutput()
    {
        var schema = McpJsonContext.Default.McpResponse.GetJsonSchemaAsNode(new()
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                if (node is not JsonObject definition) return node;
                if (definition["properties"] is not null) definition["additionalProperties"] = false;
                if (context.PropertyInfo is { } property)
                {
                    var description = Description(property.Name);
                    if (description is not null) definition["description"] = description;
                    if (property.Name == "completionStage") definition["enum"] = new JsonArray("opened", "completed");
                }
                return definition;
            }
        }).AsObject();
        schema["description"] = "Result of a scoped Mizon tool. Always inspect success and code. connection describes only this MCP connection. Authentication, appearance, navigation, taskbar, music-home, presence, VPN and main-window state each require their own read permission. Passwords, tokens and cookies are never included. A pending, denied or revoked connection cannot bypass consent using another tool.";
        schema["required"] = new JsonArray("success", "code", "message");
        return ToElement(schema);
    }

    private static string? Description(string name)
    {
        // Property names are already the JSON (camelCase) names in metadata.
        return name switch
        {
            "success" => "True only when the requested operation completed successfully; false for validation, permission, unavailable-app or transport errors.",
            "code" => "Machine-readable result code. Treat unknown codes as errors when success is false; never automatically repeat a timed-out submission.",
            "message" => "Human-readable outcome or next step. Errors never echo submitted credentials.",
            "state" => "Optional current authentication UI state. Omitted without read permission; never infer permission from its absence or a cached previous result.",
            "connection" => "Optional current connection consent status and grants. These grants can only be changed by the user inside Mizon, not through MCP.",
            "id" => "Stable identifier within the reported feature. Music items use the exact opaque ID accepted by their listed actions; connection IDs identify permission entries. Never infer a backend integer ID or arbitrary navigation target.",
            "clientName" => "Unverified label supplied by the MCP client during initialization, not a verified publisher identity.",
            "status" => "Connection lifecycle: pending, approved, denied, revoked or disconnected. Only approved connections may use granted scopes.",
            "allowRead" => "User-approved permission to read authentication-page state. Does not expose password contents or tokens.",
            "allowControl" => "User-approved permission to navigate authentication pages, edit/clear fields, change language and retry welcome initialization.",
            "allowSubmit" => "User-approved permission to submit login or registration, additionally requiring control permission and an explicit user instruction.",
            "allowAppearanceRead" => "User-approved permission to read appearance preferences and effective window appearance. Independent of authentication scopes.",
            "allowAppearanceControl" => "User-approved permission to apply and save theme/window-material preferences. Does not authorize authentication or other settings.",
            "allowNavigationRead" => "User-approved permission to read the current app destination and pending deep link, including public IDs or search text in those links. Independent of authentication and appearance permissions.",
            "allowNavigationControl" => "User-approved permission to open declared pages/popups. Does not authorize forms, playback/download actions, editing settings or reading page contents.",
            "navigation" => "Optional navigation state, returned only with navigation read permission. Control-only permission receives an outcome without this state.",
            "navigationResult" => "Optional receipt for this completed navigation invocation, returned only with navigation read permission. Not retained by get_navigation. Distinguishes opening a destination from completing its requested operation; it contains no arbitrary backend data or local paths.",
            "requestId" => "Unique UUID correlating this invocation's result. Do not reuse a previous receipt as proof that a new navigation completed.",
            "completionStage" => "opened means the destination is open and content may still be loading. completed means the route's declared operation finished. Neither stage grants access to other page data or actions.",
            "destination" => "Current declared destination key, or unavailable when no supported page is attached.",
            "uri" => "Canonical mizon deep link for the current destination, when available. Never an external URL or local file path.",
            "pendingUri" => "Optional external deep link waiting for the authenticated shell. MCP content navigation is never deferred across a later sign-in.",
            "hasPendingDestination" => "Whether an external deep link is waiting to be opened.",
            "canGoBack" => "Whether the active navigation frame has a back-stack entry.",
            "isRemembered" => "True when the user saved this client's exact scopes in Mizon. These survive app/client restarts until revoked in Settings. Does not authorize new feature domains.",
            "appearance" => "Optional appearance state, present only with separate appearance read permission. A successful write with control-only permission returns no state.",
            "theme" => "Saved theme preference: system, light, or dark.",
            "effectiveTheme" => "Theme resolved for the currently attached window, accounting for the Windows preference when theme is system.",
            "backdrop" => "Saved window-material preference. This can differ from the material currently supported or displayed.",
            "effectiveBackdrop" => "Actual material applied to the window. Absent when a solid-color fallback is in use.",
            "fallbackReason" => "Why the selected material is not active: none, windowUnavailable, highContrast, transparencyDisabled, unsupported, or unavailable.",
            "isHighContrast" => "Whether Windows high-contrast accessibility mode is active.",
            "isTransparencyEnabled" => "Whether Windows permits transparency effects.",
            "isMicaSupported" => "Whether the current Windows composition environment supports Mica.",
            "isAcrylicSupported" => "Whether the current Windows composition environment supports Acrylic.",
            "isPreferenceSaved" => "Whether the current preference was successfully persisted. A false value means it may not survive a restart.",
            "isWindowAttached" => "Whether a live main window is attached to the appearance service.",
            "isConnected" => "For connection: whether this MCP client is connected; disconnect does not revoke saved consent. For presence: whether Mizon has a live SignalR presence connection; this alone does not confirm online status. For VPN: whether an actual tunnel is connected; currently false.",
            "page" => "Current page name: login, register, welcome, or an out-of-scope/unavailable page. Only authentication pages are exposed by these tools.",
            "language" => "Current Mizon language tag, fa-IR or en-US.",
            "hasSession" => "Whether a locally stored session/token is present. This is not proof that the server has verified it or that it remains valid.",
            "taskbarPin" => "Mizon taskbar pin status refreshed from Windows. Returned only with taskbar read permission.",
            "isPinned" => "Whether Mizon is currently pinned, never inferred from an old saved preference.",
            "isPinningAllowed" => "Whether the operating system currently permits requesting pinning.",
            "canRequest" => "Whether a pin request is currently available. Windows still requires foreground app and user confirmation.",
            "allowTaskbarRead" => "Independent permission to read Mizon's taskbar pin status.",
            "allowTaskbarControl" => "Independent permission to request Windows pin confirmation, never to approve it or unpin.",
            "allowPresenceRead" => "Independent user-approved permission to read your current online presence only. It cannot change presence or activate the app window.",
            "allowVpnRead" => "Independent permission to read bounded state from the active VPN page. Existing authentication, navigation and other grants do not authorize it.",
            "allowVpnControl" => "Independent permission to change VPN-page decorative particles and open Windows VPN settings. No tunnel connection, OS configuration or arbitrary URI operations.",
            "vpn" => "Optional active VPN page snapshot. Returned only with independent VPN read permission, including after control operations. No IP addresses, server identifiers or credentials.",
            "isConfigured" => "Whether this page has a configured VPN tunnel. Currently false; network availability is not a VPN connection.",
            "networkStatus" => "Current network availability category: internet, local, offline or unknown. Not a VPN connectivity guarantee or proof of Internet access.",
            "particlesEnabled" => "User preference for decorative VPN-page particles. Rendering also respects motionAllowed and page/window lifecycle.",
            "motionAllowed" => "Whether Windows currently permits UI animations.",
            "allowWindowRead" => "Independent permission to read main-window availability, fullscreen and control visibility. Existing appearance/music/navigation grants do not authorize it.",
            "allowWindowControl" => "Independent permission to enter or exit main-window fullscreen. Does not grant other window operations, state reads, theme changes or page data.",
            "window" => "Optional bounded main-window presentation snapshot, returned only with separate window read permission. Control-only requests never disclose this state.",
            "fullScreen" => "Whether the main application window currently uses fullscreen presentation. Meaningful only when isAvailable is true.",
            "controlsVisible" => "Whether the main window's fullscreen controls are currently shown. No pointer simulation or toolbar-hover action is exposed.",
            "presence" => "Optional current-user presence snapshot shared with the header/taskbar. Returned only with presence read permission. No identifiers, activity history or other users. Check hasConfirmedState before treating isOnline as server-confirmed.",
            "isWindowActive" => "Whether the main Mizon window is currently active. Reading does not activate it.",
            "isOnline" => "The current online indicator from the shared presence service; it is server-confirmed only when hasConfirmedState is true.",
            "hasConfirmedState" => "Whether the server confirmed the current presence state. False during unavailable, disconnected or pending synchronization states.",
            "allowMusicHomeRead" => "Independent permission to read the active music-home page and its bounded visible section contents.",
            "allowMusicHomeControl" => "Independent permission to refresh music home, use its existing navigation/playback controls and change language. Does not authorize likes or follows. Playback/viewing may record normal activity.",
            "allowMusicHomeSubmit" => "Additional permission for music-home like/follow actions; also requires music home control. Does not grant authentication submission or arbitrary backend writes.",
            "musicHome" => "Optional snapshot of the active music home. Omitted without music home read permission even when a control action succeeds. No local media paths or image bytes.",
            "sections" => "Bounded snapshots of the music home sections, with at most 20 items per section. Does not fetch all backend records.",
            "source" => "Data provenance: api for real service data, demo for local sample content, mixed when both occur. Demo contents and actions are never presented as persisted backend data.",
            "items" => "Only the bounded items currently represented by this home section. IDs are inputs for supported item actions; hasMore indicates remaining results.",
            "actions" => "Currently supported action names for this item, section or player. The live page revalidates availability and item identity before executing; stale state is not authorization.",
            "isAvailable" => "Whether the live window, page or item is currently available for the described action; authentication and consent are separate prerequisites.",
            "layout" => "Current responsive music-home layout, or unavailable if it is not attached.",
            "isDemo" => "True for sample data whose actions do not represent a real backend entity.",
            "canOpen" => "Whether this current card has a supported open action. Display-only and demo cards may be unavailable.",
            "imageAvailable" => "Whether the current card has an image, without returning image bytes or cache paths.",
            "hasMore" => "Whether more items exist beyond this bounded section snapshot.",
            "hero" => "Current featured media player snapshot, separate from the music-detail audio player.",
            "story" => "Current open music-home story viewer snapshot, when present.",
            "activeDialog" => "Name of a currently open music-home dialog, if one exists.",
            "activeStoryId" => "Public/opaque identifier of the currently open story, if present.",
            "activeArtistId" => "Current spotlight artist identifier; demo artist IDs are not real account IDs.",
            "activeNewsIndex" => "Current zero-based news slide index, when available.",
            "selectedDayIndex" => "Current zero-based weekly-rhythm day index, when available.",
            "isLiked" => "Whether the current user likes the reported item, based on the live page's state.",
            "canLike" => "Whether the item supports liking now; submit permission is still independently required.",
            "positionSeconds" => "Playback position in seconds for this player or story.",
            "durationSeconds" => "Known duration in seconds, zero or absent when unavailable.",
            "volume" => "Featured-player volume from 0 to 1, independent of mute.",
            "isLoading" => "Whether this section or player currently awaits data/media.",
            "isFullscreen" => "Whether the featured player is currently in its full-screen presentation.",
            "canSeek" => "Whether the featured player has seekable media and a known duration.",
            "isBusy" => "Whether the reported feature has an operation in progress.",
            "canSubmit" => "Whether current form validation allows submission now. Connection submit permission is a separate requirement.",
            "controlsEnabled" => "Whether current-page controls accept user interaction now.",
            "hasError" => "Whether the current authentication page shows an error.",
            "errorCode" => "Optional application error code; inspect this before choosing a next step.",
            "errorMessage" => "Optional visible, sanitized authentication error. Never contains passwords or access tokens.",
            "identifier" => "Optional login identifier currently in the form, available only with read permission.",
            "firstName" => "Optional registration first name, available only with read permission.",
            "lastName" => "Optional registration last name, available only with read permission.",
            "username" => "Optional registration username, available only with read permission.",
            "email" => "Optional registration email address, available only with read permission.",
            "hasPassword" => "Only indicates that a password field is nonempty. Password contents and length are never returned.",
            "hasConfirmPassword" => "Only indicates that registration confirmation is nonempty. Its contents and length are never returned.",
            "welcomeStage" => "Optional current welcome initialization stage.",
            "statusTitle" => "Optional currently visible welcome status heading.",
            "statusDetail" => "Optional currently visible welcome status detail.",
            "displayName" => "Optional welcome profile display name, available only with read permission.",
            "hasProfileImage" => "Whether welcome has a profile image; image bytes, local paths and credentials are not returned.",
            _ => null
        };
    }

    private static void Describe(JsonObject properties, string name, string text)
    {
        if (properties[name] is JsonObject property && property["description"] is null) property["description"] = text;
    }

    private static JsonElement ToElement(JsonNode schema)
    {
        using var document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }
}
