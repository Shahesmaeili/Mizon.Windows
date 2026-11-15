using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Mizon.Windows.Pages;
using Windows.UI.StartScreen;

namespace Mizon.Windows.Services;

// Updates and event subscriptions are owned by the app's UI thread.
public sealed class JumpListService : IDisposable
{
    private static readonly JumpListEntry[] MainEntries =
    [
        new("music", "Music", "Headphones"),
        new("movies", "Movies", "Video"),
        new("apps", "Apps", "Apps"),
        new("vpn", "Vpn", "Shield")
    ];
    private static readonly JumpListEntry[] AccountEntries =
    [
        new("login", "Login", "Person"),
        new("register", "Register", "PersonAdd")
    ];

    private readonly NavigationService _navigation;
    private readonly LocalizationService _localization = LocalizationService.Current;
    private Task _pendingUpdate = Task.CompletedTask;
    private int _revision;
    private bool? _signedIn;
    private bool _disposed;

    public JumpListService(NavigationService navigationService)
    {
        _navigation = navigationService;
        _navigation.MainNavigationChanged += OnNavigation;
        _localization.PropertyChanged += OnLanguageChanged;
    }

    private void OnNavigation(object? sender, NavigationChangedEventArgs args)
    {
        if (!args.IsEnteringPage) return;
        if (args.ToPageType == typeof(HomePage)) _ = ConfigureAsync(true);
        else if (args.ToPageType == typeof(LoginPage)) _ = ConfigureAsync(false);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.LanguageTag) && _signedIn is bool signedIn)
            _ = ConfigureAsync(signedIn);
    }

    public Task ConfigureLoginRegisterJumpListAsync() => ConfigureAsync(false);

    public Task ConfigureAsync(bool signedIn)
    {
        if (_disposed) return Task.CompletedTask;
        _signedIn = signedIn;
        return _pendingUpdate = UpdateAsync(_pendingUpdate, ++_revision, signedIn);
    }

    private async Task UpdateAsync(Task previous, int revision, bool signedIn)
    {
        try
        {
            await previous;
            if (_disposed || revision != _revision || !JumpList.IsSupported()) return;
            var list = await JumpList.LoadCurrentAsync();
            if (_disposed || revision != _revision) return;

            var removed = list.Items.Where(item => item.RemovedByUser).Select(item => item.Arguments).ToHashSet(StringComparer.Ordinal);
            list.SystemGroupKind = JumpListSystemGroupKind.None;
            list.Items.Clear();
            var group = _localization.GetString(signedIn ? "JumpList/MainGroup" : "JumpList/AccountGroup");
            foreach (var entry in signedIn ? MainEntries : AccountEntries)
            {
                if (removed.Contains(entry.Arguments)) continue;
                var item = JumpListItem.CreateWithArguments(entry.Arguments, _localization.GetString($"JumpList/{entry.ResourceKey}Title"));
                item.Description = _localization.GetString($"JumpList/{entry.ResourceKey}Description");
                item.GroupName = group;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _navigation.MainNavigationChanged -= OnNavigation;
        _localization.PropertyChanged -= OnLanguageChanged;
    }

    private sealed record JumpListEntry(string Destination, string ResourceKey, string Icon)
    {
        public string Arguments => $"--mizon-destination={Destination}";
        public string LogoUri => $"ms-appx:///Assets/JumpList/FullSize/{Icon}.png";
    }
}
