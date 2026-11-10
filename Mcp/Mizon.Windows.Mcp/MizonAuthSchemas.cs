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
            Describe(properties, "backdrop", "Saved main-window material: acrylic uses the default Acrylic style; acrylicBase and acrylicThin select its variants; mica and micaAlt select Mica styles. Unsupported or accessibility-disabled materials use a solid fallback. Omit to keep the preference.");
            Describe(properties, "destination", "An allowlisted page or popup, never a raw URL. Opening a music detail does not automatically play it. Content destinations require a signed-in shell; login and register are account destinations. Welcome is an internal session-validation page and has no deep link.");
            Describe(properties, "publicId", "Nonempty public UUID in dashed D format. Required only for playlist, music-details, music-comment, client and story. For music-comment this identifies the music.");
            Describe(properties, "commentPublicId", "Nonempty public UUID in dashed D format, required only for music-comment.");
            Describe(properties, "searchQuery", "Search text of 1 to 120 characters without control characters, required only for search. Opens the existing in-app search.");
            Describe(properties, "settingsSection", "Optional section when destination is settings. Selects the existing settings section without changing any setting or permission.");
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
                }
                return definition;
            }
        }).AsObject();
        schema["description"] = "Result of a scoped Mizon tool. Always inspect success and code. connection describes only this MCP connection. Authentication, appearance and navigation state each require their own read permission. Passwords, tokens and cookies are never included. A pending, denied or revoked connection cannot bypass consent using another tool.";
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
            "id" => "Identifier of this client permission entry. A paired client retains the identifier across reconnects; legacy clients without proof have a session-only identifier.",
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
            "isConnected" => "Whether this client currently has a live connection. Disconnecting does not revoke remembered approval. Disabling MCP suspends access without deleting saved grants.",
            "page" => "Current page name: login, register, welcome, or an out-of-scope/unavailable page. Only authentication pages are exposed by these tools.",
            "language" => "Current Mizon language tag, fa-IR or en-US.",
            "hasSession" => "Whether a locally stored session/token is present. This is not proof that the server has verified it or that it remains valid.",
            "taskbarPin" => "Mizon taskbar pin status refreshed from Windows. Returned only with taskbar read permission.",
            "isPinned" => "Whether Mizon is currently pinned, never inferred from an old saved preference.",
            "isPinningAllowed" => "Whether the operating system currently permits requesting pinning.",
            "canRequest" => "Whether a pin request is currently available. Windows still requires foreground app and user confirmation.",
            "allowTaskbarRead" => "Independent permission to read Mizon's taskbar pin status.",
            "allowTaskbarControl" => "Independent permission to request Windows pin confirmation, never to approve it or unpin.",
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
        if (properties[name] is JsonObject property) property["description"] = text;
    }

    private static JsonElement ToElement(JsonNode schema)
    {
        using var document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }
}
