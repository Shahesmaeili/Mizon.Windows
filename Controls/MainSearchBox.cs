using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

// Native TextBox, Popup and virtualized ListView keep input and rendering in WinUI.
// Presentation and local filtering belong here; the page owns the streamed request.
public sealed partial class MainSearchBox : Control
{
    public static readonly DependencyProperty ResultsSourceProperty = DependencyProperty.Register(
        nameof(ResultsSource), typeof(object), typeof(MainSearchBox), new PropertyMetadata(null, SourceChanged));

    public object? ResultsSource { get => GetValue(ResultsSourceProperty); set => SetValue(ResultsSourceProperty, value); }

    private readonly ResultCollection _visible = new();
    private readonly MainSearchStatusItemViewModel _status = new();
    private readonly PointerEventHandler _inputPointerHandler;
    private readonly PointerEventHandler _outsidePointerHandler;
    private readonly PointerEventHandler _resultPointerHandler;
    private readonly UISettings _settings = new();
    private INotifyCollectionChanged? _source;
    private Popup? _popup;
    private Grid? _surface, _backdropHost, _body, _resultsPanel;
    private double _bodyColumnSpacing;
    private Border? _container, _fallbackSurface;
    private ListView? _list;
    private TextBox? _input;
    private TextBlock? _count, _feedback, _hint, _queryLabel, _allCount, _musicCount, _peopleCount, _resultsHeader;
    private FrameworkElement? _previewPane, _header, _footer;
    private ColumnDefinition? _previewColumn;
    private ContentControl? _preview;
    private Button? _previewOpen;
    private ProgressBar? _busyIndicator;
    private object? _previewItem;
    private ToggleButton? _all, _music, _people, _like, _dislike;
    private SystemBackdropElement? _backdrop;
    private ScalarKeyFrameAnimation? _entrance;
    private ScalarKeyFrameAnimation? _previewEntrance;
    private Vector3KeyFrameAnimation? _entranceScale;
    private XamlRoot? _root;
    private UIElement? _rootContent;
    private string _lastInput = string.Empty;
    private string? _pendingExternalSearch;
    private bool _returningFocus;
    private bool _attached, _syncing, _dismissed, _busy, _failed, _feedbackUpdating, _filterUpdating, _sizing;
    private string _query = string.Empty;
    private string _filter = "all";

    public MainSearchBox()
    {
        _inputPointerHandler = InputPointerPressed;
        _outsidePointerHandler = OutsidePointerPressed;
        _resultPointerHandler = ResultPointerMoved;
        Loaded += ControlLoaded;
        Unloaded += ControlUnloaded;
    }

    public event EventHandler<string>? SearchTextChanged;
    public event EventHandler<object>? ResultInvoked;
    internal string? ActiveQuery => _popup?.IsOpen == true && _query.Length != 0 ? _query : _pendingExternalSearch;
    protected override AutomationPeer OnCreateAutomationPeer() => new MainSearchAutomationPeer(this);

