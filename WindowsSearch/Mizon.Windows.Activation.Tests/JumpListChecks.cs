using System.ComponentModel;
using System.Xml.Linq;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using Windows.UI.StartScreen;

internal static class JumpListChecks
{
    internal static async Task RunAsync()
    {
        var localization = LocalizationService.Current;
        var navigation = new NavigationService();
        using var service = new JumpListService(navigation);

        navigation.Enter(typeof(WelcomePage));
        Require(JumpList.Last.Items.Count == 0, "Welcome does not contribute a jump-list entry");
        navigation.Enter(typeof(LoginPage));
        Require(JumpList.Last.Items.Count == 2 && JumpList.Last.Items[0].DisplayName == "ورود به حساب"
            && JumpList.Last.Items[0].GroupName == "حساب میزون", "Persian sign-in tasks");

        var login = JumpList.Last.Items[0];
        JumpList.Last.Items[1].RemovedByUser = true;
        localization.SetLanguage("en-US");
        Require(JumpList.Last.Items.Count == 1 && JumpList.Last.Items[0].DisplayName == "Sign in"
            && JumpList.Last.Items[0].Description == "Sign in to Mizon" && JumpList.Last.Items[0].GroupName == "Mizon account",
            "language-only refresh updates labels and respects removal");
        Require(login.Arguments == JumpList.Last.Items[0].Arguments && login.Logo == JumpList.Last.Items[0].Logo,
            "locale changes preserve the activation argument and artwork");

        var stalledLoad = new TaskCompletionSource<JumpList>();
        JumpList.PendingLoad = stalledLoad.Task;
        navigation.Enter(typeof(HomePage));
        localization.SetLanguage("fa-IR");
        var latest = service.ConfigureAsync(true);
        stalledLoad.SetResult(JumpList.Last);
        await latest;
        Require(JumpList.Last.Items.Count == 4 && JumpList.Last.Items[0].DisplayName == "موسیقی"
            && JumpList.Last.Items[0].GroupName == "بخش‌های میزون", "only current locale and signed-in navigation survive a pending refresh");
        foreach (var item in JumpList.Last.Items)
            Require(JumpListService.GetRequestedDestination(item.Arguments) is "music" or "movies" or "apps" or "vpn",
                "localized entries still activate the same known destination");

        service.Dispose();
        var previous = JumpList.Last.Items[0];
        localization.SetLanguage("en-US");
        navigation.Enter(typeof(LoginPage));
        Require(ReferenceEquals(JumpList.Last.Items[0], previous), "closing the app unhooks locale and navigation");
        Require(AppLanguagePreference.Normalize("EN_us") == "en-US"
            && AppLanguagePreference.Normalize("fa") == "fa-IR"
            && AppLanguagePreference.Normalize("de-DE") is null, "shell and app agree on canonical language tags");
        Console.WriteLine("PASS Jump List: Persian and English resources, activation, removed items, rapid changes and unsubscribe.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

namespace Mizon.Windows.Pages
{
    internal sealed class HomePage;
    internal sealed class LoginPage;
    internal sealed class WelcomePage;
}

namespace Mizon.Windows.Services
{
    public sealed class NavigationService
    {
        public event EventHandler<NavigationChangedEventArgs>? MainNavigationChanged;
        internal void Enter(Type page) => MainNavigationChanged?.Invoke(this, new(page));
    }

    public sealed class NavigationChangedEventArgs(Type page) : EventArgs
    {
        public bool IsEnteringPage => true;
        public Type ToPageType => page;
    }

    internal sealed class LocalizationService
    {
        private readonly Dictionary<string, Dictionary<string, string>> _resources = new(StringComparer.Ordinal);
        internal static LocalizationService Current { get; } = new();
        internal event PropertyChangedEventHandler? PropertyChanged;
        internal string LanguageTag { get; private set; } = "fa-IR";

        internal void SetLanguage(string language)
        {
            LanguageTag = language;
            PropertyChanged?.Invoke(this, new(nameof(LanguageTag)));
        }

        internal string GetString(string key)
        {
            if (!_resources.TryGetValue(LanguageTag, out var resources))
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", LanguageTag, "JumpList.resw");
                resources = XDocument.Load(path).Root!.Elements("data")
                    .ToDictionary(item => "JumpList/" + item.Attribute("name")!.Value, item => item.Element("value")!.Value);
                _resources.Add(LanguageTag, resources);
            }
            return resources[key];
        }
    }
}

namespace Mizon.Windows
{
    internal static class AppLog
    {
        internal static void Information(string message) { }
        internal static void Error(string message, Exception error) => throw new InvalidOperationException(message, error);
    }
}

namespace Windows.UI.StartScreen
{
    internal enum JumpListSystemGroupKind { None }

    internal sealed class JumpList
    {
        internal static JumpList Last { get; } = new();
        internal static Task<JumpList>? PendingLoad { get; set; }
        internal List<JumpListItem> Items { get; } = [];
        internal JumpListSystemGroupKind SystemGroupKind { get; set; }
        internal static bool IsSupported() => true;
        internal Task SaveAsync() => Task.CompletedTask;
        internal static Task<JumpList> LoadCurrentAsync()
        {
            var load = PendingLoad ?? Task.FromResult(Last);
            PendingLoad = null;
            return load;
        }
    }

    internal sealed class JumpListItem(string arguments, string displayName)
    {
        internal string Arguments { get; } = arguments;
        internal string DisplayName { get; } = displayName;
        internal string? Description { get; set; }
        internal string? GroupName { get; set; }
        internal Uri? Logo { get; set; }
        internal bool RemovedByUser { get; set; }
        internal static JumpListItem CreateWithArguments(string arguments, string title) => new(arguments, title);
    }
}
