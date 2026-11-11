using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mizon.Windows.ViewModels.Items;
using System;
using Windows.System;
using Windows.UI.Core;

namespace Mizon.Windows.UserControls;

/// <summary>Shared, reflection-free profile content; navigation belongs to its caller.</summary>
public sealed partial class ProfilePreviewCard : UserControl
{
    private ProfilePreviewViewModel? _profile;
    private bool _initialized;
    private bool _keyboardAttached;
    private int _actionLayout = -1;
    public event EventHandler<ProfilePreviewActionEventArgs>? ActionRequested;
    public event EventHandler? DismissRequested;

    public ProfilePreviewCard()
    {
        InitializeComponent();
        _initialized = true;
        Loaded += Card_Loaded;
        Unloaded += Card_Unloaded;
    }

    public void FocusFirstAction()
    {
        if (IsLoaded && _profile is not null) CloseButton.Focus(FocusState.Programmatic);
    }

    private void Card_Loaded(object sender, RoutedEventArgs args)
    {
        if (_keyboardAttached) return;
        _keyboardAttached = true;
        Localization.PropertyChanged += LanguageChanged;
        KeyDown += Card_KeyDown;
    }

    private void Card_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!_keyboardAttached || IsLoaded) return;
        _keyboardAttached = false;
        Localization.PropertyChanged -= LanguageChanged;
        KeyDown -= Card_KeyDown;
    }

    private void Card_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Tab)
        {
            var backwards = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
            MoveTabFocus(backwards);
            args.Handled = true;
        }
        else if (args.Key is VirtualKey.Up or VirtualKey.Down)
        {
            MoveTabFocus(args.Key == VirtualKey.Up);
            args.Handled = true;
        }
        // The MenuFlyout owns Escape/light-dismiss and restoration of anchor focus.
    }

    internal void MoveTabFocus(bool backwards)
    {
        if (!IsLoaded || _profile is null) return;
        // These are the only three tab stops. Next/Previous with SearchRoot is
        // unsupported in desktop WinUI; keep focus inside the composed menu.
        var current = FocusManager.GetFocusedElement(XamlRoot);
        var index = ReferenceEquals(current, CloseButton) ? 0 : ReferenceEquals(current, PrimaryButton) ? 1 : ReferenceEquals(current, SecondaryButton) ? 2 : backwards ? 0 : -1;
        for (var count = 0; count < 3; count++)
        {
            index = (index + (backwards ? -1 : 1) + 3) % 3;
            Button candidate = index switch { 0 => CloseButton, 1 => PrimaryButton, _ => SecondaryButton };
            if (candidate.Visibility == Visibility.Visible && candidate.IsEnabled && candidate.IsHitTestVisible && candidate.Focus(FocusState.Keyboard)) break;
        }
    }

    public void SetProfile(ProfilePreviewViewModel? profile)
    {
        _profile = profile;
        var name = string.IsNullOrWhiteSpace(profile?.DisplayName) ? Localization.GetString("MusicSurface/Profile") : profile.DisplayName;
        NameText.Text = profile is null ? string.Empty : name;
        AvatarPicture.DisplayName = NameText.Text;
        AvatarPicture.ProfilePicture = profile?.Avatar;
        BannerImage.Source = profile?.Banner;
        SetText(SubtitleText, profile?.DisplaySubtitle);
        SetText(UsernameText, string.IsNullOrWhiteSpace(profile?.Username) ? string.Empty : "@" + profile.Username.TrimStart('@'));
        SetText(BioText, profile?.Bio);
        SetText(DetailText, profile?.DisplayDetail);
        DetailPanel.Visibility = DetailText.Visibility;
        SetText(FooterText, profile?.DisplayFooterText);
        SetText(PrimaryText, profile?.DisplayPrimaryActionText);
        SetText(SecondaryText, profile?.DisplaySecondaryActionText);
        PrimaryButton.Visibility = PrimaryText.Visibility;
        SecondaryButton.Visibility = SecondaryText.Visibility;
        UpdateActionLayout(ActualWidth);
        if (profile is not null)
        {
            PrimaryIcon.Symbol = profile.PrimaryActionSymbol;
            SecondaryIcon.Symbol = profile.SecondaryActionSymbol;
        }
        AutomationProperties.SetName(this, profile is null ? Localization.GetString("MusicSurface/ProfilePreview") : Localization.Format("MusicSurface/ProfileNamed", name));
        AutomationProperties.SetName(PrimaryButton, PrimaryText.Text);
        AutomationProperties.SetName(SecondaryButton, SecondaryText.Text);
    }

    private static void SetText(TextBlock target, string? text)
    {
        target.Text = text ?? string.Empty;
        target.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Card_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_initialized) UpdateActionLayout(args.NewSize.Width);
    }

    private void UpdateActionLayout(double width)
    {
        var primaryVisible = PrimaryButton.Visibility == Visibility.Visible;
        var secondaryVisible = SecondaryButton.Visibility == Visibility.Visible;
        var both = primaryVisible && secondaryVisible;
        var stacked = both && width > 0 && width < 280;
        var layout = (primaryVisible ? 1 : 0) | (secondaryVisible ? 2 : 0) | (stacked ? 4 : 0);
        if (_actionLayout == layout) return;
        _actionLayout = layout;

        PrimaryColumn.Width = new GridLength(primaryVisible || stacked ? 1 : 0, GridUnitType.Star);
        SecondaryColumn.Width = new GridLength(secondaryVisible || stacked ? 1 : 0, GridUnitType.Star);
        Grid.SetColumn(PrimaryButton, stacked ? 0 : 1);
        Grid.SetRow(SecondaryButton, stacked ? 1 : 0);
        Grid.SetColumnSpan(PrimaryButton, stacked ? 2 : 1);
        Grid.SetColumnSpan(SecondaryButton, stacked ? 2 : 1);
        ActionsPanel.ColumnSpacing = both && !stacked ? 8 : 0;
        ActionsPanel.RowSpacing = stacked ? 8 : 0;
        ActionsPanel.Visibility = primaryVisible || secondaryVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Primary_Click(object sender, RoutedEventArgs args)
    {
        if (_profile is { } profile) ActionRequested?.Invoke(this, new(profile, ProfilePreviewAction.Primary));
    }

    private void Secondary_Click(object sender, RoutedEventArgs args)
    {
        if (_profile is { } profile) ActionRequested?.Invoke(this, new(profile, ProfilePreviewAction.Secondary));
    }

    private void Close_Click(object sender, RoutedEventArgs args) => DismissRequested?.Invoke(this, EventArgs.Empty);
}
