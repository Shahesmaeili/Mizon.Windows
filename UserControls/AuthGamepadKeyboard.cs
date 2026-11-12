using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using Mizon.Windows.Services.Gamepad;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.System;

namespace Mizon.Windows.UserControls;

/// <summary>
/// An in-page keyboard for the existing authentication controls. It never submits a form,
/// reveals a password or retains a second copy of the edited field's text.
/// The page owner closes it when foreground/controller ownership is lost.
/// </summary>
public sealed partial class AuthGamepadKeyboard : UserControl, IDisposable
{
    private const int CharacterKeyCount = 50;
    private static readonly string[] EnglishRows = ["0123456789", "qwertyuiop", "asdfghjkl@", "zxcvbnm._-", ",?!'/\\:;+="];
    private static readonly string[] PersianRows = ["0123456789", "ضصثقفغعهخح", "جچشسیبلاتن", "مکگظطزرذدپ", "وئژؤآء؟@._"];
    private static readonly string[] SymbolRows = ["0123456789", "!@#$%^&*()", "-_=+[]{}\\|", ";:'\",.<>/?", "`~€£¥₹©®°×"];
    private readonly Grid _keyGrid = new() { ColumnSpacing = 4, RowSpacing = 4, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBlock _title = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _help = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = .8 };
    private readonly List<KeyCell> _keys = [];
    private readonly FontFamily _persianFont = new("ms-appx:///Assets/Fonts/IRANSANS.TTF#IRANSans");
    private readonly FontFamily _englishFont = new("Segoe UI Variable");
    private Control? _target;
    private long _visibilityToken;
    private long? _readOnlyToken;
    private int _generation;
    private int _focusedKey;
    private bool _persian;
    private bool _symbols;
    private bool _uppercase;
    private bool _languageChosen;
    private bool _disposed;

