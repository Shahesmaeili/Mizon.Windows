using CommunityToolkit.WinUI.Lottie;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

/// <summary>Owns one composition animation loaded from the existing media cache.</summary>
public sealed partial class AnimatedMoodEmoji : Grid, IDisposable
{
    // Lottie's parser/optimizer runs in the background but does not support
    // cancellation. Bound that work instead of abandoning concurrent parsers.
    private static readonly SemaphoreSlim LoadGate = new(1, 1);

    public static readonly DependencyProperty AnimationPathProperty = DependencyProperty.Register(
        nameof(AnimationPath), typeof(string), typeof(AnimatedMoodEmoji), new PropertyMetadata(null, PathChanged));

    public static readonly DependencyProperty IsLoopingProperty = DependencyProperty.Register(
        nameof(IsLooping), typeof(bool), typeof(AnimatedMoodEmoji), new PropertyMetadata(true, PlaybackChanged));

    public static readonly DependencyProperty IsPlaybackEnabledProperty = DependencyProperty.Register(
        nameof(IsPlaybackEnabled), typeof(bool), typeof(AnimatedMoodEmoji), new PropertyMetadata(true, PlaybackChanged));

    private readonly UISettings _settings = new();
    private AnimatedVisualPlayer? _player;
    private LottieVisualSource? _source;
    private string? _sourcePath;
    private string? _loadingPath;
    private XamlRoot? _root;
    private CancellationTokenSource? _loadCancellation;
    private Task _loadTask = Task.CompletedTask;
    private IAsyncAction? _playback;
    private long _version;
    private long _playbackVersion;
    private readonly List<(UIElement Element, long Registration)> _visibilityObservers = [];
    private bool _loaded;
    private bool _disposed;
    private bool _oneShot;
    private bool _paused;
    private bool _inViewport = true;
    private bool _visibleInTree;

