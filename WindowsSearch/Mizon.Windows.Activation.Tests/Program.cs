using Mizon.Windows.Services;
using System.Text.Json.Nodes;
using System.Xml.Linq;

var id = Guid.Parse("0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72");
var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    count++;
    Console.WriteLine("PASS " + name);
}
AppActivationRequest Parse(string value) => AppActivationRequest.FromUri(new Uri(value));
foreach (var root in new[] { "mizon://music/", "mizon://localhost/music/", "mizon://S-1-5-21-123-1001/localhost/music/" })
{
    Check(Parse(root + id) == new AppActivationRequest(MusicPublicId: id), "existing music URI keeps details default");
    Check(Parse(root + id + "?action=play") == new AppActivationRequest(MusicPublicId: id, MusicAction: MusicActivationAction.Play), "explicit play is preserved");
    Check(Parse(root + id + "?action=download") == new AppActivationRequest(MusicPublicId: id, MusicAction: MusicActivationAction.Download), "explicit download is preserved");
}
Check(Parse($"mizon://music/{id}?action=details").MusicAction == MusicActivationAction.Details, "explicit details action");
Check(Parse($"MIZON://music/{id}?ACTION=PLAY").MusicAction == MusicActivationAction.Play, "allowlisted action is case insensitive");
Check(Parse($"mizon://client/{id}").ClientPublicId == id, "client public id route");
Check(Parse($"mizon://playlist/{id}").PlaylistPublicId == id, "existing playlist public id route");
foreach (var destination in new[] { "music", "movies", "apps", "vpn", "login", "register" })
    Check(Parse("mizon://" + destination).Destination == destination, "existing section " + destination);
foreach (var destination in new[] { "login", "register" })
{
    foreach (var prefix in new[] { "mizon://", "mizon://localhost/", "mizon://S-1-5-21-123-1001/localhost/" })
        Check(Parse(prefix + destination) == new AppActivationRequest(Destination: destination), "account route only selects a page " + prefix + destination);
    Check(Parse("MIZON://" + destination.ToUpperInvariant()) == new AppActivationRequest(Destination: destination), "account route is case insensitive " + destination);
    foreach (var invalid in new[]
    {
        $"mizon://{destination}?username=someone&password=secret", $"mizon://{destination}?token=secret",
        $"mizon://{destination}?redirect=https://example.com", $"mizon://{destination}?action=play",
        $"mizon://person:secret@{destination}", $"mizon://{destination}:42", $"mizon://{destination}#token",
        $"mizon://attacker/{destination}", $"mizon://S-1-5-21-123-1001/attacker/{destination}",
        $"mizon://{destination}/extra", $"mizon://{destination}/{id}", $"https://{destination}"
    }) Check(Parse(invalid) == new AppActivationRequest(), "reject untrusted account route " + invalid);
}
foreach (var invalid in new[]
{
    $"https://music/{id}", $"mizon://attacker/music/{id}", $"mizon://music/{id}/play",
    $"mizon://music/{id}?action=delete", $"mizon://music/{id}?action=play&action=download",
    $"mizon://music/{id}?url=https://example.com", $"mizon://music/{id}?action=play&path=C:/file",
    $"mizon://music/{id}#play", $"mizon://person@music/{id}", $"mizon://music:42/{id}",
    "mizon://music/not-a-guid", $"mizon://music/{Guid.Empty}", $"mizon://client/{id}?action=play",
    $"mizon://playlist/{id}?action=download", "mizon://music?action=play"
}) Check(Parse(invalid) == new AppActivationRequest(), "reject malformed/unsupported route " + invalid);
Check(AppActivationRequest.FromUri(new Uri("relative", UriKind.Relative)) == new AppActivationRequest(), "reject relative URI");
foreach (var query in new[] { "aflaskjflajsvlakjsv", "معین و شادمهر", "a+b & c?x=1/#% 🎵" })
    Check(Parse("mizon://search?query=" + Uri.EscapeDataString(query)).SearchQuery == query, "search activation preserves encoded query " + query);
Check(Parse("mizon://search?query=hello+world").SearchQuery == "hello world", "search accepts URL form spaces");
foreach (var invalid in new[] { "mizon://search", "mizon://search?query=", "mizon://search?query=%20", "mizon://search?query=a&query=b", "mizon://search?query=a&action=play", "mizon://search/extra?query=a", "mizon://search?query=a%00b", "mizon://search?query=" + new string('a', 121) })
    Check(Parse(invalid) == new AppActivationRequest(), "reject malformed search route " + invalid);

DeepLinkRouteTests.Run(Check);

