using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mizon.Windows.Pages;
using Windows.UI.StartScreen;

namespace Mizon.Windows.Services;

public sealed class JumpListService
{
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private int _revision;

    public static IReadOnlyList<JumpListEntry> MainEntries { get; } =
    [
        new("music", "موسیقی", "آهنگ‌ها، موزیک‌ویدیوها و پلی‌لیست‌ها", "Headphones"),
        new("movies", "فیلم و سریال", "بخش فیلم‌ها و سریال‌ها", "Video"),
        new("apps", "برنامه‌ها", "بخش برنامه‌ها", "Apps"),
        new("vpn", "VPN", "اتصال و مدیریت VPN", "Shield")
    ];
    public static IReadOnlyList<JumpListEntry> AccountEntries { get; } =
    [
        new("login", "ورود به حساب", "ورود به میزون", "Person"),
        new("register", "ساخت حساب", "ساخت حساب در میزون", "PersonAdd")
    ];

    public JumpListService(NavigationService navigationService)
    {
        navigationService.MainNavigationChanged += (_, args) =>
        {
            if (!args.IsEnteringPage) return;
            if (args.ToPageType == typeof(HomePage)) _ = ConfigureAsync(true);
            else if (args.ToPageType == typeof(LoginPage)) _ = ConfigureAsync(false);
        };
    }

    public Task ConfigureLoginRegisterJumpListAsync() => ConfigureAsync(false);

    public async Task ConfigureAsync(bool signedIn)
    {
        var revision = Interlocked.Increment(ref _revision);
        await _updateGate.WaitAsync();
        try
        {
            if (revision != _revision || !JumpList.IsSupported()) return;
            var list = await JumpList.LoadCurrentAsync();
            if (revision != _revision) return;
            var removed = list.Items.Where(item => item.RemovedByUser).Select(item => item.Arguments).ToHashSet(StringComparer.Ordinal);
            list.SystemGroupKind = JumpListSystemGroupKind.None;
            list.Items.Clear();
            foreach (var entry in signedIn ? MainEntries : AccountEntries)
            {
                if (removed.Contains(entry.Arguments)) continue;
                var item = JumpListItem.CreateWithArguments(entry.Arguments, entry.Title);
                item.Description = entry.Description;
                item.GroupName = signedIn ? "بخش‌های میزون" : "حساب میزون";
                item.Logo = new Uri(entry.LogoUri);
                list.Items.Add(item);
            }
            await list.SaveAsync();
            AppLog.Information($"Jump list updated: signedIn={signedIn}, count={list.Items.Count}.");
        }
        catch (Exception exception)
        {
            // Shell integration must not block login or logout.
            AppLog.Error("Unable to update the Mizon jump list.", exception);
        }
        finally { _updateGate.Release(); }
    }

    public static string? GetRequestedDestination(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        foreach (var token in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var argument = token.Trim('"');
            foreach (var entry in MainEntries)
                if (string.Equals(argument, entry.Arguments, StringComparison.Ordinal)) return entry.Destination;
            foreach (var entry in AccountEntries)
                if (string.Equals(argument, entry.Arguments, StringComparison.Ordinal)) return entry.Destination;
        }
        return null;
    }
}

public sealed record JumpListEntry(string Destination, string Title, string Description, string Icon)
{
    public string Arguments => $"--mizon-destination={Destination}";
    public string LogoUri => $"ms-appx:///Assets/JumpList/FullSize/{Icon}.png";
}
