using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Mizon.API;
using Mizon.API.Client.Operations.Clients;
using Mizon.API.Client.Operations.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace Mizon.Windows.Pages;

// Public profile fields are requested by ID; a search result never supplies trusted
// profile data or arbitrary media paths to this page. Controls use no reflection binding.
public sealed partial class ClientProfilePage : Page
{
    private readonly PersonPicture _avatar = new() { Width = 128, Height = 128 };
    private readonly TextBlock _name = new() { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _username = new() { FontSize = 15, Opacity = .7, FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _fullName = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _nickname = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right };
    private readonly ProgressRing _loading = new() { Width = 28, Height = 28, IsActive = true };
    private readonly InfoBar _status = new() { IsClosable = false };
    private readonly Controls.AuthActionButton _retry = new() { Content = "تلاش دوباره", Visibility = Visibility.Collapsed };
    private readonly Controls.AuthActionButton _copy = new() { Content = "کپی لینک پروفایل", IsEnabled = false, CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 10, 18, 10) };
    private readonly StackPanel _identity = new() { Orientation = Orientation.Horizontal, Spacing = 24 };
    private readonly StackPanel _identityText = new() { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _request;
    private TaskCompletionSource<bool> _profileReady = NewProfileCompletion();
    public Guid ClientPublicId { get; private set; }

    private static TaskCompletionSource<bool> NewProfileCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Navigation receipts wait for the existing UI request, never a second API call.
    internal Task<bool> WaitForProfileAsync(CancellationToken cancellationToken) => _profileReady.Task.WaitAsync(cancellationToken);

    public ClientProfilePage()
    {
        // Frame.Navigate resolves page activation through the generated XAML metadata.
        InitializeComponent();
        FontFamily = (FontFamily)Application.Current.Resources["IranSansFont"];
        FlowDirection = FlowDirection.RightToLeft;
        AutomationProperties.SetName(this, "پروفایل کاربر");
        AutomationProperties.SetName(_avatar, "عکس پروفایل کاربر");
        AutomationProperties.SetName(_loading, "دریافت پروفایل");
        AutomationProperties.SetName(_copy, "کپی لینک این پروفایل");
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

        var labels = _identityText;
        labels.Children.Add(new TextBlock { Text = "پروفایل", FontSize = 12, Opacity = .55, TextAlignment = TextAlignment.Right });
        labels.Children.Add(_name); labels.Children.Add(_username);
        _identity.Children.Add(_avatar); _identity.Children.Add(labels);

        var details = new StackPanel { Spacing = 12, Margin = new Thickness(0, 24, 0, 0) };
        details.Children.Add(_fullName); details.Children.Add(_nickname);
        var card = new StackPanel { Spacing = 12, Padding = new Thickness(28) };
        card.Children.Add(_identity); card.Children.Add(details); card.Children.Add(_copy);
        var layout = new StackPanel { Spacing = 16, MaxWidth = 820, Margin = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Stretch };
        layout.Children.Add(_loading);
        layout.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(20), Child = card,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"]
        });
        layout.Children.Add(_status); layout.Children.Add(_retry);
        Content = new ScrollViewer { Content = layout, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += PageLoaded;
        Unloaded += PageUnloaded;
        SizeChanged += PageSizeChanged;
        _retry.Click += RetryClick;
        _copy.Click += CopyClick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        _profileReady.TrySetResult(false);
        _profileReady = NewProfileCompletion();
        ClientPublicId = Guid.Empty;
        if (args.Parameter is Dictionary<string, object> parameters
            && parameters.TryGetValue(PageConstants.CLIENT_ID, out var value) && value is Guid id)
            ClientPublicId = id;
    }

    private async void PageLoaded(object sender, RoutedEventArgs args) => await LoadProfileAsync();
    private async void RetryClick(object sender, RoutedEventArgs args)
    {
        _profileReady.TrySetResult(false);
        _profileReady = NewProfileCompletion();
        await LoadProfileAsync();
    }