// Fixtures are copied from the package sources, so these checks work from any working directory.
var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
var manifest = XDocument.Load(Path.Combine(fixtures, "Package.appxmanifest"));
XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
XNamespace uap3 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3";
var application = manifest.Root!.Element(foundation + "Applications")!.Element(foundation + "Application")!;
var extensions = application.Element(foundation + "Extensions")!;
XNamespace com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
XNamespace com2 = "http://schemas.microsoft.com/appx/manifest/com/windows10/2";
XNamespace com3 = "http://schemas.microsoft.com/appx/manifest/com/windows10/3";
var actionServer = extensions.Elements(com2 + "Extension")
    .Elements(com2 + "ComServer").Elements(com3 + "ExeServer").Single();
Check((string?)actionServer.Attribute("Executable") == "Mizon.Windows.exe", "COM actions run in the main app executable");
Check((string?)actionServer.Attribute("Arguments") == "--app-actions", "COM activation is distinguishable from normal navigation");
Check(Guid.Parse((string)actionServer.Element(com + "Class")!.Attribute("Id")!) == Guid.Parse(AppActionCatalog.ProviderClsid),
    "package COM activation matches the runtime provider CLSID");
Check(manifest.Root.GetNamespaceOfPrefix("uap3") == uap3
    && ((string?)manifest.Root.Attribute("IgnorableNamespaces"))!.Split(' ').Contains("uap3"), "manifest declares ignorable uap3 namespace");
Check(extensions.Elements(uap + "Extension").Any(extension =>
    (string?)extension.Attribute("Category") == "windows.protocol"
    && (string?)extension.Element(uap + "Protocol")?.Attribute("Name") == "mizon"), "manifest preserves mizon protocol registration");
var actionExtensions = extensions.Elements(uap3 + "Extension")
    .Where(extension => (string?)extension.Attribute("Category") == "windows.appExtension")
    .Elements(uap3 + "AppExtension")
    .Where(extension => (string?)extension.Attribute("Name") == "com.microsoft.windows.ai.actions").ToArray();
Check(actionExtensions.Length == 1, "manifest registers one Windows App Actions provider");
var provider = actionExtensions[0];
Check((string?)provider.Attribute("PublicFolder") == "Assets", "App Actions public folder is Assets");
var registrationElement = provider.Element(uap3 + "Properties")?.Element(XName.Get("Registration"));
Check(registrationElement?.Name.Namespace == XNamespace.None
    && registrationElement.Value == @"AppActions\registration.json", "registration uses empty XML namespace and path relative to public folder");
var registrationPath = Path.Combine(fixtures, (string)provider.Attribute("PublicFolder")!,
    registrationElement!.Value.Replace('\\', Path.DirectorySeparatorChar));
Check(File.Exists(registrationPath), "manifest resolves to packaged registration file");
var registration = JsonNode.Parse(File.ReadAllText(registrationPath))!.AsObject();
Check(registration["version"]!.GetValue<int>() == 3, "App Actions schema version is 3");
var actions = registration["actions"]!.AsArray();
var expectedDestinations = new Dictionary<string, (string Destination, string Icon, bool RequiresSignIn)>
{
    ["Mizon.OpenLogin"] = ("login", "Person", false),
    ["Mizon.OpenRegister"] = ("register", "PersonAdd", false),
    ["Mizon.OpenMusic"] = ("music", "Headphones", true),
    ["Mizon.OpenMovies"] = ("movies", "Video", true),
    ["Mizon.OpenApps"] = ("apps", "Apps", true),
    ["Mizon.OpenVpn"] = ("vpn", "Shield", true)
};
var expectedActions = expectedDestinations.Keys.Concat(expectedDestinations.Keys.Select(key => key + ".English"))
    .ToHashSet(StringComparer.Ordinal);
Check(actions.Count == 12 && actions.Count == expectedActions.Count, "registration exposes six destinations in two languages");
Check(AppActionCatalog.Entries.Count == actions.Count, "runtime catalog and registration contain the same number of actions");
Check(!AppActionCatalog.TryResolve("Mizon.OpenWelcome", out _) && !AppActionCatalog.TryResolve("Mizon.OpenWelcome.English", out _),
    "welcome is excluded from App Actions and stays an internal session-validation page");
foreach (var unknownId in new string?[] { null, "", "Mizon.OpenLogin.Unknown", "mizon.openlogin", "Mizon.OpenMusic/extra" })
    Check(!AppActionCatalog.TryResolve(unknownId, out var missing) && missing is null, "catalog rejects unknown action ID " + unknownId);

