using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Common;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Controls;

// GridView recycles this small static view; a picker never creates hundreds of Lottie scenes.
public sealed partial class MoodEmojiPreview : Grid
{
    private readonly Image _image = new() { Width = 32, Height = 32, Stretch = Stretch.Uniform };
    private readonly TextBlock _fallback = new() { FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _request;
    private ClientMoodItemResponse? _item;
    private HomePageViewModel? _model;
    private MoodEmojiPreviewCache? _cache;
    private Guid? _previewMediaId;
    private int _revision;

    public MoodEmojiPreview()
    {
        Width = Height = 32;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        CornerRadius = new CornerRadius(10);
        _fallback.FontFamily = new FontFamily("Segoe UI Emoji");
        _fallback.FlowDirection = FlowDirection.LeftToRight;
        Children.Add(_fallback);
        Children.Add(_image);
        Loaded += Preview_Loaded;
        Unloaded += Preview_Unloaded;
    }

    internal void SetItem(ClientMoodItemResponse? item, HomePageViewModel? model, bool selected, MoodEmojiPreviewCache cache)
    {
        var sameItem = item is not null && _item?.PublicId == item.PublicId &&
            _previewMediaId == item.PreviewMediaPublicId && ReferenceEquals(_model, model) && ReferenceEquals(_cache, cache);
        if (!sameItem) Reset();
        _item = item;
        _model = model;
        _cache = item is null ? null : cache;
        _previewMediaId = item?.PreviewMediaPublicId;
        Opacity = selected ? 1 : .88;
        _fallback.Text = item?.Emoji ?? string.Empty;
        // A downloaded Telegram preview must not flash a different system-font
        // emoji first. Unicode is only the fallback when no preview is available.
        if (!sameItem) _fallback.Visibility = item?.PreviewMediaPublicId is null ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) _ = LoadAsync();
    }

    private void Preview_Loaded(object sender, RoutedEventArgs args) => _ = LoadAsync();
    private void Preview_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded) Reset();
    }

    private async Task LoadAsync()
    {
        if (!IsLoaded || _request is not null || _image.Source is not null ||
            _previewMediaId is not Guid mediaId || _model is not { } model || _cache is not { } cache) return;
        if (cache.TryGet(mediaId, out var cached))
        {
            _image.Source = cached;
            _fallback.Visibility = Visibility.Collapsed;
            return;
        }
        var revision = _revision;
        var request = new CancellationTokenSource();
        _request = request;
        var token = request.Token;
        try
        {
            var image = await cache.GetAsync(mediaId, model, token);
            if (revision != _revision || !IsLoaded || token.IsCancellationRequested) return;
            _image.Source = image;
            _fallback.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (revision == _revision && IsLoaded && !token.IsCancellationRequested)
                _fallback.Visibility = Visibility.Visible;
        }
        finally
        {
            if (ReferenceEquals(_request, request)) _request = null;
            request.Dispose();
        }
    }

    private void Reset()
    {
        _revision++;
        _request?.Cancel();
        _request = null; // The operation owns and disposes its CancellationTokenSource.
        _image.Source = null;
        _fallback.Visibility = _previewMediaId is null ? Visibility.Visible : Visibility.Collapsed;
    }
}

// Scoped to one picker, not a global cache. Closing it releases realized images;
// at most 64 original 96px previews remain for instant reopen/back-scroll.
internal sealed class MoodEmojiPreviewCache : IDisposable
{
    private const int Capacity = 64;
    private readonly Dictionary<Guid, ImageSource> _images = [];
    private readonly Queue<Guid> _order = new(Capacity);
    private readonly SemaphoreSlim _decodes = new(2, 2);
    private readonly CancellationTokenSource _lifetime = new();
    private int _active;
    private bool _disposed, _released;

    internal bool TryGet(Guid mediaId, out ImageSource? image)
    {
        image = null;
        return !_disposed && _images.TryGetValue(mediaId, out image);
    }

    internal async ValueTask<ImageSource> GetAsync(Guid mediaId, HomePageViewModel model, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGet(mediaId, out var cached)) return cached!;
        _active++;
        var entered = false;
        try
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var token = request.Token;
            await _decodes.WaitAsync(token);
            entered = true;
            if (TryGet(mediaId, out cached)) return cached!;
            var path = await model.GetMoodMediaPathAsync(mediaId, token);
            token.ThrowIfCancellationRequested();
            var image = await ImageSourceFactory.LoadFileAsync(path, 96, token);
            token.ThrowIfCancellationRequested();
            if (TryGet(mediaId, out cached)) return cached!;
            if (_images.Count == Capacity) _images.Remove(_order.Dequeue());
            _images.Add(mediaId, image);
            _order.Enqueue(mediaId);
            return image;
        }
        finally
        {
            if (entered) _decodes.Release();
            _active--;
            ReleaseWhenIdle();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _images.Clear();
        _order.Clear();
        ReleaseWhenIdle();
    }

    private void ReleaseWhenIdle()
    {
        if (!_disposed || _active != 0 || _released) return;
        _released = true;
        _decodes.Dispose();
        _lifetime.Dispose();
    }
}