    public AnimatedMoodEmoji()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Loaded += ControlLoaded;
        Unloaded += ControlUnloaded;
    }

    public string? AnimationPath
    {
        get => (string?)GetValue(AnimationPathProperty);
        set => SetValue(AnimationPathProperty, value);
    }

    public bool IsLooping
    {
        get => (bool)GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    public bool IsPlaybackEnabled
    {
        get => (bool)GetValue(IsPlaybackEnabledProperty);
        set => SetValue(IsPlaybackEnabledProperty, value);
    }

    public bool IsReady => _player?.IsAnimatedVisualLoaded == true;
    public bool IsPrepared => _source is not null && string.Equals(_sourcePath, AnimationPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Retains only this control's current parsed document across transient popup closure.</summary>
    public bool RetainSourceOnUnload { get; set; }

    public bool IsPlaying => CanAnimate && _player?.IsPlaying == true;
    public string? ErrorMessage { get; private set; }
    public event EventHandler? Ready;
    public event EventHandler<string>? Failed;

    /// <summary>Prepares one cached document without creating an unloaded native composition scene.</summary>
    public async Task PrepareAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        AnimationPath = path;
        StartLoad(allowUnloaded: true);
        var version = _version;
        await _loadTask.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (version != _version || _disposed) throw new OperationCanceledException("Animation preparation was replaced.");
        if (ErrorMessage is not null) throw new InvalidDataException(ErrorMessage);
    }

    /// <summary>Use a second, loaded instance with IsLooping=false for click effects.</summary>
    public async Task PlayOnceAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loaded) return;
        cancellationToken.ThrowIfCancellationRequested();
        StopPlayback();
        AnimationPath = path;
        var version = _version;
        var requestVersion = _playbackVersion;
        await _loadTask.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (version != _version || requestVersion != _playbackVersion || !_loaded || !IsReady || !CanAnimate) return;

        StopPlayback();
        _oneShot = true;
        var playbackVersion = _playbackVersion;
        var action = _player!.PlayAsync(0, 1, false);
        _playback = action;
        try
        {
            await action.AsTask().WaitAsync(cancellationToken);
        }
        finally
        {
            // A newer click owns its own action and must not be stopped by this completion.
            if (playbackVersion == _playbackVersion)
            {
                StopPlayback();
                _player?.SetProgress(0);
                ApplyPlayback();
            }
        }
    }

    public void Stop()
    {
        IsPlaybackEnabled = false;
        StopPlayback();
        _player?.SetProgress(0);
    }

    private static void PathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((AnimatedMoodEmoji)sender).StartLoad();

    private static void PlaybackChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (AnimatedMoodEmoji)sender;
        if (args.Property == IsLoopingProperty && !control._oneShot) control.StopPlayback();
        control.ApplyPlayback();
    }

    private void ControlLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || _loaded || !IsLoaded) return;
        _loaded = true;
        _inViewport = true;
        _root = XamlRoot;
        if (_root is not null) _root.Changed += RootChanged;
        _settings.AnimationsEnabledChanged += AnimationsChanged;
        EffectiveViewportChanged += ViewportChanged;
        ObserveVisibility();
        StartLoad();
    }

    private void ControlUnloaded(object sender, RoutedEventArgs args)
    {
        // Ignore an old Unloaded delivered after a reparented control is already loaded again.
        if (!IsLoaded) Release();
    }

    private void StartLoad(bool allowUnloaded = false)
    {
        if (_disposed || (!_loaded && !allowUnloaded)) return;
        if (IsPrepared)
        {
            if (_loaded && !IsReady)
            {
                try { AttachSource(); }
                catch (Exception exception) { ReportLoadFailure(exception); }
            }
            return;
        }
        if (_loadCancellation is { IsCancellationRequested: false }
            && string.Equals(_loadingPath, AnimationPath, StringComparison.OrdinalIgnoreCase)) return;
        _version++;
        _loadCancellation?.Cancel();
        StopPlayback();
        if (_player is not null) _player.Source = null;
        _source = null;
        _sourcePath = null;
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(AnimationPath))
        {
            _loadTask = Task.CompletedTask;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _loadingPath = AnimationPath;
        _loadTask = LoadAsync(AnimationPath, _version, cancellation);
    }

    private async Task LoadAsync(string path, long version, CancellationTokenSource cancellation)
    {
        var gateEntered = false;
        try
        {
            // Network requests, TGS decompression and caching belong to the media service.
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute cached animation path is required.", nameof(path));
            await LoadGate.WaitAsync(cancellation.Token);
            gateEntered = true;
            cancellation.Token.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellation.Token);
            var source = new LottieVisualSource { Options = LottieVisualOptions.Optimize };
            // Own the input explicitly; StorageFileLoader otherwise opens its own
            // stream without exposing a deterministic way for us to close it.
            using (var stream = await file.OpenReadAsync().AsTask(cancellation.Token))
            using (var input = stream.GetInputStreamAt(0))
                await source.SetSourceAsync(input);
            if (version != _version || _disposed || cancellation.IsCancellationRequested) return;

            _source = source;
            _sourcePath = path;
            if (_loaded) AttachSource();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (version != _version || _disposed) return;
            ReportLoadFailure(exception);
        }
        finally
        {
            if (gateEntered) LoadGate.Release();
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                _loadingPath = null;
            }
            cancellation.Dispose();
        }
    }

    private void ReportLoadFailure(Exception exception)
    {
        ErrorMessage = exception.Message;
        if (_player is not null) _player.Source = null;
        _source = null;
        _sourcePath = null;
        Failed?.Invoke(this, ErrorMessage);
    }

    private void AttachSource()
    {
        if (_source is null || !_loaded || _disposed) return;
        if (_player is null)
        {
            _player = new AnimatedVisualPlayer { AutoPlay = false, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            AutomationProperties.SetAccessibilityView(_player, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Children.Add(_player);
        }

        _player.Source = _source;
        _player.SetProgress(0);
        if (!_player.IsAnimatedVisualLoaded) throw new InvalidDataException("The animation could not be rendered.");
        Ready?.Invoke(this, EventArgs.Empty);
        ApplyPlayback();
    }

    private bool CanAnimate => _loaded && !_disposed && _inViewport && _visibleInTree && IsPlaybackEnabled
        && _root?.IsHostVisible == true && _settings.AnimationsEnabled;

    private void ApplyPlayback()
    {
        if (!IsReady) return;
        if (!CanAnimate)
        {
            // A one-shot must finish when hidden so its caller can remove the overlay.
            if (_oneShot) StopPlayback();
            else if (!_paused)
            {
                _player!.Pause();
                _paused = true;
            }
            return;
        }
        if (_playback is not null)
        {
            if (_paused)
            {
                _player!.Resume();
                _paused = false;
            }
            return;
        }
        if (!IsLooping) return;
        _ = PlayLoopAsync(_playbackVersion);
    }

    private async Task PlayLoopAsync(long version)
    {
        try
        {
            var playback = _player!.PlayAsync(0, 1, true);
            _paused = false;
            _playback = playback;
            await playback;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (version == _playbackVersion && _loaded && !_disposed)
            {
                ErrorMessage = exception.Message;
                Failed?.Invoke(this, ErrorMessage);
            }
        }
        finally
        {
            if (version == _playbackVersion) _playback = null;
        }
    }

    private void StopPlayback()
    {
        _playbackVersion++;
        _oneShot = false;
        _paused = false;
        _player?.Stop();
        _playback = null;
    }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyPlayback();
    private void VisibilityChanged(DependencyObject sender, DependencyProperty property)
    {
        _visibleInTree = true;
        foreach (var observer in _visibilityObservers)
        {
            if (observer.Element.Visibility == Visibility.Visible) continue;
            _visibleInTree = false;
            break;
        }
        ApplyPlayback();
    }

    private void ObserveVisibility()
    {
        // A collapsed ancestor does not raise EffectiveViewportChanged in WinUI.
        // Observe only while attached, rather than walking the tree on each frame.
        for (DependencyObject? current = this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement element)
                _visibilityObservers.Add((element, element.RegisterPropertyChangedCallback(VisibilityProperty, VisibilityChanged)));
        }
        VisibilityChanged(this, VisibilityProperty);
    }
    private void ViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var visible = args.EffectiveViewport.Width > 0 && args.EffectiveViewport.Height > 0;
        if (visible == _inViewport) return;
        _inViewport = visible;
        ApplyPlayback();
    }
    private void AnimationsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        => DispatcherQueue.TryEnqueue(ApplyPlayback);

    private void Release()
    {
        var retainSource = RetainSourceOnUnload && !_disposed;
        if (!retainSource)
        {
            _version++;
            _loadCancellation?.Cancel();
        }
        if (_loaded)
        {
            _loaded = false;
            _settings.AnimationsEnabledChanged -= AnimationsChanged;
            EffectiveViewportChanged -= ViewportChanged;
            foreach (var observer in _visibilityObservers)
                observer.Element.UnregisterPropertyChangedCallback(VisibilityProperty, observer.Registration);
            _visibilityObservers.Clear();
            _visibleInTree = false;
            if (_root is not null) _root.Changed -= RootChanged;
            _root = null;
        }
        StopPlayback();
        if (_player is not null) _player.Source = null;
        Children.Clear();
        _player = null;
        if (!retainSource)
        {
            _source = null;
            _sourcePath = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
        Loaded -= ControlLoaded;
        Unloaded -= ControlUnloaded;
        Ready = null;
        Failed = null;
    }
}