var actionIds = new HashSet<string>(StringComparer.Ordinal);
foreach (var node in actions)
{
    var action = node!.AsObject();
    var actionId = action["id"]!.GetValue<string>();
    Check(actionIds.Add(actionId) && expectedActions.Contains(actionId), "action has a unique expected ID " + actionId);
    Check(AppActionCatalog.TryResolve(actionId, out var entry), "registered action resolves in runtime catalog " + actionId);
    var english = actionId.EndsWith(".English", StringComparison.Ordinal);
    var baseId = english ? actionId[..^".English".Length] : actionId;
    var expected = expectedDestinations[baseId];
    Check(entry!.Destination == expected.Destination && entry.RequiresSignIn == expected.RequiresSignIn
        && entry.LanguageTag == (english ? "en-US" : "fa-IR"), "catalog metadata matches action destination and language " + actionId);
    var description = action["description"]!.GetValue<string>();
    Check(!string.IsNullOrWhiteSpace(description) && !description.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
        && description.Any(character => character is >= '\u0600' and <= '\u06FF') == !english,
        "action text has a fixed language independent of Windows preferences " + actionId);
    Check(action["displaysUI"]!.GetValue<bool>() && !action["usesGenerativeAI"]!.GetValue<bool>(),
        "action shows UI without generative AI " + actionId);
    Check(action["isAvailable"]!.GetValue<bool>() == (!english && !expected.RequiresSignIn),
        "installation defaults to Persian signed-out account actions " + actionId);
    Check(action["isAvailable"]!.GetValue<bool>() == AppActionCatalog.IsAvailable(entry, false, "fa-IR"),
        "initial registration agrees with runtime availability " + actionId);
    var invokers = action["allowedAppInvokers"]!.AsArray();
    Check(invokers.Count == 1 && invokers[0]!.GetValue<string>() == "*", "action allows all app invokers " + actionId);
    Check(action["inputs"]!.AsArray().Count == 0 && !action.ContainsKey("outputs"), "page action takes no input and returns no output " + actionId);
    var combinations = action["inputCombinations"]!.AsArray();
    Check(combinations.Count == 1 && combinations[0]!["inputs"]!.AsArray().Count == 0
        && !string.IsNullOrWhiteSpace(combinations[0]!["description"]?.GetValue<string>()), "action describes its empty input combination " + actionId);
    Check(combinations[0]!["description"]!.GetValue<string>().Any(character => character is >= '\u0600' and <= '\u06FF') == !english,
        "input combination uses the action language " + actionId);
    var invocation = action["invocation"]!.AsObject();
    Check(invocation["type"]!.GetValue<string>() == "COM"
        && Guid.Parse(invocation["clsid"]!.GetValue<string>()) == Guid.Parse(AppActionCatalog.ProviderClsid)
        && !invocation.ContainsKey("uri"), "action invokes the stable COM provider " + actionId);
    Check(Parse("mizon://" + entry.Destination) == new AppActivationRequest(Destination: expected.Destination),
        "COM destination matches the existing allowlisted page-only deep link " + actionId);
    var icon = action["icon"]!.GetValue<string>();
    var iconPath = $"Assets/JumpList/FullSize/{expected.Icon}.png";
    Check(icon == "ms-resource://Files/" + iconPath, "action uses the matching jump-list icon " + actionId);
    Check(File.Exists(Path.Combine(fixtures, iconPath.Replace('/', Path.DirectorySeparatorChar))), "action icon exists in packaged sources " + actionId);
}
Check(actionIds.SetEquals(AppActionCatalog.Entries.Select(entry => entry.Id)), "every runtime action has exactly one packaged definition");

foreach (var language in new[] { "fa-IR", "en-US" })
{
    foreach (var signedIn in new[] { false, true })
    {
        var suffix = language == "en-US" ? ".English" : "";
        var expected = expectedDestinations.Where(pair => pair.Value.RequiresSignIn == signedIn)
            .Select(pair => pair.Key + suffix).ToHashSet(StringComparer.Ordinal);
        var actual = AppActionCatalog.Entries.Where(entry => AppActionCatalog.IsAvailable(entry, signedIn, language))
            .Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        Check(actual.SetEquals(expected), $"exact action set follows language={language}, signedIn={signedIn}");
        Check(actual.Count == (signedIn ? 4 : 2), $"only the intended destinations are available for language={language}, signedIn={signedIn}");
    }
}
Check(AppActionCatalog.Entries.All(entry => !AppActionCatalog.IsAvailable(entry, true, "fr-FR")
    && !AppActionCatalog.IsAvailable(entry, false, "fr-FR")), "unsupported language never exposes an unrelated variant");
Check(AppActionCatalog.Entries.Where(entry => AppActionCatalog.IsAvailable(entry, false, "FA-ir"))
    .Select(entry => entry.Id).ToHashSet().SetEquals(new[] { "Mizon.OpenLogin", "Mizon.OpenRegister" }),
    "language tags compare without casing differences");
Console.WriteLine($"PASS {count} activation-route and App Actions assertions. No app launch, registry changes or network calls performed.");