    private async Task LoadProfileAsync()
    {
        CancelRequest();
        var ready = _profileReady;
        if (ClientPublicId == Guid.Empty) { ready.TrySetResult(false); return; }
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _request = request;
        var token = request.Token;
        _loading.IsActive = true; _loading.Visibility = Visibility.Visible;
        _status.IsOpen = false; _retry.Visibility = Visibility.Collapsed; _copy.IsEnabled = false;
        _avatar.ProfilePicture = null;
        try
        {
            var services = ((App)App.Current).Services;
            var api = services.GetRequiredService<MizonApi>();
            var response = await api.SendRequestAsync(new ClientProfileOperation(new() { ClientPublicId = ClientPublicId }), token);
            token.ThrowIfCancellationRequested();
            if (!response.IsSuccess || response.Content is not { } profile)
            {
                ShowStatus("پروفایل در دسترس نیست", "این حساب پیدا نشد یا دسترسی به آن ممکن نیست.", InfoBarSeverity.Warning);
                return;
            }
            var fullName = string.Join(" ", new[] { profile.FirstName, profile.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
            var displayName = !string.IsNullOrWhiteSpace(profile.NickName) ? profile.NickName : fullName;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = profile.Username ?? "کاربر میزون";
            _name.Text = displayName;
            _username.Text = string.IsNullOrWhiteSpace(profile.Username) ? string.Empty : "@" + profile.Username;
            _avatar.DisplayName = displayName;
            _fullName.Text = string.IsNullOrWhiteSpace(fullName) ? string.Empty : "نام: " + fullName;
            _nickname.Text = string.IsNullOrWhiteSpace(profile.NickName) ? string.Empty : "نام نمایشی: " + profile.NickName;
            _copy.IsEnabled = true;
            if (profile.ProfileMediaPublicId is { } mediaId)
            {
                var image = await ProfileImageLoader.LoadAsync(api, services.GetRequiredService<MediaDownloader>(), mediaId, token);
                token.ThrowIfCancellationRequested();
                _avatar.ProfilePicture = image.Image;
            }
            ready.TrySetResult(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (ReferenceEquals(_request, request) && IsLoaded)
                ShowStatus("دریافت پروفایل طول کشید", "اتصال را بررسی کن و دوباره امتحان کن.", InfoBarSeverity.Warning);
        }
        catch (Exception exception)
        {
            AppLog.Error($"Client profile could not be loaded. ClientPublicId='{ClientPublicId}'.", exception);
            if (ReferenceEquals(_request, request))
                ShowStatus("دریافت کامل نشد", "اطلاعات یا تصویر پروفایل دریافت نشد؛ دوباره امتحان کن.", InfoBarSeverity.Error);
        }
        finally
        {
            ready.TrySetResult(false);
            if (ReferenceEquals(_request, request))
            {
                _loading.IsActive = false; _loading.Visibility = Visibility.Collapsed;
                _request = null;
            }
            request.Dispose();
        }
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        _status.Title = title; _status.Message = message; _status.Severity = severity; _status.IsOpen = true;
        _retry.Visibility = Visibility.Visible;
    }

    private void CopyClick(object sender, RoutedEventArgs args)
    {
        try
        {
            var data = new DataPackage();
            data.SetText($"mizon://client/{ClientPublicId:D}");
            Clipboard.SetContent(data);
            _status.Title = "لینک کپی شد"; _status.Message = string.Empty;
            _status.Severity = InfoBarSeverity.Success; _status.IsOpen = true;
        }
        catch (Exception exception) { AppLog.Error("Client profile link could not be copied.", exception); }
    }

    private void PageSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 600;
        _identity.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        _avatar.Width = _avatar.Height = compact ? 88 : 128;
        _identityText.Width = Math.Max(1, Math.Min(820, args.NewSize.Width - 56) - 56 - (compact ? 0 : 152));
    }

    private void CancelRequest()
    {
        var request = _request; _request = null;
        request?.Cancel(); // The async owner disposes its source after unwinding.
    }

    private void PageUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        _profileReady.TrySetResult(false);
        CancelRequest(); _avatar.ProfilePicture = null;
        _name.Text = _username.Text = _fullName.Text = _nickname.Text = string.Empty;
    }
}