    private sealed partial class MainSearchAutomationPeer(MainSearchBox owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(MainSearchBox);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
    public bool FocusInput()
    {
        var focused = _input?.Focus(FocusState.Keyboard) == true;
        if (focused && !_returningFocus && _query.Length >= 2) OpenResults();
        return focused;
    }

    internal void OpenSearch(string query)
    {
        _pendingExternalSearch = query;
        ApplyTemplate();
        TryOpenExternalSearch();
    }

    private void TryOpenExternalSearch()
    {
        if (!IsLoaded || !_attached || _input is null || _pendingExternalSearch is not { } query) return;
        _pendingExternalSearch = null;
        _filter = "all";
        // TextChanged may be deferred by WinUI. Set the guard first so the
        // activation starts exactly one request, including a repeated query.
        _lastInput = query;
        _input.Text = query;
        SearchTextChanged?.Invoke(this, query);
        _input.SelectionStart = _input.Text.Length;
        _input.SelectionLength = 0;
        FocusInput();
    }

    protected override void OnApplyTemplate()
    {
        DetachParts();
        base.OnApplyTemplate();
        _popup = GetTemplateChild("SuggestionsPopup") as Popup;
        _container = GetTemplateChild("SuggestionsContainer") as Border;
        _fallbackSurface = GetTemplateChild("SearchFallbackSurface") as Border;
        _surface = GetTemplateChild("SearchPopupSurface") as Grid;
        _backdropHost = GetTemplateChild("SearchBackdropHost") as Grid;
        _list = GetTemplateChild("SuggestionsList") as ListView;
        _input = GetTemplateChild("TextBox") as TextBox;
        _count = GetTemplateChild("ResultCount") as TextBlock;
        _feedback = GetTemplateChild("FeedbackText") as TextBlock;
        _hint = GetTemplateChild("KeyboardHint") as TextBlock;
        _queryLabel = GetTemplateChild("QueryLabel") as TextBlock;
        _allCount = GetTemplateChild("AllCount") as TextBlock;
        _musicCount = GetTemplateChild("MusicCount") as TextBlock;
        _peopleCount = GetTemplateChild("PeopleCount") as TextBlock;
        _resultsHeader = GetTemplateChild("ResultsHeader") as TextBlock;
        _previewPane = GetTemplateChild("PreviewPane") as FrameworkElement;
        _previewColumn = GetTemplateChild("PreviewColumn") as ColumnDefinition;
        _preview = GetTemplateChild("SearchPreview") as ContentControl;
        _previewOpen = GetTemplateChild("PreviewOpen") as Button;
        _busyIndicator = GetTemplateChild("SearchBusyIndicator") as ProgressBar;
        _header = GetTemplateChild("SearchHeader") as FrameworkElement;
        _footer = GetTemplateChild("SearchFooter") as FrameworkElement;
        _body = GetTemplateChild("SearchBody") as Grid ?? _previewPane?.Parent as Grid;
        _resultsPanel = _list?.Parent as Grid;
        _bodyColumnSpacing = _body?.ColumnSpacing ?? 14;
        _all = GetTemplateChild("AllFilter") as ToggleButton;
        _music = GetTemplateChild("MusicFilter") as ToggleButton;
        _people = GetTemplateChild("PeopleFilter") as ToggleButton;
        _like = GetTemplateChild("LikeResults") as ToggleButton;
        _dislike = GetTemplateChild("DislikeResults") as ToggleButton;
        if (_list is not null) _list.ItemsSource = _visible;
        UpdatePreviewContent();
        if (IsLoaded) AttachParts();
    }

    private static void SourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (MainSearchBox)sender;
        control.ObserveSource();
        control.Rebuild();
    }

    private void ObserveSource()
    {
        if (_source is not null) _source.CollectionChanged -= ResultsChanged;
        _source = IsLoaded ? ResultsSource as INotifyCollectionChanged : null;
        if (_source is not null) _source.CollectionChanged += ResultsChanged;
    }

    private IEnumerable<object> Results => ResultsSource is IEnumerable source ? source.Cast<object>() : [];
    private bool Matches(object item) => item switch
    {
        MainSearchMusicItemViewModel => _filter is "all" or "music",
        MainSearchClientItemViewModel => _filter is "all" or "people",
        _ => false
    };

    public void BeginSearch(string query)
    {
        _query = query.Trim();
        _busy = _query.Length >= 2;
        _failed = false;
        _dismissed = false;
        ResetFeedback();
        Rebuild();
        if (_query.Length >= 2) OpenResults();
        else CloseResults();
    }

    public void CompleteSearch(bool failed = false)
    {
        _busy = false;
        _failed = failed;
        UpdateStatus();
        if (!_dismissed && _query.Length >= 2)
            AccessibilityAnnouncements.Announce(this, failed ? "جستجو کامل نشد؛ دوباره تلاش کن." : $"{FormatCount(Results.Count())} نتیجه پیدا شد.", "main-search");
    }

    public void ClearSearch()
    {
        _query = string.Empty;
        _busy = _failed = false;
        _dismissed = true;
        _lastInput = string.Empty;
        if (_input is not null) _input.Text = string.Empty;
        Rebuild();
        CloseResults();
    }

