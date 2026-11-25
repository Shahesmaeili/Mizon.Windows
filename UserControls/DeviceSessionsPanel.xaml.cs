using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.UserControls;

public sealed record DeviceSessionRow(ClientSessionItemResponse Session, string Glyph, SvgImageSource? Logo, string Title,
    string VersionLabel, string StatusLabel)
{
    public Visibility DeviceGlyphVisibility => Logo is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OnlineIndicatorVisibility => Session.IsOnline ? Visibility.Visible : Visibility.Collapsed;
    public override string ToString() => $"{Title}, {VersionLabel}, {StatusLabel}";
}

public sealed partial class DeviceSessionsPanel : UserControl
{
    private static readonly int[] InactivityChoices = [0, 7, 30, 90, 180, 365];
    private SvgImageSource? _windowsLogo;
    private SvgImageSource? _appleLogo;
    private SvgImageSource? _androidLogo;
    private readonly MizonApi _api;
    private readonly Lazy<MizonSocket> _socket;
    private CancellationTokenSource? _lifetime;
    private bool _busy;
    private bool _refreshPending;
    private bool _settingInactivity;
    private bool _showingDialog;
    private ContentDialog? _dialog;
    private int _days;

    public DeviceSessionsPanel()
    {
        var services = ((App)Application.Current).Services;
        _api = services.GetRequiredService<MizonApi>();
        _socket = services.GetRequiredService<Lazy<MizonSocket>>();
        InitializeComponent();
    }

    public LocalizationService Localization => LocalizationService.Current;
    public string Text(string key, int revision) => Localization.GetString("Settings/" + key, revision);
    private string Text(string key) => Text(key, Localization.Revision);
    private CultureInfo Culture => CultureInfo.GetCultureInfo(Localization.LanguageTag);

