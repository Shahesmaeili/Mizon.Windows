using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Common;
using Mizon.Windows.Controls;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.UserControls;

public sealed partial class MoodPicker : UserControl, IDisposable
{
    private readonly HomePageViewModel _model;
    private readonly DispatcherQueueTimer _searchTimer;
    private readonly MoodEmojiPreviewCache _previews = new();
    private CancellationTokenSource? _lifetime;
    private IReadOnlyList<ClientMoodItemResponse> _catalog = [];
    private SearchEntry[] _searchIndex = [];
    private object[] _visibleItems = [];
    private string? _appliedQuery;
    private bool _catalogChanged, _hasCatalog;
    private bool _disposed, _loading, _saving;
    private int _loadRevision;
    public event EventHandler? SelectionSaved;

    public MoodPicker(HomePageViewModel model)
    {
        _model = model;
        InitializeComponent();
        _searchTimer = DispatcherQueue.CreateTimer();
        _searchTimer.Interval = TimeSpan.FromMilliseconds(120);
        _searchTimer.IsRepeating = false;
        _searchTimer.Tick += SearchTimer_Tick;
        Loaded += Picker_Loaded;
        Unloaded += Picker_Unloaded;
    }

    internal bool IsSaving => _saving;

    private async void Picker_Loaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || _lifetime is not null || !IsLoaded) return;
        _lifetime = new CancellationTokenSource();
        SynchronizeSelection();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_lifetime is null || _loading) return;
        var revision = ++_loadRevision;
        _loading = true;
        var token = _lifetime.Token;
        SetLoading(!_hasCatalog);
        if (!_hasCatalog) EmptySurface.Visibility = Visibility.Collapsed;
        try
        {
            var catalog = await _model.GetMoodTypesAsync(token);
            token.ThrowIfCancellationRequested();
            if (revision != _loadRevision || _disposed || !IsLoaded) return;
            if (!ReferenceEquals(catalog, _catalog))
            {
                _catalog = catalog;
                _searchIndex = new SearchEntry[catalog.Count];
                for (var index = 0; index < catalog.Count; index++)
                {
                    var item = catalog[index];
                    _searchIndex[index] = new(item, Normalize(item.Title), Normalize(item.PersianTitle ?? string.Empty));
                }
                _catalogChanged = true;
            }
            _hasCatalog = true;
            ApplySearch();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Services.AppLog.Error("Mood picker catalog could not be displayed.", exception);
            if (!token.IsCancellationRequested && revision == _loadRevision && !_disposed)
            {
                if (!_hasCatalog)
                {
                    EmptyText.Text = "ایموجی‌ها دریافت نشدند؛ دوباره امتحان کن.";
                    EmptySurface.Visibility = Visibility.Visible;
                    RetryButton.Visibility = Visibility.Visible;
                }
            }
        }
        finally
        {
            if (revision == _loadRevision)
            {
                _loading = false;
                if (!token.IsCancellationRequested) SetLoading(false);
            }
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_searchTimer is null) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimer_Tick(DispatcherQueueTimer sender, object args) => ApplySearch();

    private void ApplySearch()
    {
        if (_disposed || !_hasCatalog || _lifetime?.IsCancellationRequested != false) return;
        var query = Normalize(SearchInput.Text.Trim());
        if (!_catalogChanged && string.Equals(query, _appliedQuery, StringComparison.Ordinal))
        {
            SynchronizeSelection();
            return;
        }

        List<object> matches = new(_searchIndex.Length);
        foreach (var entry in _searchIndex)
        {
            if (query.Length == 0 || entry.Item.Emoji.Contains(query, StringComparison.Ordinal) ||
                entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.PersianTitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                matches.Add(entry.Item);
        }
        // Reopening the flyout and repeated text notifications must not tear
        // down a virtualized grid whose actual results have not changed.
        if (!SameResults(matches))
        {
            _visibleItems = matches.ToArray();
            EmojiGrid.ItemsSource = _visibleItems;
        }
        _catalogChanged = false;
        _appliedQuery = query;
        SynchronizeSelection();
        EmptyText.Text = "ایموجی‌ای پیدا نشد";
        RetryButton.Visibility = Visibility.Collapsed;
        EmptySurface.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Normalize(string text) => text.Replace('ي', 'ی').Replace('ك', 'ک').Replace("\u200c", " ");

    private readonly record struct SearchEntry(ClientMoodItemResponse Item, string Title, string PersianTitle);

    private bool SameResults(List<object> matches)
    {
        if (matches.Count != _visibleItems.Length) return false;
        for (var index = 0; index < matches.Count; index++)
        {
            var before = (ClientMoodItemResponse)_visibleItems[index];
            var after = (ClientMoodItemResponse)matches[index];
            if (ReferenceEquals(before, after)) continue;
            if (before.PublicId != after.PublicId || before.PreviewMediaPublicId != after.PreviewMediaPublicId ||
                before.AnimationMediaPublicId != after.AnimationMediaPublicId || before.Emoji != after.Emoji ||
                before.Title != after.Title || before.PersianTitle != after.PersianTitle) return false;
        }
        return true;
    }

    private void SetLoading(bool loading)
    {
        LoadingSurface.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = loading;
    }

    internal void PrepareForOpen()
    {
        if (_disposed) return;
        SetLoading(!_hasCatalog);
        SynchronizeSelection();
    }

    private void SynchronizeSelection()
    {
        var selectedId = _model.CurrentMood?.PublicId;
        object? selected = null;
        foreach (var item in _visibleItems)
        {
            if (((ClientMoodItemResponse)item).PublicId == selectedId) { selected = item; break; }
        }
        if (!ReferenceEquals(EmojiGrid.SelectedItem, selected)) EmojiGrid.SelectedItem = selected;
        if (EmojiGrid.ItemsPanelRoot is not { } panel) return;
        foreach (var child in panel.Children)
        {
            if (child is GridViewItem { Content: ClientMoodItemResponse item, ContentTemplateRoot: MoodEmojiPreview preview } container)
                SetPreview(container, preview, item);
        }
    }

    private void SetPreview(GridViewItem container, MoodEmojiPreview preview, ClientMoodItemResponse item)
    {
        var selected = item.PublicId == _model.CurrentMood?.PublicId;
        preview.SetItem(item, _model, selected, _previews);
        var name = string.IsNullOrWhiteSpace(item.PersianTitle) ? item.Title : item.PersianTitle;
        AutomationProperties.SetName(container, $"{name}{(selected ? "، انتخاب فعلی" : string.Empty)}");
        ToolTipService.SetToolTip(container, name);
    }

    private void Emoji_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer.ContentTemplateRoot is not MoodEmojiPreview preview) return;
        var item = args.InRecycleQueue ? null : args.Item as ClientMoodItemResponse;
        if (item is not null && args.ItemContainer is GridViewItem container)
        {
            SetPreview(container, preview, item);
        }
        else
        {
            preview.SetItem(null, null, false, _previews);
            AutomationProperties.SetName(args.ItemContainer, string.Empty);
            ToolTipService.SetToolTip(args.ItemContainer, null);
        }
        args.Handled = true;
    }

    private async void Emoji_ItemClick(object sender, ItemClickEventArgs args)
    {
        if (_saving || _lifetime is null || args.ClickedItem is not ClientMoodItemResponse mood) return;
        var revision = _loadRevision;
        var token = _lifetime.Token;
        _saving = true;
        EmojiGrid.IsEnabled = SearchInput.IsEnabled = false;
        StatusText.Visibility = Visibility.Collapsed;
        SavingBar.Visibility = Visibility.Visible;
        try
        {
            var saved = await _model.SaveMoodAsync(mood, token);
            if (_disposed || token.IsCancellationRequested || revision != _loadRevision || !IsLoaded) return;
            if (saved)
            {
                AccessibilityAnnouncements.Announce(this, "حال‌وهوایت ذخیره شد", "mood-saved");
                SelectionSaved?.Invoke(this, EventArgs.Empty);
            }
            else if (IsLoaded)
            {
                StatusText.Text = _model.MoodStatus;
                StatusText.Visibility = string.IsNullOrEmpty(_model.MoodStatus) ? Visibility.Collapsed : Visibility.Visible;
                AccessibilityAnnouncements.Announce(StatusText, _model.MoodStatus, "mood-error");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Services.AppLog.Error("Mood selection could not be completed.", exception);
            if (!_disposed && !token.IsCancellationRequested && revision == _loadRevision)
            {
                StatusText.Text = "حال‌وهوایت ذخیره نشد؛ دوباره امتحان کن.";
                StatusText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _saving = false;
            if (!_disposed)
            {
                EmojiGrid.IsEnabled = SearchInput.IsEnabled = true;
                SavingBar.Visibility = Visibility.Collapsed;
                SynchronizeSelection();
            }
        }
    }

    private async void Retry_Click(object sender, RoutedEventArgs args) { if (!_loading) await LoadAsync(); }
    internal void FocusSearch() => SearchInput.Focus(FocusState.Keyboard);

    private void Picker_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded && !_disposed) return;
        _searchTimer.Stop();
        _loadRevision++;
        _loading = false;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        SetLoading(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Picker_Unloaded(this, new RoutedEventArgs());
        EmojiGrid.ItemsSource = null;
        _catalog = [];
        _visibleItems = [];
        _searchIndex = [];
        _previews.Dispose();
        _searchTimer.Tick -= SearchTimer_Tick;
        Loaded -= Picker_Loaded;
        Unloaded -= Picker_Unloaded;
        SelectionSaved = null;
    }
}
