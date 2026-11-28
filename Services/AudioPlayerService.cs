
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Render;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;

namespace Mizon.Windows.Services;


public class TrackEndedEventArgs : EventArgs { }
public class PositionUpdatedEventArgs : EventArgs
{
    public TimeSpan Position { get; }
    public PositionUpdatedEventArgs(TimeSpan position)
        => Position = position;
}

public class StateUpdatedEventArgs : EventArgs
{
    public AudioPlayerState AudioPlayerState { get; }
    public StateUpdatedEventArgs(AudioPlayerState audioPlayerState)
        => AudioPlayerState = audioPlayerState;
}

public sealed class NowPlayingMetadata
{
    public string Title { get; init; } = string.Empty;
    public string? Artist { get; init; }
    public string? AlbumTitle { get; init; }
    public string? ThumbnailPath { get; init; }
}

public enum AudioPlayerState
{
    Playing, Paused, Idle,
}

public enum AudioPlayeRepeatMode
{
    Off,
    RepeatOne,
    // RepeatAll (آینده)
}

public interface IAudioPlayerService
{
    event EventHandler<TrackEndedEventArgs>? TrackEnded;
    event EventHandler? TrackRestarted;
    event EventHandler<PositionUpdatedEventArgs>? PositionUpdated;
    event EventHandler<StateUpdatedEventArgs>? StateUpdated;

    Task InitializeAsync();
    Task PlayAsync(string path, TimeSpan? startPosition = null);
    Task PlayAsync(Uri uri, TimeSpan? startPosition = null);
    Task PlayStemMixAsync(string vocalsPath, string instrumentalPath, double balance, CancellationToken token = default,
        TimeSpan? startPosition = null);
    Task SwitchStemMixAsync(string vocalsPath, string instrumentalPath, double balance, CancellationToken token);
    Task SwitchToMainAsync(string path, CancellationToken token);
    Task PauseAsync();
    Task StopAsync();
    Task TogglePlayPauseAsync();
    Task UpdateNowPlayingAsync(NowPlayingMetadata metadata);
    AudioPlayerState AudioPlayerState { get; }
    Task ResumeAsync();

    void SeekTo(TimeSpan position);
    void AddReverbEffectDefinition(float wetDryMix = 80f, float decayTime = 4f, float roomSize = 100f);
    void RemoveReverbEffectDefinition();
    Task ChangeOutputDeviceAsync(DeviceInformation deviceInformation);

    TimeSpan TrackDuration { get; }
    TimeSpan Position { get; }
    AudioPlayeRepeatMode AudioPlayeRepeatMode { get; set; }

    double Volume { get; set; }
    bool IsStemMixing { get; }
    double StemMixBalance { get; set; }
}