    private async void Panel_Loaded(object sender, RoutedEventArgs args)
    {
        if (_lifetime is not null) return;
        _lifetime = new();
        _socket.Value.SessionsChanged += SessionsChanged;
        Localization.PropertyChanged += LocalizationChanged;
        await RefreshAsync();
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs args)
    {
        if (_lifetime is not { } lifetime) return;
        _socket.Value.SessionsChanged -= SessionsChanged;
        Localization.PropertyChanged -= LocalizationChanged;
        _dialog?.Hide();
        _lifetime = null;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private void SessionsChanged(object? sender, EventArgs args) =>
        DispatcherQueue.TryEnqueue(async () => await RefreshAsync());

    private void LocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.Revision))
            DispatcherQueue.TryEnqueue(async () => await RefreshAsync());
    }

    private async void Refresh_Click(object sender, RoutedEventArgs args) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_lifetime is not { } lifetime) return;
        if (_busy)
        {
            _refreshPending = true;
            return;
        }

        _busy = true;
        Loading.IsActive = true;
        Loading.Visibility = Visibility.Visible;
        try
        {
            do
            {
                _refreshPending = false;
                var response = await _api.SendRequestAsync(new ClientSessionsOperation(new()), lifetime.Token);
                if (!ReferenceEquals(_lifetime, lifetime)) return;
                if (!response.IsSuccess || response.Content is not { } content)
                {
                    ShowError(response.Error?.Code);
                    return;
                }

                var current = content.Sessions.FirstOrDefault(item => item.IsCurrent);
                var others = content.Sessions.Where(item => !item.IsCurrent).Select(ToRow).ToArray();
                CurrentDevice.Content = current is null ? null : ToRow(current);
                DeviceList.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
                OtherDevices.ItemsSource = others;
                OtherDevices.Visibility = others.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                NoOthers.Visibility = others.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RevokeOthers.IsEnabled = others.Length > 0;
                DeviceActions.Visibility = Visibility.Visible;
                _days = content.InactivityDays;
                SetInactivity();
            }
            while (_refreshPending && ReferenceEquals(_lifetime, lifetime));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Could not list account sessions.", error); ShowError(); }
        finally
        {
            _busy = false;
            Loading.IsActive = false;
            Loading.Visibility = Visibility.Collapsed;
            if (_lifetime is not null && !ReferenceEquals(_lifetime, lifetime))
                await RefreshAsync();
        }
    }

    private void SetInactivity()
    {
        _settingInactivity = true;
        try
        {
            Inactivity.ItemsSource = InactivityChoices.Select(days => days == 0
                ? Text("DevicesNever") : string.Format(Culture, Text("DevicesDays"), days)).ToArray();
            Inactivity.SelectedIndex = Array.IndexOf(InactivityChoices, _days);
        }
        finally { _settingInactivity = false; }
    }

    private DeviceSessionRow ToRow(ClientSessionItemResponse session)
    {
        var platform = session.Platform.ToLowerInvariant();
        var logo = platform switch
        {
            "windows" => _windowsLogo ??= LoadLogo("windows"),
            "ios" or "ipados" or "iphone" or "ipad" or "mac" or "macos" or "apple watch" or "watchos" =>
                _appleLogo ??= LoadLogo("apple"),
            "android" or "android tv" or "google tv" or "wear os" or "wearos" =>
                _androidLogo ??= LoadLogo("android2"),
            _ => null,
        };
        var glyph = platform.Contains("web", StringComparison.Ordinal) || platform.Contains("browser", StringComparison.Ordinal)
            ? "\uE774" : "\uE7F4";
        var title = string.IsNullOrWhiteSpace(session.DeviceName) ? session.Platform : session.DeviceName;
        var status = session.IsOnline ? Text("DevicesOnline") :
            $"{Text("DevicesOffline")} · {string.Format(Culture, Text("DevicesLastSeen"), DateLabel(session.LastActiveAtUtc))}";
        if (!session.IsCurrent && session.IsUnconfirmed) status = $"{Text("DevicesUnconfirmed")} · {status}";
        return new(session, glyph, logo, title, $"{session.Platform} · {session.AppVersion} · {session.OsVersion}", status);
    }

    private static SvgImageSource LoadLogo(string name) => new(new Uri($"ms-appx:///Assets/PlatformLogos/{name}.svg"));

    private string DateLabel(DateTimeOffset date) => date.ToLocalTime().ToString("g", Culture);

    private async void CurrentDevice_Click(object sender, RoutedEventArgs args)
    {
        if (CurrentDevice.Content is DeviceSessionRow row) await ShowDetailsAsync(row);
    }

    private async void OtherDevices_ItemClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is DeviceSessionRow row) await ShowDetailsAsync(row);
    }

    private async Task ShowDetailsAsync(DeviceSessionRow row)
    {
        if (_showingDialog || _lifetime is null || XamlRoot is null) return;
        var session = row.Session;
        var information = new StackPanel { Spacing = 8 };
        Add(row.VersionLabel);
        if (!string.IsNullOrWhiteSpace(session.DeviceModel) && session.DeviceModel != row.Title) Add(session.DeviceModel);
        Add(row.StatusLabel);
        Add(string.Format(Culture, Text("DevicesCreated"), DateLabel(session.CreatedAtUtc)));
        Add(string.Format(Culture, Text("DevicesLastSeen"), DateLabel(session.LastActiveAtUtc)));
        if (!string.IsNullOrWhiteSpace(session.IpAddress))
            Add(string.Format(Culture, Text("DevicesIpAddress"), session.IpAddress));
        if (!string.IsNullOrWhiteSpace(session.Location)) Add(session.Location);

        _showingDialog = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = row.Title, Content = information, XamlRoot = XamlRoot, FlowDirection = FlowDirection,
                CloseButtonText = Text("DevicesClose"), DefaultButton = ContentDialogButton.Close,
                PrimaryButtonText = session.IsCurrent ? string.Empty : Text("DevicesTerminate"),
                SecondaryButtonText = !session.IsCurrent && session.IsUnconfirmed ? Text("DevicesConfirmSession") : string.Empty,
            };
            var action = await ShowDialogAsync(dialog);
            if (action == ContentDialogResult.Secondary && _lifetime is { } lifetime)
            {
                var confirmation = await _api.SendRequestAsync(new ConfirmSessionOperation(new()
                {
                    SessionPublicId = session.SessionPublicId,
                }), lifetime.Token);
                if (!ReferenceEquals(_lifetime, lifetime)) return;
                if (!confirmation.IsSuccess) ShowError(confirmation.Error?.Code);
                else await RefreshAsync();
            }
            if (action == ContentDialogResult.Primary && _lifetime is not null &&
                await ConfirmAsync(Text("DevicesTerminate"), Text("DevicesTerminateOneConfirm")))
                await RevokeAsync(session.SessionPublicId);
        }
        catch (Exception error) { AppLog.Error("Could not show session details.", error); ShowError(); }
        finally { _showingDialog = false; }

        void Add(string message) => information.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
    }

    private async void RevokeOthers_Click(object sender, RoutedEventArgs args)
    {
        if (_showingDialog || _lifetime is null || XamlRoot is null) return;
        _showingDialog = true;
        try
        {
            if (await ConfirmAsync(Text("DevicesTerminateOthers"), Text("DevicesTerminateOthersConfirm")))
                await RevokeAsync(null);
        }
        catch (Exception error) { AppLog.Error("Could not close other sessions.", error); ShowError(); }
        finally { _showingDialog = false; }
    }

    private async Task<bool> ConfirmAsync(string title, string message) => await ShowDialogAsync(new ContentDialog
    {
        Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
        PrimaryButtonText = Text("DevicesTerminate"), CloseButtonText = Text("DevicesClose"),
        DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot, FlowDirection = FlowDirection,
    }) == ContentDialogResult.Primary;

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        _dialog = dialog;
        try { return await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    private async Task RevokeAsync(Guid? session)
    {
        if (_lifetime is not { } lifetime) return;
        try
        {
            var response = session is Guid target
                ? await _api.SendRequestAsync(new TerminateSessionOperation(new() { SessionPublicId = target }), lifetime.Token)
                : await _api.SendRequestAsync(new TerminateOtherSessionsOperation(new()), lifetime.Token);
            if (!ReferenceEquals(_lifetime, lifetime)) return;
            if (!response.IsSuccess) ShowError(response.Error?.Code);
            else
            {
                StatusMessage.IsOpen = false;
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async void Inactivity_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_settingInactivity || _lifetime is not { } lifetime || Inactivity.SelectedIndex < 0) return;
        var requested = InactivityChoices[Inactivity.SelectedIndex];
        if (requested == _days) return;
        Inactivity.IsEnabled = false;
        try
        {
            var response = await _api.SendRequestAsync(new UpdateSessionInactivityOperation(new() { InactivityDays = requested }), lifetime.Token);
            if (!ReferenceEquals(_lifetime, lifetime)) return;
            if (response.IsSuccess && response.Content is { } content)
            {
                _days = content.InactivityDays;
                StatusMessage.IsOpen = false;
                await RefreshAsync();
            }
            else
            {
                ShowError(response.Error?.Code);
                SetInactivity();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Could not change session inactivity.", error); ShowError(); SetInactivity(); }
        finally { Inactivity.IsEnabled = true; }
    }

    private void ShowError(int? code = null)
    {
        if (_lifetime is null) return;
        StatusMessage.Message = Text(code == 4603 ? "DevicesFreshSession" : "DevicesError");
        StatusMessage.Severity = InfoBarSeverity.Error;
        StatusMessage.IsOpen = true;
    }
}