    public AuthGamepadKeyboard()
    {
        Visibility = Visibility.Collapsed;
        IsTabStop = false;
        TabFocusNavigation = KeyboardNavigationMode.Cycle;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        for (var column = 0; column < 10; column++)
            _keyGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 7; row++)
            _keyGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < CharacterKeyCount; index++)
            AddKey(KeyCommand.Character, index / 10, index % 10);

        AddKey(KeyCommand.Language, 5, 0, 2);
        AddKey(KeyCommand.Layout, 5, 2, 2);
        AddKey(KeyCommand.Case, 5, 4, 2);
        AddKey(KeyCommand.Backspace, 5, 6, 2);
        AddKey(KeyCommand.Done, 5, 8, 2);
        AddKey(KeyCommand.CaretPrevious, 6, 0);
        AddKey(KeyCommand.CaretNext, 6, 1);
        AddKey(KeyCommand.SelectAll, 6, 2, 2);
        AddKey(KeyCommand.Space, 6, 4, 4);
        AddKey(KeyCommand.Close, 6, 8, 2);

        var content = new StackPanel { Spacing = 8, Padding = new Thickness(12) };
        content.Children.Add(_title);
        content.Children.Add(_help);
        content.Children.Add(_keyGrid);
        var scroller = new ScrollViewer
        {
            Content = content,
            IsTabStop = false,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ZoomMode = ZoomMode.Disabled
        };
        // Static, built-in markup keeps native ThemeResource expressions live for
        // light/dark/high-contrast and inherited RequestedTheme changes.
        var border = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Background="{ThemeResource LayerFillColorDefaultBrush}"
                    BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                    BorderThickness="1" CornerRadius="12" />
            """);
        border.Child = scroller;
        Content = border;
        KeyDown += OnKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public bool IsOpen => _target is not null && !_disposed;
    internal Control? EditingTarget => IsOpen ? _target : null;
    public event EventHandler? Closed;

    public void Open(Control target)
    {
        if (_disposed || !CanEdit(target)) return;
        if (IsOpen) Close(false);
        _target = target;
        if (target is PasswordBox passwordBox) passwordBox.PasswordRevealMode = PasswordRevealMode.Hidden;
        _persian = LocalizationService.Current.IsPersian;
        _languageChosen = _symbols = _uppercase = false;
        _focusedKey = 0;
        target.Unloaded += OnTargetUnloaded;
        target.IsEnabledChanged += OnTargetEnabledChanged;
        _visibilityToken = target.RegisterPropertyChangedCallback(VisibilityProperty, OnTargetPropertyChanged);
        if (target is TextBox)
            _readOnlyToken = target.RegisterPropertyChangedCallback(TextBox.IsReadOnlyProperty, OnTargetPropertyChanged);
        LocalizationService.Current.PropertyChanged += OnLocalizationChanged;
        Visibility = Visibility.Visible;
        RefreshLabels();
        var generation = ++_generation;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsOpen && generation == _generation && CanEdit(_target)) FocusKey(0);
        });
    }

    public void Close(bool restoreFocus = true)
    {
        if (_target is not { } target) return;
        _generation++;
        _target = null;
        target.Unloaded -= OnTargetUnloaded;
        target.IsEnabledChanged -= OnTargetEnabledChanged;
        target.UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityToken);
        if (_readOnlyToken is { } token)
            target.UnregisterPropertyChangedCallback(TextBox.IsReadOnlyProperty, token);
        _readOnlyToken = null;
        LocalizationService.Current.PropertyChanged -= OnLocalizationChanged;
        Visibility = Visibility.Collapsed;
        if (restoreFocus && CanEdit(target)) target.Focus(FocusState.Keyboard);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public bool Handle(AuthGamepadAction action)
    {
        if (!IsOpen) return false;
        if (!CanEdit(_target)) { Close(false); return true; }
        switch (action)
        {
            case AuthGamepadAction.Up:
            case AuthGamepadAction.ScrollUp: MoveFocus(0, -1); break;
            case AuthGamepadAction.Down:
            case AuthGamepadAction.ScrollDown: MoveFocus(0, 1); break;
            case AuthGamepadAction.Left: MoveFocus(-1, 0); break;
            case AuthGamepadAction.Right: MoveFocus(1, 0); break;
            case AuthGamepadAction.Accept: Execute(_keys[_focusedKey]); break;
            case AuthGamepadAction.Back:
            case AuthGamepadAction.Submit: Close(); break;
            case AuthGamepadAction.Clear: Backspace(); break;
            case AuthGamepadAction.RevealPassword: ToggleCase(); break;
            case AuthGamepadAction.PreviousSection: _symbols = false; RefreshLabels(); break;
            case AuthGamepadAction.NextSection: _symbols = true; RefreshLabels(); break;
            case AuthGamepadAction.PreviousField: MoveCaret(false); break;
            case AuthGamepadAction.NextField: MoveCaret(true); break;
            case AuthGamepadAction.ToggleHelp: ToggleLanguage(); break;
            case AuthGamepadAction.FocusPrimary: Insert(" "); break;
            case AuthGamepadAction.ResetFocus: FocusKey(0); break;
            default: return false;
        }
        return true;
    }

    private void AddKey(KeyCommand command, int row, int column, int span = 1)
    {
        var button = new Button
        {
            MinWidth = 0, MinHeight = 36, Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            UseSystemFocusVisuals = true, IsTabStop = true
        };
        var label = new TextBlock { FontSize = command == KeyCommand.Character ? 18 : 12, TextTrimming = TextTrimming.CharacterEllipsis };
        button.Content = label;
        var key = new KeyCell(button, label, _keys.Count, row, column, span, command);
        button.Tag = key;
        button.Click += OnKeyClicked;
        button.GotFocus += OnKeyFocused;
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        Grid.SetColumnSpan(button, span);
        _keys.Add(key);
        _keyGrid.Children.Add(button);
    }

    private void RefreshLabels()
    {
        var localization = LocalizationService.Current;
        FontFamily = localization.TextFontFamily;
        Language = localization.LanguageTag;
        _title.FlowDirection = _help.FlowDirection = localization.FlowDirection;
        _title.Text = localization.GetString("GamepadKeyboardTitle");
        _help.Text = localization.GetString("GamepadKeyboardHelp");
        if (_target is PasswordBox)
            _help.Text += "\n" + localization.GetString("GamepadKeyboardPasswordHelp");
        AutomationProperties.SetName(this, _title.Text);
        AutomationProperties.SetHelpText(this, _help.Text);
        var rows = _symbols ? SymbolRows : _persian ? PersianRows : EnglishRows;
        foreach (var key in _keys)
        {
            if (key.Command == KeyCommand.Character)
            {
                key.Insertion = rows[key.Row][key.Column].ToString();
                if (!_symbols && !_persian && _uppercase) key.Insertion = key.Insertion.ToUpperInvariant();
                key.Label.Text = key.Insertion;
                key.Label.FontFamily = _persian && !_symbols ? _persianFont : _englishFont;
            }
            else
            {
                var resource = key.Command switch
                {
                    KeyCommand.Language => _persian ? "GamepadKeyboardLanguageEnglish" : "GamepadKeyboardLanguagePersian",
                    KeyCommand.Layout => _symbols ? "GamepadKeyboardLetters" : "GamepadKeyboardSymbols",
                    KeyCommand.Case => _uppercase ? "GamepadKeyboardLowercase" : "GamepadKeyboardUppercase",
                    KeyCommand.Backspace => "GamepadKeyboardBackspace",
                    KeyCommand.Done => "GamepadKeyboardDone",
                    KeyCommand.CaretPrevious => "GamepadKeyboardCaretPrevious",
                    KeyCommand.CaretNext => "GamepadKeyboardCaretNext",
                    KeyCommand.SelectAll => "GamepadKeyboardSelectAll",
                    KeyCommand.Space => "GamepadKeyboardSpace",
                    _ => "GamepadKeyboardClose"
                };
                key.Label.Text = localization.GetString(resource);
                key.Label.FlowDirection = localization.FlowDirection;
            }
            key.Button.IsEnabled = key.Command switch
            {
                KeyCommand.Case => !_persian && !_symbols,
                KeyCommand.CaretPrevious or KeyCommand.CaretNext or KeyCommand.SelectAll => _target is TextBox,
                _ => true
            };
            AutomationProperties.SetName(key.Button, key.Label.Text);
            if (key.Command == KeyCommand.Language)
                AutomationProperties.SetHelpText(key.Button, localization.GetString("GamepadKeyboardSwitchLanguageHelp"));
        }
        if (!_keys[_focusedKey].Button.IsEnabled) FocusKey(0);
    }

    private void Execute(KeyCell key)
    {
        if (!CanEdit(_target)) { Close(false); return; }
        if (!key.Button.IsEnabled) return;
        switch (key.Command)
        {
            case KeyCommand.Character: Insert(key.Insertion); break;
            case KeyCommand.Language: ToggleLanguage(); break;
            case KeyCommand.Layout: _symbols = !_symbols; RefreshLabels(); break;
            case KeyCommand.Case: ToggleCase(); break;
            case KeyCommand.Backspace: Backspace(); break;
            case KeyCommand.CaretPrevious: MoveCaret(false); break;
            case KeyCommand.CaretNext: MoveCaret(true); break;
            case KeyCommand.SelectAll: if (_target is TextBox textBox) textBox.SelectAll(); break;
            case KeyCommand.Space: Insert(" "); break;
            case KeyCommand.Done:
            case KeyCommand.Close: Close(); break;
        }
    }

    private void Insert(string value)
    {
        if (_target is TextBox textBox)
            ApplyEdit(textBox, AuthGamepadTextEditing.Insert(textBox.Text, textBox.SelectionStart, textBox.SelectionLength, value, textBox.MaxLength));
        else if (_target is PasswordBox passwordBox)
        {
            var password = passwordBox.Password;
            ApplyEdit(passwordBox, password, AuthGamepadTextEditing.Insert(password, password.Length, 0, value, passwordBox.MaxLength));
        }
    }

    private void Backspace()
    {
        if (_target is TextBox textBox)
            ApplyEdit(textBox, AuthGamepadTextEditing.Backspace(textBox.Text, textBox.SelectionStart, textBox.SelectionLength));
        else if (_target is PasswordBox passwordBox)
        {
            var password = passwordBox.Password;
            ApplyEdit(passwordBox, password, AuthGamepadTextEditing.Backspace(password, password.Length, 0));
        }
    }

    private static void ApplyEdit(TextBox target, AuthGamepadTextEdit? edit)
    {
        if (edit is not { } value) return;
        target.Select(value.Start, value.Length);
        target.SelectedText = value.Replacement;
        target.Select(Math.Min(value.Caret, target.Text.Length), 0);
    }

    private static void ApplyEdit(PasswordBox target, string password, AuthGamepadTextEdit? edit)
    {
        if (edit is not { } value) return;
        target.Password = password.Remove(value.Start, value.Length).Insert(value.Start, value.Replacement);
    }

    private void MoveCaret(bool forward)
    {
        if (_target is not TextBox textBox) return;
        var caret = AuthGamepadTextEditing.MoveCaret(textBox.Text, textBox.SelectionStart, textBox.SelectionLength, forward);
        textBox.Select(caret, 0);
    }

    private void ToggleCase()
    {
        if (_persian || _symbols) return;
        _uppercase = !_uppercase;
        RefreshLabels();
    }

    private void ToggleLanguage()
    {
        _persian = !_persian;
        _languageChosen = true;
        _symbols = false;
        RefreshLabels();
    }

    private void MoveFocus(int horizontal, int vertical)
    {
        var current = _keys[_focusedKey];
        KeyCell? next = null;
        var bestDistance = double.MaxValue;
        foreach (var candidate in _keys)
        {
            if (!candidate.Button.IsEnabled || candidate.Index == current.Index) continue;
            if (vertical != 0)
            {
                if (candidate.Row != current.Row + vertical) continue;
            }
            else if (candidate.Row != current.Row || (candidate.Center - current.Center) * horizontal <= 0) continue;
            var distance = Math.Abs(candidate.Center - current.Center);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            next = candidate;
        }
        if (next is not null) FocusKey(next.Index);
    }

    private void FocusKey(int index)
    {
        _focusedKey = index;
        if (IsOpen && IsLoaded && _keys[index].Button.IsEnabled)
            _keys[index].Button.Focus(FocusState.Keyboard);
    }

    private bool CanEdit(Control? target)
    {
        if (target is not (TextBox or PasswordBox) || !target.IsLoaded || !target.IsEnabled
            || target.XamlRoot?.IsHostVisible != true || (XamlRoot is not null && target.XamlRoot != XamlRoot)
            || target is TextBox { IsReadOnly: true }) return false;
        for (DependencyObject? element = target; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    private void OnKeyClicked(object sender, RoutedEventArgs args)
    {
        if (IsOpen && sender is Button { Tag: KeyCell key }) Execute(key);
    }

    private void OnKeyFocused(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: KeyCell key }) return;
        _focusedKey = key.Index;
        key.Button.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!IsOpen) return;
        var action = args.Key switch
        {
            VirtualKey.Left => AuthGamepadAction.Left,
            VirtualKey.Right => AuthGamepadAction.Right,
            VirtualKey.Up => AuthGamepadAction.Up,
            VirtualKey.Down => AuthGamepadAction.Down,
            VirtualKey.Escape => AuthGamepadAction.Back,
            _ => (AuthGamepadAction?)null
        };
        if (action is { } value) args.Handled = Handle(value);
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!IsOpen || args.PropertyName != nameof(LocalizationService.Revision)) return;
        if (!_languageChosen) _persian = LocalizationService.Current.IsPersian;
        RefreshLabels();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) { if (IsOpen) FocusKey(_focusedKey); }
    private void OnUnloaded(object sender, RoutedEventArgs args) => Close(false);
    private void OnTargetUnloaded(object sender, RoutedEventArgs args) => Close(false);
    private void OnTargetEnabledChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!CanEdit(_target)) Close(false); }
    private void OnTargetPropertyChanged(DependencyObject sender, DependencyProperty property) { if (!CanEdit(_target)) Close(false); }

    public void Dispose()
    {
        if (_disposed) return;
        Close(false);
        _disposed = true;
        KeyDown -= OnKeyDown;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        foreach (var key in _keys)
        {
            key.Button.Click -= OnKeyClicked;
            key.Button.GotFocus -= OnKeyFocused;
            key.Button.Tag = null;
        }
        Closed = null;
    }

    private enum KeyCommand { Character, Language, Layout, Case, Backspace, Done, CaretPrevious, CaretNext, SelectAll, Space, Close }
    private sealed class KeyCell(Button button, TextBlock label, int index, int row, int column, int span, KeyCommand command)
    {
        internal Button Button { get; } = button;
        internal TextBlock Label { get; } = label;
        internal int Index { get; } = index;
        internal int Row { get; } = row;
        internal int Column { get; } = column;
        internal double Center => Column + span / 2d;
        internal KeyCommand Command { get; } = command;
        internal string Insertion { get; set; } = string.Empty;
    }
}