public class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private AudioGraph? _audioGraph;
    private MediaSourceAudioInputNode? _audioInputNode;
    private MediaSourceAudioInputNode? _vocalsInputNode;
    private MediaSourceAudioInputNode? _instrumentalInputNode;
    private AudioSubmixNode? _stemSubmixNode;
    private AudioFrameOutputNode? _frameOutputNode;
    private AudioDeviceOutputNode? _deviceOutputNode;
    private ReverbEffectDefinition? _reverbEffectDefinition;
    private SystemMediaTransportControls? _smtc;
    private NowPlayingMetadata? _nowPlayingMetadata;
    private string? _currentTrackPath;
    private string? _instrumentalSourcePath;
    private long _totalSamplesProcessed;
    private int _sampleRate;
    private int _channelCount;
    private AudioPlayerState _playState = AudioPlayerState.Idle;
    public AudioPlayerState AudioPlayerState => _playState;

    private TimeSpan _lastPosition = TimeSpan.Zero;
    private long _lastTaskbarProgressUpdate;
    private long _lastSmtcTimelineUpdate;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _positionNotificationTimer;
    private Window? _lifetimeWindow;
    private readonly ITaskbarService _taskbarService;
    private readonly SemaphoreSlim _playbackTransitionLock = new(1, 1);
    // Frame analysis runs on a worker thread. A source switch must freeze that
    // timeline before it snapshots the audible position, otherwise a queued frame
    // from the outgoing source can overwrite the hand-off position mid-switch.
    private readonly object _timelineSynchronization = new();
    private int _isSourceSwitching;
    private int _trackEndTransitionPending;
    private int _frameProcessingFailureLogged;
    private Exception? _pendingFrameProcessingFailure;
    private int _positionNotificationFailureLogged;
    private int _disposed;


    public AudioPlayerService(DispatcherQueue dispatcher, ITaskbarService taskbarService)
    {
        _dispatcher = dispatcher;
        _taskbarService = taskbarService;
        _positionNotificationTimer = dispatcher.CreateTimer();
        _positionNotificationTimer.Interval = TimeSpan.FromMilliseconds(33);
        _positionNotificationTimer.Tick += PositionNotificationTimer_Tick;
    }


    public TimeSpan TrackDuration { get; private set; }
    public TimeSpan Position
    {
        get { lock (_timelineSynchronization) return _lastPosition; }
    }

    public bool IsStemMixing => _vocalsInputNode is not null && _instrumentalInputNode is not null;

    private double _stemMixBalance;
    public double StemMixBalance
    {
        get => _stemMixBalance;
        set
        {
            if (!double.IsFinite(value))
                return;

            _stemMixBalance = Math.Clamp(value, -1d, 1d);
            if (_vocalsInputNode is { } vocals && _instrumentalInputNode is { } instrumental)
                ApplyStemMixGains(vocals, instrumental, _stemMixBalance);
        }
    }

    private double _volume = 1.0;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0, 1.0);

            if (_deviceOutputNode != null)
                _deviceOutputNode.OutgoingGain = _volume;
        }
    }

    public event EventHandler<TrackEndedEventArgs>? TrackEnded;
    public event EventHandler? TrackRestarted;
    public event EventHandler<PositionUpdatedEventArgs>? PositionUpdated;
    public event EventHandler<StateUpdatedEventArgs>? StateUpdated;
   
    private AudioPlayeRepeatMode _audioPlayeRepeatMode = AudioPlayeRepeatMode.Off;
    public AudioPlayeRepeatMode AudioPlayeRepeatMode
    {
        get => _audioPlayeRepeatMode;
        set => _audioPlayeRepeatMode = value;
    }

    private Task InitializeSmtcAsync()
    {
        if (_smtc is not null)
            return Task.CompletedTask;

        var completionSource = new TaskCompletionSource();

        if (!_dispatcher.TryEnqueue(() =>
        {
            try
            {
                // GetForCurrentView به CoreWindow متعلق به مدل UWP نیاز دارد و در
                // WinUI 3 با E_INVALID_WINDOW_HANDLE شکست می‌خورد. GetForWindow
                // SMTC را مستقیماً به HWND پنجرهٔ اصلی WinUI 3 متصل می‌کند.
                var mainWindow = App.MainMizonWindow
                    ?? throw new InvalidOperationException("Main window is not available for System Media Transport Controls.");

                if (_lifetimeWindow is null)
                {
                    _lifetimeWindow = mainWindow;
                    _lifetimeWindow.Closed += LifetimeWindow_Closed;
                }

                var mainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
                _smtc = SystemMediaTransportControlsInterop.GetForWindow(mainWindowHandle);
                _smtc.IsEnabled = true;
                _smtc.IsPlayEnabled = true;
                _smtc.IsPauseEnabled = true;
                _smtc.IsStopEnabled = true;
                _smtc.IsNextEnabled = false;
                _smtc.IsPreviousEnabled = false;
                _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                _smtc.ButtonPressed += Smtc_ButtonPressed;
                completionSource.SetResult();
            }
            catch (Exception exception)
            {
                completionSource.SetException(exception);
            }
        }))
        {
            completionSource.SetException(new InvalidOperationException("Unable to initialize System Media Transport Controls."));
        }

        return completionSource.Task;
    }

    private void Smtc_ButtonPressed(
    SystemMediaTransportControls sender,
    SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        _dispatcher.TryEnqueue(async () =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    if (_playState == AudioPlayerState.Paused)
                        await ResumeAsync();
                    else if (_playState == AudioPlayerState.Idle && !string.IsNullOrWhiteSpace(_currentTrackPath))
                        await TogglePlayPauseAsync();
                    break;

                case SystemMediaTransportControlsButton.Pause:
                    await PauseAsync();
                    break;

                case SystemMediaTransportControlsButton.Stop:
                    await StopAsync();
                    break;

            }
        });
    }


    public async Task ResumeAsync()
    {
        await _playbackTransitionLock.WaitAsync();
        try
        {
            ResumeCore();
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private void ResumeCore()
    {
        if (_playState != AudioPlayerState.Paused || (_audioInputNode == null && !IsStemMixing)
            || _audioGraph is not { } graph)
            return;

        _audioInputNode?.Seek(_lastPosition);
        _vocalsInputNode?.Seek(_lastPosition);
        _instrumentalInputNode?.Seek(_lastPosition);
        _totalSamplesProcessed =
            (long)(_lastPosition.TotalSeconds * _sampleRate);

        graph.Start();
        StartAudioAnalysis();
        SetPlayState(AudioPlayerState.Playing);

        SetDeviceGain(Volume);

        _smtc?.PlaybackStatus = MediaPlaybackStatus.Playing;
        UpdateSmtcTimeline(_lastPosition);

    }

    public async Task InitializeAsync()
    {
        AppLog.Information("Audio graph initialization started.");
        var settings = new AudioGraphSettings(AudioRenderCategory.Media)
        {
            DesiredRenderDeviceAudioProcessing = AudioProcessing.Default,
            //PrimaryRenderDevice = 
        };

        var result = await AudioGraph.CreateAsync(settings);
        if (result.Status != AudioGraphCreationStatus.Success)
            throw new Exception("AudioGraph creation failed");

        var graph = result.Graph;
        try
        {
            var outputResult = await graph.CreateDeviceOutputNodeAsync();
            if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
                throw new InvalidOperationException($"Device output creation failed: {outputResult.Status}.");

            var deviceOutput = outputResult.DeviceOutputNode;
            deviceOutput.OutgoingGain = _volume;
            var frameOutput = graph.CreateFrameOutputNode();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            graph.QuantumStarted += AudioGraph_QuantumStarted;
            _deviceOutputNode = deviceOutput;
            _frameOutputNode = frameOutput;
            _audioGraph = graph;
        }
        catch
        {
            graph.Dispose();
            throw;
        }

        await InitializeSmtcAsync();
        AppLog.Information("Audio graph initialization completed.");
    }

    public async Task TogglePlayPauseAsync()
    {
        switch (_playState)
        {
            case AudioPlayerState.Playing:
                await PauseAsync();
                break;
            case AudioPlayerState.Paused:
                await ResumeAsync();
                break;
            case AudioPlayerState.Idle when !string.IsNullOrWhiteSpace(_currentTrackPath):
                if (_instrumentalSourcePath is { } instrumentalPath)
                    await PlayStemMixAsync(_currentTrackPath, instrumentalPath, _stemMixBalance);
                else
                    await PlayAsync(_currentTrackPath);
                if (_playState == AudioPlayerState.Playing && Volatile.Read(ref _disposed) == 0)
                    TrackRestarted?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
    public async Task UpdateNowPlayingAsync(NowPlayingMetadata metadata)
    {
        _nowPlayingMetadata = metadata;

        StorageFile? thumbnail = null;

        if (!string.IsNullOrWhiteSpace(metadata.ThumbnailPath) && File.Exists(metadata.ThumbnailPath))
            thumbnail = await StorageFile.GetFileFromPathAsync(metadata.ThumbnailPath);

        var completionSource = new TaskCompletionSource();
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    if (_smtc is not null)
                    {
                        var updater = _smtc.DisplayUpdater;
                        updater.Type = MediaPlaybackType.Music;
                        updater.MusicProperties.Title = metadata.Title;
                        updater.MusicProperties.Artist = metadata.Artist ?? string.Empty;
                        updater.MusicProperties.AlbumTitle = metadata.AlbumTitle ?? string.Empty;
                        updater.Thumbnail = thumbnail is null
                            ? null
                            : RandomAccessStreamReference.CreateFromFile(thumbnail);
                        updater.Update();
                    }

                    completionSource.SetResult();
                }
                catch (Exception exception)
                {
                    completionSource.SetException(exception);
                }
            }))
        {
            completionSource.SetException(new InvalidOperationException("Unable to update System Media Transport Controls."));
        }

        await completionSource.Task;
    }

    public Task PlayAsync(string path, TimeSpan? startPosition = null) =>
        ReplaceSourcesAsync(path, null, startPosition, 0, false, CancellationToken.None);

    public Task PlayAsync(Uri uri, TimeSpan? startPosition = null) =>
        PlayAsync(uri.AbsoluteUri, startPosition);

    public Task PlayStemMixAsync(string vocalsPath, string instrumentalPath, double balance, CancellationToken token = default,
        TimeSpan? startPosition = null) =>
        ReplaceSourcesAsync(vocalsPath, instrumentalPath, startPosition ?? TimeSpan.Zero, balance, false, token);

    public Task SwitchStemMixAsync(string vocalsPath, string instrumentalPath, double balance, CancellationToken token) =>
        ReplaceSourcesAsync(vocalsPath, instrumentalPath, null, balance, true, token);

    public Task SwitchToMainAsync(string path, CancellationToken token) =>
        ReplaceSourcesAsync(path, null, null, 0, true, token);

    private async Task ReplaceSourcesAsync(string path, string? instrumentalPath, TimeSpan? startPosition,
        double balance, bool preservePlayback, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (instrumentalPath is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(instrumentalPath);

        await _playbackTransitionLock.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (preservePlayback && _playState == AudioPlayerState.Idle)
                return;

            if (!preservePlayback && instrumentalPath is null && _playState == AudioPlayerState.Paused && !startPosition.HasValue &&
                string.Equals(_currentTrackPath, path, StringComparison.OrdinalIgnoreCase) && !IsStemMixing)
            {
                ResumeCore();
                return;
            }

            if (_audioGraph is null)
                await InitializeAsync();
            if (!preservePlayback && _nowPlayingMetadata is not null)
                await UpdateNowPlayingAsync(_nowPlayingMetadata);

            var graph = _audioGraph ?? throw new InvalidOperationException("The audio graph is unavailable.");
            var deviceOutput = _deviceOutputNode ?? throw new InvalidOperationException("The audio device output is unavailable.");
            var frameOutput = _frameOutputNode ?? throw new InvalidOperationException("The audio frame output is unavailable.");
            MediaSourceAudioInputNode? primary = null;
            MediaSourceAudioInputNode? instrumental = null;
            AudioSubmixNode? submix = null;
            var previousState = _playState;
            var previousPosition = TimeSpan.Zero;
            var frozen = false;

            try
            {
                // Open and decode the files with no output connection. The current source
                // keeps playing until all new nodes are ready for the same render quantum.
                primary = await CreateInputNodeAsync(graph, path);
                if (instrumentalPath is not null)
                    instrumental = await CreateInputNodeAsync(graph, instrumentalPath);
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (preservePlayback && _playState == AudioPlayerState.Idle)
                    return;

                previousState = _playState;
                frozen = true;
                previousPosition = FreezeTimelineForSourceSwitch(graph);
                var duration = instrumental is null ? primary.Duration : GetShortestDuration(primary.Duration, instrumental.Duration);
                var position = ClampPosition(preservePlayback ? previousPosition : startPosition.GetValueOrDefault(), duration);

                primary.Seek(position);
                if (instrumental is null)
                {
                    primary.AddOutgoingConnection(deviceOutput);
                    primary.AddOutgoingConnection(frameOutput);
                }
                else
                {
                    instrumental.Seek(position);
                    submix = ConnectStemNodes(graph, primary, instrumental, deviceOutput, frameOutput);
                    ApplyStemMixGains(primary, instrumental, balance);
                }

                ReleaseInputNodes();
                _audioInputNode = instrumental is null ? primary : null;
                _vocalsInputNode = instrumental is null ? null : primary;
                _instrumentalInputNode = instrumental;
                _stemSubmixNode = submix;
                primary = null;
                instrumental = null;
                submix = null;
                frozen = false; // Source ownership has transferred to the player.

                _currentTrackPath = path;
                _instrumentalSourcePath = instrumentalPath;
                _stemMixBalance = balance;
                TrackDuration = duration;
                _sampleRate = (int)graph.EncodingProperties.SampleRate;
                _channelCount = (int)graph.EncodingProperties.ChannelCount;
                SetTimelineAfterSourceSwitch(position);
                Volatile.Write(ref _isSourceSwitching, 0);

                if (!preservePlayback || previousState == AudioPlayerState.Playing)
                {
                    deviceOutput.OutgoingGain = Volume;
                    graph.Start();
                    StartAudioAnalysis();
                    _smtc?.PlaybackStatus = MediaPlaybackStatus.Playing;
                    if (previousState != AudioPlayerState.Playing)
                        SetPlayState(AudioPlayerState.Playing);
                    UpdateTaskbarProgress(position, force: true);
                }
                UpdateSmtcTimeline(position);
                PositionUpdated?.Invoke(this, new PositionUpdatedEventArgs(position));
            }
            finally
            {
                DisposeInputNode(primary);
                DisposeInputNode(instrumental);
                submix?.Dispose();

                Volatile.Write(ref _isSourceSwitching, 0);
                if (frozen && previousState == AudioPlayerState.Playing && Volatile.Read(ref _disposed) == 0)
                {
                    graph.Start();
                    StartAudioAnalysis();
                }
            }
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private static async Task<MediaSourceAudioInputNode> CreateInputNodeAsync(AudioGraph graph, string path)
    {
        var source = await CreateMediaSourceAsync(path);
        try
        {
            var result = await graph.CreateMediaSourceAudioInputNodeAsync(source);
            if (result.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                throw new InvalidOperationException($"Unable to open the audio source: {result.Status}.");
            return result.Node;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private TimeSpan FreezeTimelineForSourceSwitch(AudioGraph graph)
    {
        TimeSpan handoffPosition;
        lock (_timelineSynchronization)
        {
            Volatile.Write(ref _isSourceSwitching, 1);
            handoffPosition = _lastPosition;
        }

        // A quantum already waiting for the timeline lock must be able to finish
        // before Stop waits for the native graph. The switching flag rejects its
        // commit, so the handoff stays frozen after releasing the lock.
        StopAudioAnalysis();
        graph.Stop();
        ResetFrameOutputNode();
        return handoffPosition;
    }

    private void SetTimelineAfterSourceSwitch(TimeSpan position)
    {
        lock (_timelineSynchronization)
        {
            _lastPosition = position;
            _totalSamplesProcessed = (long)(position.TotalSeconds * _sampleRate);
        }
    }

    private void ResetFrameOutputNode()
    {
        try
        {
            _frameOutputNode?.Reset();
        }
        catch (Exception exception)
        {
            AppLog.Error("Unable to reset the audio frame output node.", exception);
        }
    }

    private static AudioSubmixNode ConnectStemNodes(AudioGraph graph, MediaSourceAudioInputNode vocals,
        MediaSourceAudioInputNode instrumental, AudioDeviceOutputNode device, AudioFrameOutputNode frames)
    {
        var submix = graph.CreateSubmixNode();
        try
        {
            vocals.AddOutgoingConnection(submix);
            instrumental.AddOutgoingConnection(submix);
            submix.AddOutgoingConnection(device);
            submix.AddOutgoingConnection(frames);
            return submix;
        }
        catch
        {
            submix.Dispose();
            throw;
        }
    }

    private static void ApplyStemMixGains(MediaSourceAudioInputNode vocals, MediaSourceAudioInputNode instrumental, double balance)
    {
        vocals.OutgoingGain = Math.Min(1d, 1d + balance);
        instrumental.OutgoingGain = Math.Min(1d, 1d - balance);
    }

    private static TimeSpan GetShortestDuration(TimeSpan first, TimeSpan second)
    {
        if (first == TimeSpan.Zero)
            return second;
        if (second == TimeSpan.Zero)
            return first;
        return first <= second ? first : second;
    }

    private void ReleaseInputNodes()
    {
        DisposeInputNode(_audioInputNode);
        DisposeInputNode(_vocalsInputNode);
        DisposeInputNode(_instrumentalInputNode);
        try { _stemSubmixNode?.Dispose(); }
        catch (Exception exception) { AppLog.Error("Unable to release the stem submix node.", exception); }
        _audioInputNode = null;
        _vocalsInputNode = null;
        _instrumentalInputNode = null;
        _stemSubmixNode = null;
        _reverbEffectDefinition = null;
    }

    private static void DisposeInputNode(MediaSourceAudioInputNode? node)
    {
        if (node is null)
            return;

        try
        {
            using var source = node.MediaSource;
            node.Dispose();
        }
        catch (Exception exception)
        {
            AppLog.Error("Unable to release an audio input node.", exception);
        }
    }

    private void SetDeviceGain(double volume)
    {
        if (_deviceOutputNode is not null)
            _deviceOutputNode.OutgoingGain = volume;
    }

    public async Task PauseAsync()
    {
        await _playbackTransitionLock.WaitAsync();
        try
        {
            PauseCore();
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private void PauseCore()
    {
        if (_playState != AudioPlayerState.Playing)
            return;

        SetPlayState(AudioPlayerState.Paused);
        SetDeviceGain(0);

        _audioGraph?.Stop();
        StopAudioAnalysis();

        _smtc?.PlaybackStatus = MediaPlaybackStatus.Paused;

        // Update taskbar to paused state
        if (TrackDuration != TimeSpan.Zero)
        {
            double percentage = _lastPosition.TotalSeconds / TrackDuration.TotalSeconds;
            percentage = Math.Max(0, Math.Min(1, percentage));
            _taskbarService.SetProgress(percentage, TaskbarProgressState.Paused);
        }


    }

    private void SetPlayState(AudioPlayerState audioPlayerState)
    {
        _playState = audioPlayerState;
        _taskbarService.SetPlaybackButtonState(audioPlayerState);
        StateUpdated?.Invoke(this, new StateUpdatedEventArgs(audioPlayerState));
    }

    private void UpdateSmtcTimeline(TimeSpan position)
    {
        if (TrackDuration == TimeSpan.Zero)
            return;

        var now = Environment.TickCount64;
        if (now - _lastSmtcTimelineUpdate < 500)
            return;

        _lastSmtcTimelineUpdate = now;

        var timeline = new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            EndTime = TrackDuration,
            Position = position
        };

        _dispatcher.TryEnqueue(() => _smtc?.UpdateTimelineProperties(timeline));
    }

    public async Task StopAsync()
    {
        await _playbackTransitionLock.WaitAsync();
        try
        {
            StopCore();
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private void StopCore()
    {
        if (_playState == AudioPlayerState.Idle && _audioInputNode is null && !IsStemMixing)
            return;

        SetDeviceGain(0);

        StopAudioAnalysis();
        var graph = _audioGraph;
        graph?.Stop();
        ReleaseInputNodes();
        graph?.ResetAllNodes();
        SetTimelineAfterSourceSwitch(TimeSpan.Zero);

        _smtc?.PlaybackStatus = MediaPlaybackStatus.Stopped;

        SetPlayState(AudioPlayerState.Idle);

        // Clear taskbar progress on stop
        _taskbarService.ClearProgress();
    }

    public async Task ChangeOutputDeviceAsync(DeviceInformation newDevice)
    {
        ArgumentNullException.ThrowIfNull(newDevice);

        AppLog.Information($"Audio output change requested. Name='{newDevice.Name}', Id='{newDevice.Id}', State='{_playState}'.");
        await _playbackTransitionLock.WaitAsync();
        try
        {
            var previousState = _playState;
            var currentSource = _currentTrackPath;
            var wasStemMixing = IsStemMixing;
            var instrumentalSourcePath = _instrumentalSourcePath;
            var resumePosition = Position;
            var duration = TrackDuration;
            var previousGraph = _audioGraph;

            var settings = new AudioGraphSettings(AudioRenderCategory.Media)
            {
                DesiredRenderDeviceAudioProcessing = AudioProcessing.Default,
                PrimaryRenderDevice = newDevice
            };

            var graphResult = await AudioGraph.CreateAsync(settings);
            if (graphResult.Status != AudioGraphCreationStatus.Success)
                throw new InvalidOperationException($"Unable to use '{newDevice.Name}' as the audio output device.");

            var newGraph = graphResult.Graph;
            AudioDeviceOutputNode? newOutputNode = null;
            AudioFrameOutputNode? newFrameNode = null;
            MediaSourceAudioInputNode? newInputNode = null;
            MediaSourceAudioInputNode? newVocalsInputNode = null;
            MediaSourceAudioInputNode? newInstrumentalInputNode = null;
            AudioSubmixNode? newStemSubmixNode = null;

            try
            {
                var outputResult = await newGraph.CreateDeviceOutputNodeAsync();
                if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
                    throw new InvalidOperationException($"Unable to create an audio output node for '{newDevice.Name}'.");

                newOutputNode = outputResult.DeviceOutputNode;
                newOutputNode.OutgoingGain = 0f;
                newFrameNode = newGraph.CreateFrameOutputNode();

                if (previousState != AudioPlayerState.Idle && !string.IsNullOrWhiteSpace(currentSource))
                {
                    if (wasStemMixing && instrumentalSourcePath is not null)
                    {
                        newVocalsInputNode = await CreateInputNodeAsync(newGraph, currentSource);
                        newInstrumentalInputNode = await CreateInputNodeAsync(newGraph, instrumentalSourcePath);
                        newStemSubmixNode = ConnectStemNodes(newGraph, newVocalsInputNode, newInstrumentalInputNode, newOutputNode, newFrameNode);
                        duration = GetShortestDuration(newVocalsInputNode.Duration, newInstrumentalInputNode.Duration);
                    }
                    else
                    {
                        newInputNode = await CreateInputNodeAsync(newGraph, currentSource);
                        newInputNode.AddOutgoingConnection(newOutputNode);
                        newInputNode.AddOutgoingConnection(newFrameNode);
                        duration = newInputNode.Duration;
                    }
                }

                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                resumePosition = ClampPosition(Position, duration);
                newInputNode?.Seek(resumePosition);
                if (newVocalsInputNode is not null && newInstrumentalInputNode is not null)
                {
                    newVocalsInputNode.Seek(resumePosition);
                    newInstrumentalInputNode.Seek(resumePosition);
                    ApplyStemMixGains(newVocalsInputNode, newInstrumentalInputNode, _stemMixBalance);
                }
            }
            catch
            {
                DisposeInputNode(newInputNode);
                DisposeInputNode(newVocalsInputNode);
                DisposeInputNode(newInstrumentalInputNode);
                newStemSubmixNode?.Dispose();
                newGraph.Dispose();
                throw;
            }

            if (previousState == AudioPlayerState.Playing)
                SetDeviceGain(0);

            StopAudioAnalysis();
            if (previousGraph is not null)
                previousGraph.QuantumStarted -= AudioGraph_QuantumStarted;
            previousGraph?.Stop();
            ReleaseInputNodes();
            previousGraph?.Dispose();

            _deviceOutputNode = newOutputNode;
            _frameOutputNode = newFrameNode;
            _audioGraph = newGraph;
            newGraph.QuantumStarted += AudioGraph_QuantumStarted;
            _audioInputNode = newInputNode;
            _vocalsInputNode = newVocalsInputNode;
            _instrumentalInputNode = newInstrumentalInputNode;
            _stemSubmixNode = newStemSubmixNode;
            TrackDuration = duration;
            _sampleRate = (int)newGraph.EncodingProperties.SampleRate;
            _channelCount = (int)newGraph.EncodingProperties.ChannelCount;
            SetTimelineAfterSourceSwitch(resumePosition);

            await InitializeSmtcAsync();

            if (newInputNode is null && newVocalsInputNode is null)
                return;

            UpdateSmtcTimeline(resumePosition);
            UpdateTaskbarProgress(resumePosition);

            if (previousState == AudioPlayerState.Playing)
            {
                newGraph.Start();
                StartAudioAnalysis();
                _smtc?.PlaybackStatus = MediaPlaybackStatus.Playing;
                SetPlayState(AudioPlayerState.Playing);
                SetDeviceGain(Volume);
            }
            else
            {
                _smtc?.PlaybackStatus = MediaPlaybackStatus.Paused;
                SetPlayState(AudioPlayerState.Paused);
            }

            AppLog.Information($"Audio output change completed. Name='{newDevice.Name}', RestoredState='{_playState}', Position='{_lastPosition}'.");
        }

        catch (Exception exception)
        {
            AppLog.Error($"Audio output change failed. Name='{newDevice.Name}', Id='{newDevice.Id}'.", exception);
            throw;
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private static async Task<MediaSource> CreateMediaSourceAsync(string sourceIdentifier)
    {
        if (Uri.TryCreate(sourceIdentifier, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return MediaSource.CreateFromUri(uri);

        var file = await StorageFile.GetFileFromPathAsync(sourceIdentifier);
        return MediaSource.CreateFromStorageFile(file);
    }

    private static TimeSpan ClampPosition(TimeSpan position, TimeSpan duration) =>
        position < TimeSpan.Zero ? TimeSpan.Zero : duration != TimeSpan.Zero && position > duration ? duration : position;



    public void AddReverbEffectDefinition(float wetDryMix = 80f, float decayTime = 4f, float roomSize = 100f)
    {
        if (_audioInputNode == null || _audioGraph == null)
            return;

        RemoveReverbEffectDefinition();

        _reverbEffectDefinition = new ReverbEffectDefinition(_audioGraph)
        {
            WetDryMix = wetDryMix,
            DecayTime = decayTime,
            RoomSize = roomSize
        };

        _audioInputNode.EffectDefinitions.Add(_reverbEffectDefinition);


        //var props = new PropertySet
        //{
        //    ["Strength"] = 1.2f
        //};


        //var asdasd = typeof(VocalRemoveEffect).FullName;

        //var vocalEffect = new AudioEffectDefinition(
        //    asdasd,
        //    props
        //);

        //_audioInputNode.EffectDefinitions.Add(vocalEffect);
    }

    public void RemoveReverbEffectDefinition()
    {
        if (_audioInputNode == null || _reverbEffectDefinition == null)
            return;

        if (_audioInputNode.EffectDefinitions.Contains(_reverbEffectDefinition))
            _audioInputNode.EffectDefinitions.Remove(_reverbEffectDefinition);

        _reverbEffectDefinition = null;
    }

    public bool IsReverbActive => _reverbEffectDefinition != null;


    public void SeekTo(TimeSpan position)
    {
        if (Volatile.Read(ref _isSourceSwitching) != 0)
        {
            return;
        }

        if (_audioInputNode == null && !IsStemMixing)
            return;

        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        position = ClampPosition(position, TrackDuration);

        lock (_timelineSynchronization)
        {
            if (Volatile.Read(ref _isSourceSwitching) != 0)
                return;

            _audioInputNode?.Seek(position);
            _vocalsInputNode?.Seek(position);
            _instrumentalInputNode?.Seek(position);
            ResetFrameOutputNode();
            _lastPosition = position;
            _totalSamplesProcessed = (long)(position.TotalSeconds * _sampleRate);
        }

        UpdateSmtcTimeline(position);
        UpdateTaskbarProgress(position);
    }

    private void StartAudioAnalysis()
    {
        if (_analysisRunning || Volatile.Read(ref _disposed) != 0)
            return;

        _analysisRunning = true;
        UpdatePositionNotificationTimer();
    }

    private volatile bool _analysisRunning;


    private void StopAudioAnalysis()
    {
        _analysisRunning = false;
        UpdatePositionNotificationTimer();
    }

    private void UpdatePositionNotificationTimer()
    {
        void Update()
        {
            // Read the current state when dispatched: a queued start must not
            // restart notifications after a newer stop or disposal.
            if (_analysisRunning && Volatile.Read(ref _disposed) == 0)
                _positionNotificationTimer.Start();
            else
                _positionNotificationTimer.Stop();
        }

        if (_dispatcher.HasThreadAccess)
            Update();
        else
            _dispatcher.TryEnqueue(Update);
    }

    private void PositionNotificationTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var frameFailure = Interlocked.Exchange(ref _pendingFrameProcessingFailure, null);
        if (frameFailure is not null)
            AppLog.Error("Audio frame processing failed in AudioGraph.QuantumStarted.", frameFailure);

        if (!_analysisRunning || Volatile.Read(ref _isSourceSwitching) != 0 || Volatile.Read(ref _disposed) != 0)
            return;

        try
        {
            // Observe the latest render clock on the UI dispatcher. Slow RGB,
            // presence, logging or UI observers cannot delay a render quantum,
            // and timer ticks do not accumulate stale position notifications.
            var position = Position;
            UpdateTaskbarProgress(position);
            UpdateSmtcTimeline(position);
            PositionUpdated?.Invoke(this, new PositionUpdatedEventArgs(position));
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _positionNotificationFailureLogged, 1) == 0)
                AppLog.Error("Audio playback position notification failed.", exception);
        }
    }

    private void AudioGraph_QuantumStarted(AudioGraph sender, object args)
    {
        if (!_analysisRunning ||
            Volatile.Read(ref _isSourceSwitching) != 0 ||
            !ReferenceEquals(sender, _audioGraph) ||
            _frameOutputNode is not { } frameOutput)
            return;

        try
        {
            // GetFrame returns the audio accumulated since the previous read. It must
            // be consumed from QuantumStarted, not an arbitrary wall-clock timer, so
            // the timeline follows the actual render quanta after a seek/switch.
            bool reachedEnd;
            using (var frame = frameOutput.GetFrame())
                reachedEnd = ProcessAudioFrame(frame);

            // Release the frame before a transition can stop or seek the graph.
            if (reachedEnd)
                QueueTrackEndTransition();
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _frameProcessingFailureLogged, 1) == 0)
                Interlocked.Exchange(ref _pendingFrameProcessingFailure, exception);
        }
    }

    private void UpdateTaskbarProgress(TimeSpan position, bool force = false)
    {
        if (TrackDuration == TimeSpan.Zero)
            return;

        // محاسبه درصد پیشرفت (0.0 تا 1.0)
        double percentage = position.TotalSeconds / TrackDuration.TotalSeconds;

        // محدود کردن به 0-1
        percentage = Math.Max(0, Math.Min(1, percentage));

        var now = Environment.TickCount64;
        if (!force && now - _lastTaskbarProgressUpdate < 200)
            return;

        _lastTaskbarProgressUpdate = now;
        _taskbarService.SetProgress(percentage, _playState == AudioPlayerState.Paused
            ? TaskbarProgressState.Paused : TaskbarProgressState.Normal);

        // فقط وقتی پخش واقعاً تمام می‌شود progress تسک‌بار را پاک کن.
        // در حالت RepeatOne، آهنگ فوراً از ابتدا ادامه پیدا می‌کند.
        if (position >= TrackDuration && AudioPlayeRepeatMode != AudioPlayeRepeatMode.RepeatOne)
        {
            _ = Task.Delay(1000).ContinueWith(_ =>
            {
                _taskbarService.ClearProgress();
            });
        }

    }


    private unsafe bool ProcessAudioFrame(AudioFrame frame)
    {
        if (!_analysisRunning || Volatile.Read(ref _isSourceSwitching) != 0)
            return false;

        int samplesPerChannel;
        using (var buffer = frame.LockBuffer(AudioBufferAccessMode.Read))
        using (var reference = buffer.CreateReference())
        {
            var byteAccess = reference.As<IMemoryBufferByteAccess>();

            byte* dataInBytes;
            uint capacity;
            byteAccess.GetBuffer(out dataInBytes, out capacity);

            int floatCount = (int)Math.Min(buffer.Length, capacity) / sizeof(float);
            samplesPerChannel = floatCount / _channelCount;
        }

        TimeSpan position;
        lock (_timelineSynchronization)
        {
            if (!_analysisRunning || Volatile.Read(ref _isSourceSwitching) != 0)
                return false;

            // Timeline is committed atomically with respect to a seek/source switch.
            _totalSamplesProcessed += samplesPerChannel;
            position = TimeSpan.FromSeconds((double)_totalSamplesProcessed / _sampleRate);
            _lastPosition = position;
        }

        return TrackDuration != TimeSpan.Zero && position >= TrackDuration;
    }

    private void LifetimeWindow_Closed(object sender, WindowEventArgs args) => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _analysisRunning = false;
        if (_dispatcher.HasThreadAccess)
            DisposeOnDispatcher();
        else if (!_dispatcher.TryEnqueue(DisposeOnDispatcher))
            DisposeAudioGraph();

        GC.SuppressFinalize(this);
    }

    private void DisposeOnDispatcher()
    {
        _positionNotificationTimer.Stop();
        _positionNotificationTimer.Tick -= PositionNotificationTimer_Tick;
        if (_lifetimeWindow is not null)
        {
            _lifetimeWindow.Closed -= LifetimeWindow_Closed;
            _lifetimeWindow = null;
        }
        if (_smtc is not null)
        {
            _smtc.ButtonPressed -= Smtc_ButtonPressed;
            _smtc = null;
        }

        DisposeAudioGraph();
    }

    private void DisposeAudioGraph()
    {
        var graph = Interlocked.Exchange(ref _audioGraph, null);
        if (graph is null)
            return;
        _deviceOutputNode = null;
        _frameOutputNode = null;

        // Never stop/dispose the native graph while holding the timeline lock:
        // an in-flight synchronous quantum may still need that lock to finish.
        try
        {
            graph.QuantumStarted -= AudioGraph_QuantumStarted;
            graph.Stop();
        }
        catch (Exception exception)
        {
            // An output-device transition may already have released this graph.
            // Native teardown must not abort window/provider disposal.
            AppLog.Error("Unable to stop the audio graph during disposal.", exception);
        }
        finally
        {
            try
            {
                ReleaseInputNodes();
                graph.Dispose();
            }
            catch (Exception exception)
            {
                AppLog.Error("Unable to dispose the audio graph.", exception);
            }
        }
    }

    private void QueueTrackEndTransition()
    {
        if (Interlocked.Exchange(ref _trackEndTransitionPending, 1) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _playbackTransitionLock.WaitAsync();
                try
                {
                    if (_playState != AudioPlayerState.Playing ||
                        TrackDuration == TimeSpan.Zero ||
                        _lastPosition < TrackDuration)
                        return;

                    if (AudioPlayeRepeatMode == AudioPlayeRepeatMode.RepeatOne)
                    {
                        _audioInputNode?.Seek(TimeSpan.Zero);
                        _vocalsInputNode?.Seek(TimeSpan.Zero);
                        _instrumentalInputNode?.Seek(TimeSpan.Zero);
                        SetTimelineAfterSourceSwitch(TimeSpan.Zero);
                        UpdateTaskbarProgress(TimeSpan.Zero, force: true);
                        UpdateSmtcTimeline(TimeSpan.Zero);
                        _smtc?.PlaybackStatus = MediaPlaybackStatus.Playing;
                        // This runs on the transition worker, never the audio quantum callback.
                        if (Volatile.Read(ref _disposed) == 0)
                            TrackRestarted?.Invoke(this, EventArgs.Empty);
                        return;
                    }

                    StopAudioAnalysis();
                    _audioGraph?.Stop();
                    _smtc?.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    SetTimelineAfterSourceSwitch(TimeSpan.Zero);
                    SetPlayState(AudioPlayerState.Idle);
                    TrackEnded?.Invoke(this, new TrackEndedEventArgs());
                }
                finally
                {
                    _playbackTransitionLock.Release();
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Unable to process the end of the current track.", exception);
            }
            finally
            {
                Interlocked.Exchange(ref _trackEndTransitionPending, 0);
            }
        });
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}