    private void ResultsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // Append in arrival order without resetting selection or scroll position.
        if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is not null && _query.Length >= 2)
        {
            _syncing = true;
            try
            {
                foreach (var item in args.NewItems)
                    if (Matches(item)) _visible.Add(item);
                if (_visible.Count > 1 && ReferenceEquals(_visible[0], _status)) _visible.RemoveAt(0);
                UpdateStatus();
                // The first streamed result replaces the status preview. Later
                // arrivals leave the current preview, selection and scroll alone.
                if (_previewItem is not (MainSearchMusicItemViewModel or MainSearchClientItemViewModel))
                    SetPreview(_visible.FirstOrDefault(IsResult) ?? _status);
            }
            finally { _syncing = false; }
        }
        else Rebuild();
    }

    private void Rebuild()
    {
        _syncing = true;
        try
        {
            var items = _query.Length >= 2 ? Results.Where(Matches).ToList() : [];
            if (items.Count == 0 && _query.Length >= 2) items.Add(_status);
            _visible.Replace(items);
            UpdateStatus();
            SetPreview(items.FirstOrDefault(IsResult) ?? (_query.Length >= 2 ? _status : null));
        }
        finally { _syncing = false; }
    }

    private void UpdateStatus()
    {
        var musicCount = 0;
        var peopleCount = 0;
        foreach (var item in Results)
        {
            if (item is MainSearchMusicItemViewModel) musicCount++;
            else if (item is MainSearchClientItemViewModel) peopleCount++;
        }
        var total = musicCount + peopleCount;
        var shown = _visible.Count(item => item is not MainSearchStatusItemViewModel);
        var totalText = FormatCount(total);
        var musicText = FormatCount(musicCount);
        var peopleText = FormatCount(peopleCount);
        var shownText = FormatCount(shown);
        _status.IsBusy = _busy;
        _status.Title = _busy ? "داریم پیداش می‌کنیم…" : _failed ? "ارتباط برقرار نشد" : "هنوز چیزی پیدا نکردیم";
        _status.Description = _busy ? "نتیجه‌ها به‌محض پیدا شدن اینجا ظاهر می‌شوند."
            : _failed ? "کمی بعد دوباره بنویس؛ نتایج دریافت‌شده هنوز در دسترس‌اند."
            : total > 0 ? "نوع دیگری از نتیجه را انتخاب کن."
            : "نام آهنگ، نام نمایشی یا نام کاربری دیگری را امتحان کن.";
        if (_count is not null) _count.Text = _busy ? $"{shownText} نتیجه · در حال جستجو" : _failed ? $"{shownText} نتیجه · ارتباط قطع شد" : $"{shownText} نتیجه";
        if (_queryLabel is not null) _queryLabel.Text = _query.Length >= 2 ? $"برای «{_query}»" : string.Empty;
        if (_allCount is not null) _allCount.Text = totalText;
        if (_musicCount is not null) _musicCount.Text = musicText;
        if (_peopleCount is not null) _peopleCount.Text = peopleText;
        if (_all is not null) AutomationProperties.SetName(_all, $"همهٔ نتایج، {totalText} نتیجه");
        if (_music is not null) AutomationProperties.SetName(_music, $"فقط آهنگ‌ها، {musicText} نتیجه");
        if (_people is not null) AutomationProperties.SetName(_people, $"فقط پروفایل‌ها، {peopleText} نتیجه");
        if (_resultsHeader is not null) _resultsHeader.Text = _filter switch
        {
            "music" => "آهنگ‌ها",
            "people" => "پروفایل‌ها",
            _ => "همهٔ نتایج"
        };
        if (_busyIndicator is not null)
        {
            _busyIndicator.IsIndeterminate = _busy && _popup?.IsOpen == true;
            _busyIndicator.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateFilterButtons();
    }

    private static string FormatCount(int count)
    {
        var digits = count.ToString(CultureInfo.InvariantCulture);
        return string.Create(digits.Length, digits, static (destination, source) =>
        {
            for (var index = 0; index < source.Length; index++)
                destination[index] = source[index] is >= '0' and <= '9' ? (char)('۰' + source[index] - '0') : source[index];
        });
    }

    private static bool IsResult(object? item) => item is MainSearchMusicItemViewModel or MainSearchClientItemViewModel;

    private void SetPreview(object? item)
    {
        if (ReferenceEquals(_previewItem, item)) return;
        ReleasePreviewAnimation();
        _previewItem = item;
        UpdatePreviewContent();
        UpdateSize();
        if (_preview is not null && _previewPane?.Visibility == Visibility.Visible && _popup?.IsOpen == true && _settings.AnimationsEnabled)
        {
            _previewEntrance = CompositionTarget.GetCompositorForCurrentThread().CreateScalarKeyFrameAnimation();
            _previewEntrance.Target = nameof(UIElement.Opacity);
            _previewEntrance.Duration = TimeSpan.FromMilliseconds(140);
            _previewEntrance.InsertKeyFrame(0, .45f);
            _previewEntrance.InsertKeyFrame(1, 1);
            _preview.StartAnimation(_previewEntrance);
        }
    }

    private void UpdatePreviewContent()
    {
        // Bind the original view model so async artwork updates remain live.
        if (_preview is not null) _preview.Content = _previewItem;
        if (_previewOpen is not null)
        {
            _previewOpen.Visibility = _previewItem is MainSearchMusicItemViewModel ? Visibility.Visible : Visibility.Collapsed;
            if (_previewItem is MainSearchMusicItemViewModel music)
                AutomationProperties.SetName(_previewOpen, $"باز کردن آهنگ {music.DisplayTitle}");
        }
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_syncing && IsResult(_list?.SelectedItem)) SetPreview(_list!.SelectedItem);
    }

    private void ResultPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_syncing || _list is null) return;
        for (var element = args.OriginalSource as DependencyObject; element is not null && element != _list;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is not ListViewItem container) continue;
            var item = _list.ItemFromContainer(container);
            if (IsResult(item)) SetPreview(item);
            return;
        }
    }

    private void PreviewOpened(object sender, RoutedEventArgs args)
    {
        if (_previewItem is MainSearchMusicItemViewModel music) InvokeResult(music);
    }

    private void UpdateFilterButtons()
    {
        _filterUpdating = true;
        if (_all is not null) _all.IsChecked = _filter == "all";
        if (_music is not null) _music.IsChecked = _filter == "music";
        if (_people is not null) _people.IsChecked = _filter == "people";
        _filterUpdating = false;
    }

    private void FilterClicked(object sender, RoutedEventArgs args)
    {
        if (_filterUpdating) return;
        _filter = (sender as FrameworkElement)?.Tag as string ?? "all";
        Rebuild();
    }

    private void FilterChecked(object sender, RoutedEventArgs args)
    {
        if (!_filterUpdating) FilterClicked(sender, args);
    }

    private void FilterUnchecked(object sender, RoutedEventArgs args)
    {
        if (!_filterUpdating && (sender as FrameworkElement)?.Tag as string == _filter) UpdateFilterButtons();
    }

    private void FeedbackClicked(object sender, RoutedEventArgs args)
    {
        if (_feedbackUpdating) return;
        _feedbackUpdating = true;
        var selected = (ToggleButton)sender;
        if (selected.IsChecked == true)
        {
            var other = ReferenceEquals(selected, _like) ? _dislike : _like;
            if (other is not null) other.IsChecked = false;
        }
        if (_feedback is not null)
        {
            _feedback.Text = _like?.IsChecked == true || _dislike?.IsChecked == true ? "ممنون از نظرت!" : "از نتیجهٔ جستجو راضی بودی؟";
            AccessibilityAnnouncements.Announce(_feedback, _feedback.Text, "search-feedback");
        }
        _feedbackUpdating = false;
    }

    private void ResetFeedback()
    {
        _feedbackUpdating = true;
        if (_like is not null) _like.IsChecked = false;
        if (_dislike is not null) _dislike.IsChecked = false;
        if (_feedback is not null) _feedback.Text = "از نتیجهٔ جستجو راضی بودی؟";
        _feedbackUpdating = false;
    }

    private void ContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // Recycling must restore these properties for ordinary results too.
        bool result = args.Item is not MainSearchStatusItemViewModel;
        args.ItemContainer.IsEnabled = result;
        args.ItemContainer.IsTabStop = result;
        AutomationProperties.SetName(args.ItemContainer, args.Item switch
        {
            MainSearchMusicItemViewModel music => $"آهنگ {music.DisplayTitle}؛ باز کردن جزئیات",
            MainSearchClientItemViewModel client => $"پروفایل {client.DisplayName}، {client.Username}",
            _ => "وضعیت جستجو"
        });
    }

    private void ControlLoaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded) return;
        ObserveSource();
        AttachParts();
        Rebuild();
    }

    private void ControlUnloaded(object sender, RoutedEventArgs args)
    {
        // WinUI can deliver a deferred Unloaded after responsive reparenting has
        // already attached this same control to its new parent.
        if (IsLoaded) return;
        _dismissed = true;
        CloseResults();
        if (_source is not null) _source.CollectionChanged -= ResultsChanged;
        _source = null;
        DetachParts();
    }

    private void AttachParts()
    {
        if (_attached || _popup is null) return;
        _attached = true;
        _popup.Opened += PopupOpened;
        _popup.Closed += PopupClosed;
        _popup.PlacementTarget = this;
        _popup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedRight;
        if (_input is not null)
        {
            _input.TextChanged += InputTextChanged;
            _input.GotFocus += InputGotFocus;
            _input.AddHandler(PointerPressedEvent, _inputPointerHandler, true);
            _input.PreviewKeyDown += InputKeyDown;
            AutomationProperties.SetName(_input, "جستجو در میزون");
            AutomationProperties.SetHelpText(_input, "نام آهنگ یا نام کاربری؛ بالا و پایین برای انتخاب، Enter برای باز کردن و Escape برای بستن نتایج");
        }
        if (_list is not null)
        {
            _list.ItemClick += ItemClicked;
            _list.SelectionChanged += SelectionChanged;
            _list.AddHandler(PointerMovedEvent, _resultPointerHandler, true);
            _list.AddHandler(PointerEnteredEvent, _resultPointerHandler, true);
        }
        if (_previewOpen is not null) _previewOpen.Click += PreviewOpened;
        if (_header is not null) _header.SizeChanged += PopupSectionSizeChanged;
        if (_footer is not null) _footer.SizeChanged += PopupSectionSizeChanged;
        if (_resultsHeader is not null) _resultsHeader.SizeChanged += PopupSectionSizeChanged;
        _settings.AnimationsEnabledChanged += AnimationsPreferenceChanged;
        if (_surface is not null) { _surface.KeyDown += SurfaceKeyDown; _surface.LostFocus += SurfaceLostFocus; }
        if (_list is not null) _list.ContainerContentChanging += ContainerChanging;
        foreach (var button in new[] { _all, _music, _people })
            if (button is not null) { button.Checked += FilterChecked; button.Unchecked += FilterUnchecked; }
        if (_like is not null) { _like.Checked += FeedbackClicked; _like.Unchecked += FeedbackClicked; }
        if (_dislike is not null) { _dislike.Checked += FeedbackClicked; _dislike.Unchecked += FeedbackClicked; }
        _root = XamlRoot;
        if (_root is not null) _root.Changed += RootChanged;
        _rootContent = _root?.Content;
        _rootContent?.AddHandler(PointerPressedEvent, _outsidePointerHandler, true);
        SizeChanged += ControlSizeChanged;
        UpdateSize();
        UpdateStatus();
        TryOpenExternalSearch();
    }

    private void DetachParts()
    {
        if (_popup is not null) _popup.IsOpen = false;
        ReleaseBackdrop();
        if (!_attached) return;
        _attached = false;
        if (_popup is not null) { _popup.Opened -= PopupOpened; _popup.Closed -= PopupClosed; }
        if (_list is not null) _list.ContainerContentChanging -= ContainerChanging;
        foreach (var button in new[] { _all, _music, _people })
            if (button is not null) { button.Checked -= FilterChecked; button.Unchecked -= FilterUnchecked; }
        if (_like is not null) { _like.Checked -= FeedbackClicked; _like.Unchecked -= FeedbackClicked; }
        if (_dislike is not null) { _dislike.Checked -= FeedbackClicked; _dislike.Unchecked -= FeedbackClicked; }
        if (_root is not null) _root.Changed -= RootChanged;
        _rootContent?.RemoveHandler(PointerPressedEvent, _outsidePointerHandler);
        _rootContent = null;
        _root = null;
        SizeChanged -= ControlSizeChanged;
        if (_input is not null)
        {
            _input.TextChanged -= InputTextChanged;
            _input.GotFocus -= InputGotFocus;
            _input.RemoveHandler(PointerPressedEvent, _inputPointerHandler);
            _input.PreviewKeyDown -= InputKeyDown;
        }
        if (_list is not null)
        {
            _list.ItemClick -= ItemClicked;
            _list.SelectionChanged -= SelectionChanged;
            _list.RemoveHandler(PointerMovedEvent, _resultPointerHandler);
            _list.RemoveHandler(PointerEnteredEvent, _resultPointerHandler);
        }
        if (_previewOpen is not null) _previewOpen.Click -= PreviewOpened;
        if (_header is not null) _header.SizeChanged -= PopupSectionSizeChanged;
        if (_footer is not null) _footer.SizeChanged -= PopupSectionSizeChanged;
        if (_resultsHeader is not null) _resultsHeader.SizeChanged -= PopupSectionSizeChanged;
        _settings.AnimationsEnabledChanged -= AnimationsPreferenceChanged;
        if (_surface is not null) { _surface.KeyDown -= SurfaceKeyDown; _surface.LostFocus -= SurfaceLostFocus; }
    }

    private void InputTextChanged(object sender, TextChangedEventArgs args)
    {
        var text = _input?.Text ?? string.Empty;
        if (text == _lastInput) return;
        _lastInput = text;
        SearchTextChanged?.Invoke(this, text);
    }

    private void InputGotFocus(object sender, RoutedEventArgs args)
    {
        VisualStateManager.GoToState(this, "SearchFocused", true);
        // Light-dismiss restores focus here too. Only explicit input, a click,
        // Ctrl+K or an arrow key should reopen a dismissed result panel.
    }

    private void InputPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_query.Length >= 2) OpenResults();
    }

    private void OutsidePointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_popup?.IsOpen != true) return;
        var source = args.OriginalSource as DependencyObject;
        if (!IsWithin(source, this) && !IsWithin(source, _surface)) CloseResults();
    }

    protected override void OnLostFocus(RoutedEventArgs args)
    {
        base.OnLostFocus(args);
        CheckFocusAfterChange();
    }

    private void SurfaceLostFocus(object sender, RoutedEventArgs args) => CheckFocusAfterChange();

    private void CheckFocusAfterChange()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_attached || XamlRoot is null) return;
            var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (IsWithin(focused, this) || IsWithin(focused, _surface)) return;
            CloseResults();
            VisualStateManager.GoToState(this, "SearchIdle", true);
        });
    }

    private void OpenResults()
    {
        if (!_attached || _popup is null || _query.Length < 2) return;
        _dismissed = false;
        UpdateSize();
        _popup.IsOpen = true;
    }

    private void CloseResults()
    {
        _dismissed = true;
        if (_popup is not null) _popup.IsOpen = false;
        ReleaseAnimations();
        if (_busyIndicator is not null) _busyIndicator.IsIndeterminate = false;
    }

    private void InputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_list is null) return;
        if (args.Key is VirtualKey.Up or VirtualKey.Down)
        {
            OpenResults();
            var step = args.Key == VirtualKey.Down ? 1 : -1;
            var index = _list.SelectedIndex;
            if (index < 0) index = step > 0 ? -1 : _visible.Count;
            for (index += step; index >= 0 && index < _visible.Count; index += step)
            {
                if (_visible[index] is MainSearchStatusItemViewModel) continue;
                _list.SelectedIndex = index;
                SetPreview(_visible[index]);
                _list.ScrollIntoView(_visible[index]);
                break;
            }
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Enter && _popup?.IsOpen == true)
        {
            InvokeResult(_list.SelectedItem);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            CloseResults();
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Tab && _popup?.IsOpen == true)
        {
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            if (!shift.HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down))
                args.Handled = _all?.Focus(FocusState.Keyboard) == true;
            else CloseResults();
        }
    }

    private void SurfaceKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        CloseResults();
        _returningFocus = true;
        FocusInput();
        _returningFocus = false;
        args.Handled = true;
    }

    private void ItemClicked(object sender, ItemClickEventArgs args) => InvokeResult(args.ClickedItem);
    private void InvokeResult(object? item)
    {
        if (!IsResult(item)) return;
        SetPreview(item);
        // Profiles currently expose a local preview; only music has a detail route.
        if (item is MainSearchMusicItemViewModel music) ResultInvoked?.Invoke(this, music);
    }

    private static bool IsWithin(DependencyObject? child, DependencyObject? parent)
    {
        if (parent is null) return false;
        for (; child is not null; child = VisualTreeHelper.GetParent(child)) if (child == parent) return true;
        return false;
    }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (!sender.IsHostVisible) CloseResults();
        else UpdateSize();
    }
    private void ControlSizeChanged(object sender, SizeChangedEventArgs args) => UpdateSize();
    private void PopupSectionSizeChanged(object sender, SizeChangedEventArgs args) => UpdateSize();
    private void UpdateSize()
    {
        if (_surface is null || _sizing) return;
        _sizing = true;
        try
        {
            var rootWidth = XamlRoot?.Size.Width ?? 844;
            var rootHeight = XamlRoot?.Size.Height ?? 720;
            var width = Math.Min(Math.Max(820, ActualWidth), Math.Max(0, rootWidth - 24));
            var availableHeight = Math.Max(0, rootHeight - 100);
            if (IsLoaded && XamlRoot?.Content is UIElement rootContent)
            {
                var position = TransformToVisual(rootContent).TransformPoint(new global::Windows.Foundation.Point());
                var below = Math.Max(0, rootHeight - position.Y - ActualHeight - 20);
                var above = Math.Max(0, position.Y - 20);
                var useAbove = below < Math.Min(600, above);
                availableHeight = useAbove ? above : below;
                if (_popup is not null) _popup.DesiredPlacement = useAbove
                    ? PopupPlacementMode.TopEdgeAlignedRight : PopupPlacementMode.BottomEdgeAlignedRight;
            }
            var popupHeight = Math.Min(600, availableHeight);
            var fixedHeight = MeasuredHeight(_header, 110) + MeasuredHeight(_footer, 56);
            var bodyInsets = _body is null ? 12 : _body.Margin.Top + _body.Margin.Bottom
                + _body.Padding.Top + _body.Padding.Bottom + _body.BorderThickness.Top + _body.BorderThickness.Bottom;
            var bodyHeight = Math.Max(0, popupHeight - fixedHeight - bodyInsets);
            var canPreview = width >= 740 && popupHeight >= 520 && IsResult(_previewItem);
            var showPreview = canPreview && bodyHeight >= MeasurePreviewHeight(260);
            _surface.MaxHeight = popupHeight;
            if (_container is not null) _container.Width = width;
            if (_previewPane is not null) _previewPane.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
            if (_previewColumn is not null) _previewColumn.Width = new GridLength(showPreview ? 260 : 0);
            if (_body is not null)
            {
                _body.ColumnSpacing = showPreview ? _bodyColumnSpacing : 0;
                // bodyHeight already excludes the body margin and fixed rows.
                // Reserve it in split mode so changing previews never moves the footer.
                _body.Height = showPreview ? bodyHeight : double.NaN;
            }
            if (!showPreview) ReleasePreviewAnimation();
            if (_list is not null) _list.MaxHeight = Math.Max(0, bodyHeight - MeasuredHeight(_resultsHeader, 24)
                - (_resultsPanel?.RowSpacing ?? 4) - _list.Margin.Top - _list.Margin.Bottom);
            // A horizontal StackPanel measures its query without a width limit.
            // Reserve space for the title and the status before trimming the query.
            var headerWidth = Math.Max(0, width - (_header?.Margin.Left ?? 20) - (_header?.Margin.Right ?? 20) - 2);
            if (_count is not null)
            {
                _count.MaxWidth = headerWidth / 2;
                _count.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            if (_queryLabel is not null)
            {
                _queryLabel.MaxWidth = Math.Min(200, Math.Max(0, headerWidth / 2 - 82));
                _queryLabel.Visibility = _queryLabel.MaxWidth >= 40 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_hint is not null) _hint.Visibility = ActualWidth >= 470 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _sizing = false; }
    }

    private static double MeasuredHeight(FrameworkElement? element, double fallback) => element is null ? fallback
        : element.Visibility == Visibility.Collapsed ? 0
        : element.ActualHeight > 0 ? element.ActualHeight + element.Margin.Top + element.Margin.Bottom : fallback;

    private double MeasurePreviewHeight(double width)
    {
        // Measure the content independently of its collapsed parent so a long
        // title cannot make the preview cover the fixed feedback footer.
        if (_previewPane is not Border { Child: FrameworkElement content } pane) return 400;
        var horizontalInsets = pane.Padding.Left + pane.Padding.Right + pane.BorderThickness.Left + pane.BorderThickness.Right;
        content.Measure(new global::Windows.Foundation.Size(Math.Max(0, width - horizontalInsets), double.PositiveInfinity));
        return Math.Max(340, content.DesiredSize.Height + pane.Padding.Top + pane.Padding.Bottom
            + pane.BorderThickness.Top + pane.BorderThickness.Bottom + pane.Margin.Top + pane.Margin.Bottom);
    }

    private void PopupOpened(object? sender, object args)
    {
        if (!ReferenceEquals(sender, _popup)) return;
        if (_dismissed || !_attached || !IsLoaded || _popup?.IsOpen != true) { CloseResults(); return; }
        UpdateSize();
        UpdateStatus();
        // The backdrop must be created after its host is connected to XamlRoot.
        if (_backdropHost is not null && _backdrop is null)
        {
            try
            {
                _backdrop = new SystemBackdropElement { SystemBackdrop = new DesktopAcrylicBackdrop(), CornerRadius = _surface?.CornerRadius ?? new CornerRadius(16), IsHitTestVisible = false };
                _backdropHost.Children.Add(_backdrop);
                if (_fallbackSurface is not null) _fallbackSurface.Visibility = Visibility.Collapsed;
            }
            catch (Exception exception)
            {
                ReleaseBackdrop();
                Services.AppLog.Error("Search acrylic could not be initialized; using the theme fallback.", exception);
            }
        }
        ReleaseAnimations();
        if (_surface is not null && _settings.AnimationsEnabled)
        {
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), Vector2.One);
            _surface.CenterPoint = new Vector3((float)_surface.ActualWidth, 0, 0);
            _entrance = compositor.CreateScalarKeyFrameAnimation();
            _entrance.Target = nameof(UIElement.Opacity);
            _entrance.Duration = TimeSpan.FromMilliseconds(180);
            _entrance.InsertKeyFrame(0, 0);
            _entrance.InsertKeyFrame(1, 1, easing);
            _surface.StartAnimation(_entrance);
            // Animate the XAML facade, as with the app's native menu motion;
            // a handout Visual transform would break XAML hit-test alignment.
            _entranceScale = compositor.CreateVector3KeyFrameAnimation();
            _entranceScale.Target = nameof(UIElement.Scale);
            _entranceScale.Duration = TimeSpan.FromMilliseconds(220);
            _entranceScale.InsertKeyFrame(0, new Vector3(.975f, .975f, 1));
            _entranceScale.InsertKeyFrame(1, Vector3.One, easing);
            _surface.StartAnimation(_entranceScale);
        }
    }

    private void PopupClosed(object? sender, object args)
    {
        if (!ReferenceEquals(sender, _popup)) return;
        if (_popup?.IsOpen == true) return;
        if (!_syncing && _query.Length >= 2) _dismissed = true;
        ReleaseBackdrop();
    }

    private void ReleaseBackdrop()
    {
        ReleaseAnimations();
        if (_busyIndicator is not null) _busyIndicator.IsIndeterminate = false;
        if (_backdrop is not null)
        {
            _backdrop.SystemBackdrop = null;
            _backdropHost?.Children.Remove(_backdrop);
            _backdrop = null;
        }
        if (_fallbackSurface is not null) _fallbackSurface.Visibility = Visibility.Visible;
    }

    private void AnimationsPreferenceChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_attached && !_settings.AnimationsEnabled) ReleaseAnimations();
    });

    private void ReleaseAnimations()
    {
        ReleasePreviewAnimation();
        if (_entrance is not null) { _surface?.StopAnimation(_entrance); _entrance.Dispose(); _entrance = null; }
        if (_entranceScale is not null) { _surface?.StopAnimation(_entranceScale); _entranceScale.Dispose(); _entranceScale = null; }
        if (_surface is not null)
        {
            _surface.Opacity = 1;
            _surface.Scale = Vector3.One;
            _surface.CenterPoint = Vector3.Zero;
        }
    }

    private void ReleasePreviewAnimation()
    {
        if (_previewEntrance is not null)
        {
            _preview?.StopAnimation(_previewEntrance);
            _previewEntrance.Dispose();
            _previewEntrance = null;
        }
        if (_preview is not null) _preview.Opacity = 1;
    }

    private sealed class ResultCollection : ObservableCollection<object>
    {
        internal void Replace(IEnumerable<object> values)
        {
            Items.Clear();
            foreach (var value in values) Items.Add(value);
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
