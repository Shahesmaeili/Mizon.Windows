
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
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
    event EventHandler<PositionUpdatedEventArgs>? PositionUpdated;
    event EventHandler<StateUpdatedEventArgs>? StateUpdated;

    Task InitializeAsync();
    Task PlayAsync(string path, TimeSpan? startPosition = null);
    Task PlayAsync(Uri uri, TimeSpan? startPosition = null);
    Task PlayStemMixAsync(string vocalsPath, string instrumentalPath, TimeSpan? startPosition = null);
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
    AudioPlayeRepeatMode AudioPlayeRepeatMode { get; set; }

    double Volume { get; set; }
    bool IsStemMixing { get; }
    double StemMixBalance { get; set; }
}

public class AudioPlayerService : IAudioPlayerService
{
    private AudioGraph _audioGraph;
    private MediaSourceAudioInputNode _audioInputNode;
    private MediaSourceAudioInputNode? _vocalsInputNode;
    private MediaSourceAudioInputNode? _instrumentalInputNode;
    private AudioSubmixNode? _stemSubmixNode;
    private AudioFrameOutputNode _frameOutputNode;
    private AudioDeviceOutputNode _deviceOutputNode;
    private ReverbEffectDefinition? _reverbEffectDefinition;
    // AudioGraph state transitions await fades. Cancelling an active fade leaves the
    // caller (Pause/Resume/Stop) midway through its transition, which can leave old
    // sources audible. Keep fades serialized instead.
    private readonly SemaphoreSlim _fadeTransitionLock = new(1, 1);
    private const int FadeDurationMs = 400;
    private SystemMediaTransportControls _smtc;
    private NowPlayingMetadata? _nowPlayingMetadata;
    private string? _currentTrackPath;
    private string? _vocalsSourcePath;
    private string? _instrumentalSourcePath;
    private long _totalSamplesProcessed;
    private int _sampleRate;
    private int _channelCount;
    private AudioPlayerState _playState = AudioPlayerState.Idle;
    public AudioPlayerState AudioPlayerState => _playState;

    private TimeSpan _lastPosition = TimeSpan.Zero;
    private long _lastTaskbarProgressUpdate;
    private long _lastSmtcTimelineUpdate;
    private int _lastStemDiagnosticSecond = -1;
    private long _audioGraphGeneration;
    private long _stemDiagnosticSessionId;
    private readonly DispatcherQueue _dispatcher;
    private readonly ITaskbarService _taskbarService;
    private readonly SemaphoreSlim _outputDeviceChangeLock = new(1, 1);
    private readonly SemaphoreSlim _playbackTransitionLock = new(1, 1);
    // Frame analysis runs on a worker thread. A source switch must freeze that
    // timeline before it snapshots the audible position, otherwise a queued frame
    // from the outgoing source can overwrite the hand-off position mid-switch.
    private readonly object _timelineSynchronization = new();
    private int _isSourceSwitching;
    private int _trackEndTransitionPending;
    private int _frameProcessingFailureLogged;


    public AudioPlayerService(DispatcherQueue dispatcher, ITaskbarService taskbarService)
    {
        _dispatcher = dispatcher;
        _taskbarService = taskbarService;
    }


    public TimeSpan TrackDuration { get; private set; }

    public bool IsStemMixing => _vocalsInputNode is not null && _instrumentalInputNode is not null;

    private double _stemMixBalance;
    public double StemMixBalance
    {
        get => _stemMixBalance;
        set
        {
            _stemMixBalance = Math.Clamp(value, -1d, 1d);
            ApplyStemMixGains();
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
                        await PlayAsync(_currentTrackPath);
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
            await ResumeCoreAsync();
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private async Task ResumeCoreAsync()
    {
        if (_playState != AudioPlayerState.Paused || (_audioInputNode == null && !IsStemMixing))
            return;

        LogStemDiagnostic("resume-before");
        _audioInputNode?.Seek(_lastPosition);
        _vocalsInputNode?.Seek(_lastPosition);
        _instrumentalInputNode?.Seek(_lastPosition);
        _totalSamplesProcessed =
            (long)(_lastPosition.TotalSeconds * _sampleRate);

        _audioGraph.Start();
        StartAudioAnalysis();
        SetPlayState(AudioPlayerState.Playing);

        await FadeInAsync((float)_lastVolume);

        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        UpdateSmtcTimeline(_lastPosition);
        LogStemDiagnostic("resume-after");

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

        _audioGraph = result.Graph;

        var outputResult = await _audioGraph.CreateDeviceOutputNodeAsync();
        if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
            throw new Exception("Device output creation failed");

        _deviceOutputNode = outputResult.DeviceOutputNode;
        _deviceOutputNode.OutgoingGain = _volume;
        _frameOutputNode = _audioGraph.CreateFrameOutputNode();
        _audioGraph.QuantumStarted += AudioGraph_QuantumStarted;

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
                if (IsStemMixing && _vocalsSourcePath is not null && _instrumentalSourcePath is not null)
                    await PlayStemMixAsync(_vocalsSourcePath, _instrumentalSourcePath);
                else
                    await PlayAsync(_currentTrackPath);
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

    public async Task PlayAsync(string path, TimeSpan? startPosition = null)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            await PlayAsync(uri, startPosition);
            return;
        }

        if (IsStemMixing && _playState != AudioPlayerState.Idle)
        {
            await SwitchStemMixToMainAsync(path, startPosition);
            return;
        }

        if (_playState == AudioPlayerState.Paused && _currentTrackPath == path)
        {
            await ResumeAsync();
            return;
        }

        await StopAsync();
        ClearStemMixSources();

        if (_audioGraph == null)
            await InitializeAsync();

        if (_nowPlayingMetadata is not null)
            await UpdateNowPlayingAsync(_nowPlayingMetadata);


        var file = await StorageFile.GetFileFromPathAsync(path);
        _currentTrackPath = path;

        var source = MediaSource.CreateFromStorageFile(file);
        var inputResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(source);

        if (inputResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
            throw new Exception("Audio input node creation failed");

        _audioInputNode = inputResult.Node;
        TrackDuration = source.Duration ?? _audioInputNode.Duration;

        _audioInputNode.AddOutgoingConnection(_deviceOutputNode);
        _audioInputNode.AddOutgoingConnection(_frameOutputNode);

        _sampleRate = (int)_audioGraph.EncodingProperties.SampleRate;
        _channelCount = (int)_audioGraph.EncodingProperties.ChannelCount;
        SetInitialPlaybackPosition(_audioInputNode, startPosition);

        _audioGraph.Start();
        StartAudioAnalysis();

        await FadeInAsync((float)Volume);

        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;

        SetPlayState(AudioPlayerState.Playing);
        // update taskbar immediately after starting playback
        UpdateTaskbarProgress(_lastPosition);

    }

    public async Task PlayAsync(Uri uri, TimeSpan? startPosition = null)
    {
        if (_playState == AudioPlayerState.Paused && _currentTrackPath == uri.AbsoluteUri)
        {
            await ResumeAsync();
            return;
        }

        if (IsStemMixing && _playState != AudioPlayerState.Idle)
        {
            await SwitchStemMixToMainAsync(uri.AbsoluteUri, startPosition);
            return;
        }

        await StopAsync();
        ClearStemMixSources();

        if (_audioGraph == null)
            await InitializeAsync();

        if (_nowPlayingMetadata is not null)
            await UpdateNowPlayingAsync(_nowPlayingMetadata);

        _currentTrackPath = uri.AbsoluteUri;

        var source = MediaSource.CreateFromUri(uri);
        var inputResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(source);

        if (inputResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
            throw new Exception("Audio input node creation failed");

        _audioInputNode = inputResult.Node;
        TrackDuration = source.Duration ?? _audioInputNode.Duration;

        _audioInputNode.AddOutgoingConnection(_deviceOutputNode);
        _audioInputNode.AddOutgoingConnection(_frameOutputNode);

        _sampleRate = (int)_audioGraph.EncodingProperties.SampleRate;
        _channelCount = (int)_audioGraph.EncodingProperties.ChannelCount;
        SetInitialPlaybackPosition(_audioInputNode, startPosition);

        _audioGraph.Start();
        StartAudioAnalysis();

        await FadeInAsync((float)Volume);

        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;

        SetPlayState(AudioPlayerState.Playing);
        UpdateTaskbarProgress(_lastPosition);
    }

    public async Task PlayStemMixAsync(string vocalsPath, string instrumentalPath, TimeSpan? startPosition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vocalsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentalPath);

        if (_playState == AudioPlayerState.Paused &&
            string.Equals(_vocalsSourcePath, vocalsPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_instrumentalSourcePath, instrumentalPath, StringComparison.OrdinalIgnoreCase))
        {
            await ResumeAsync();
            return;
        }

        AppLog.Information($"StemDiag play-mix-request. Vocals='{vocalsPath}', Instrumental='{instrumentalPath}', State='{_playState}'.");
        if (_audioInputNode is not null && _playState != AudioPlayerState.Idle)
        {
            await SwitchMainToStemMixAsync(vocalsPath, instrumentalPath, startPosition);
            return;
        }
        LogAudioGraphState("stem-request-before-stop");
        var handoffPosition = await StopForSourceSwitchAsync();
        LogAudioGraphState("stem-request-after-stop");
        ClearStemMixSources();

        if (_audioGraph == null)
            await InitializeAsync();

        if (_nowPlayingMetadata is not null)
            await UpdateNowPlayingAsync(_nowPlayingMetadata);

        var vocalsSource = await CreateMediaSourceAsync(vocalsPath);
        var instrumentalSource = await CreateMediaSourceAsync(instrumentalPath);
        AppLog.Information($"StemDiag source-created. VocalsDurationMs='{vocalsSource.Duration?.TotalMilliseconds:F0}', InstrumentalDurationMs='{instrumentalSource.Duration?.TotalMilliseconds:F0}', GraphGeneration='{_audioGraphGeneration}'.");
        var vocalsResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(vocalsSource);
        var instrumentalResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(instrumentalSource);

        AppLog.Information($"StemDiag node-creation-result. VocalsStatus='{vocalsResult.Status}', InstrumentalStatus='{instrumentalResult.Status}'.");

        if (vocalsResult.Status != MediaSourceAudioInputNodeCreationStatus.Success ||
            instrumentalResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
            throw new InvalidOperationException("Unable to create the audio stem input nodes.");

        _vocalsInputNode = vocalsResult.Node;
        _instrumentalInputNode = instrumentalResult.Node;
        _audioGraphGeneration++;
        _vocalsSourcePath = vocalsPath;
        _instrumentalSourcePath = instrumentalPath;
        _currentTrackPath = $"stem-mix:{vocalsPath}|{instrumentalPath}";

        ConnectStemNodes();
        ApplyStemMixGains();
        LogAudioGraphState("stem-nodes-connected");

        TrackDuration = GetShortestDuration(vocalsSource.Duration ?? _vocalsInputNode.Duration,
            instrumentalSource.Duration ?? _instrumentalInputNode.Duration);
        _sampleRate = (int)_audioGraph.EncodingProperties.SampleRate;
        _channelCount = (int)_audioGraph.EncodingProperties.ChannelCount;
        // The original source keeps advancing while it fades out. Start stems at its
        // actual hand-off point rather than the position captured at click time.
        var initialPosition = handoffPosition ?? startPosition.GetValueOrDefault();
        if (initialPosition < TimeSpan.Zero)
            initialPosition = TimeSpan.Zero;

        if (TrackDuration != TimeSpan.Zero && initialPosition > TrackDuration)
            initialPosition = TrackDuration;

        // Both sources must be positioned before the graph starts. Starting at zero and
        // seeking after FadeInAsync made source switching audibly jump and left the page
        // timeline briefly reporting the wrong position.
        _vocalsInputNode.Seek(initialPosition);
        _instrumentalInputNode.Seek(initialPosition);
        _totalSamplesProcessed = (long)(initialPosition.TotalSeconds * _sampleRate);
        _lastPosition = initialPosition;
        LogStemDiagnostic("initial-seek-applied");

        // MediaSourceAudioInputNode uses an independent decoder for each stem. Prime both
        // decoders while the device output is muted, then seek them together once more.
        // This prevents a decoder's initial buffered packets from being heard against the
        // other stem when switching from the original track.
        _deviceOutputNode.OutgoingGain = 0f;
        _audioGraph.Start();
        LogStemDiagnostic("graph-started-muted");
        await Task.Delay(TimeSpan.FromMilliseconds(180));
        LogStemDiagnostic("decoder-prime-complete-before-reseek");

        _vocalsInputNode.Seek(initialPosition);
        _instrumentalInputNode.Seek(initialPosition);
        _totalSamplesProcessed = (long)(initialPosition.TotalSeconds * _sampleRate);
        _lastPosition = initialPosition;
        LogStemDiagnostic("decoder-prime-complete-after-reseek");

        StartAudioAnalysis();
        LogAudioGraphState("stem-graph-primed-and-started");
        await FadeInAsync((float)Volume);
        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        SetPlayState(AudioPlayerState.Playing);
        UpdateTaskbarProgress(_lastPosition, force: true);
        LogStemDiagnostic("play-mix-started");
        ScheduleStemDiagnosticSnapshots();
    }

    private async Task SwitchMainToStemMixAsync(string vocalsPath, string instrumentalPath, TimeSpan? requestedPosition)
    {
        await _playbackTransitionLock.WaitAsync();
        try
        {
            var previousMain = _audioInputNode
                ?? throw new InvalidOperationException("There is no main-track node to switch from.");
            var wasPlaying = _playState == AudioPlayerState.Playing;
            var handoffPosition = FreezeTimelineForSourceSwitch();

            try
            {
                // Do not add/start one decoder at a time while the graph is rendering.
                // Both MediaSource nodes are created and positioned while the graph is
                // stopped, then AudioGraph.Start becomes the single start barrier.
                var vocalsSource = await CreateMediaSourceAsync(vocalsPath);
                var instrumentalSource = await CreateMediaSourceAsync(instrumentalPath);
                var vocalsResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(vocalsSource);
                var instrumentalResult = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(instrumentalSource);
                if (vocalsResult.Status != MediaSourceAudioInputNodeCreationStatus.Success ||
                    instrumentalResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                    throw new InvalidOperationException("Unable to create stem nodes during source switch.");

                _vocalsInputNode = vocalsResult.Node;
                _instrumentalInputNode = instrumentalResult.Node;
                ConnectStemNodes();

                TrackDuration = GetShortestDuration(
                    vocalsSource.Duration ?? _vocalsInputNode.Duration,
                    instrumentalSource.Duration ?? _instrumentalInputNode.Duration);
                var position = ClampPosition(handoffPosition, TrackDuration);
                _vocalsInputNode.Seek(position);
                _instrumentalInputNode.Seek(position);

                _vocalsSourcePath = vocalsPath;
                _instrumentalSourcePath = instrumentalPath;
                _currentTrackPath = $"stem-mix:{vocalsPath}|{instrumentalPath}";
                SetTimelineAfterSourceSwitch(position);

                previousMain.OutgoingGain = 0f;
                DisposeInputNode(previousMain);
                _audioInputNode = null;
                ApplyStemMixGains();

                if (wasPlaying)
                {
                    _audioGraph.Start();
                    StartAudioAnalysis();
                }

                AppLog.Information($"StemDiag switch-main-to-stems-complete. PositionMs='{position.TotalMilliseconds:F0}', WasPlaying='{wasPlaying}'.");
                LogStemDiagnostic("switch-main-to-stems-complete");
            }
            catch
            {
                RestoreMainTrackAfterFailedSwitch(previousMain, handoffPosition, wasPlaying);
                throw;
            }
            finally
            {
                Volatile.Write(ref _isSourceSwitching, 0);
            }
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private async Task SwitchStemMixToMainAsync(string path, TimeSpan? requestedPosition)
    {
        await _playbackTransitionLock.WaitAsync();
        try
        {
            var vocals = _vocalsInputNode
                ?? throw new InvalidOperationException("There is no vocal stem node to switch from.");
            var instrumental = _instrumentalInputNode
                ?? throw new InvalidOperationException("There is no instrumental stem node to switch from.");
            var wasPlaying = _playState == AudioPlayerState.Playing;
            var handoffPosition = FreezeTimelineForSourceSwitch();

            try
            {
                var source = await CreateMediaSourceAsync(path);
                var result = await _audioGraph.CreateMediaSourceAudioInputNodeAsync(source);
                if (result.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                    throw new InvalidOperationException("Unable to create the main-track node during source switch.");

                var main = result.Node;
                main.AddOutgoingConnection(_deviceOutputNode);
                main.AddOutgoingConnection(_frameOutputNode);
                TrackDuration = source.Duration ?? main.Duration;
                var position = ClampPosition(handoffPosition, TrackDuration);
                main.Seek(position);

                vocals.OutgoingGain = 0f;
                instrumental.OutgoingGain = 0f;
                DisposeInputNode(vocals);
                DisposeInputNode(instrumental);
                DisposeStemSubmixNode();
                ClearStemMixSources();
                _audioInputNode = main;
                _currentTrackPath = path;
                SetTimelineAfterSourceSwitch(position);

                if (wasPlaying)
                {
                    _audioGraph.Start();
                    StartAudioAnalysis();
                }

                AppLog.Information($"StemDiag switch-stems-to-main-complete. PositionMs='{position.TotalMilliseconds:F0}', WasPlaying='{wasPlaying}'.");
            }
            catch
            {
                RestoreStemMixAfterFailedSwitch(vocals, instrumental, handoffPosition, wasPlaying);
                throw;
            }
            finally
            {
                Volatile.Write(ref _isSourceSwitching, 0);
            }
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private TimeSpan FreezeTimelineForSourceSwitch()
    {
        lock (_timelineSynchronization)
        {
            Volatile.Write(ref _isSourceSwitching, 1);
            var handoffPosition = _lastPosition;
            StopAudioAnalysis();
            _audioGraph.Stop();
            ResetFrameOutputNode();
            AppLog.Information($"StemDiag source-switch-frozen. HandoffMs='{handoffPosition.TotalMilliseconds:F0}', State='{_playState}'.");
            return handoffPosition;
        }
    }

    private void SetTimelineAfterSourceSwitch(TimeSpan position)
    {
        lock (_timelineSynchronization)
        {
            _lastPosition = position;
            _totalSamplesProcessed = (long)(position.TotalSeconds * _sampleRate);
            _lastStemDiagnosticSecond = (int)position.TotalSeconds;
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

    private void RestoreMainTrackAfterFailedSwitch(
        MediaSourceAudioInputNode previousMain,
        TimeSpan handoffPosition,
        bool wasPlaying)
    {
        DisposeInputNode(_vocalsInputNode);
        DisposeInputNode(_instrumentalInputNode);
        DisposeStemSubmixNode();
        ClearStemMixSources();
        _audioInputNode = previousMain;
        previousMain.OutgoingGain = 1f;
        previousMain.Seek(handoffPosition);
        SetTimelineAfterSourceSwitch(handoffPosition);

        if (wasPlaying)
        {
            _audioGraph.Start();
            StartAudioAnalysis();
        }
    }

    private void RestoreStemMixAfterFailedSwitch(
        MediaSourceAudioInputNode vocals,
        MediaSourceAudioInputNode instrumental,
        TimeSpan handoffPosition,
        bool wasPlaying)
    {
        _vocalsInputNode = vocals;
        _instrumentalInputNode = instrumental;
        vocals.Seek(handoffPosition);
        instrumental.Seek(handoffPosition);
        ApplyStemMixGains();
        SetTimelineAfterSourceSwitch(handoffPosition);

        if (wasPlaying)
        {
            _audioGraph.Start();
            StartAudioAnalysis();
        }
    }

    private void DisposeStemSubmixNode()
    {
        if (_stemSubmixNode is null)
            return;

        try
        {
            _stemSubmixNode.Dispose();
        }
        catch (Exception exception)
        {
            AppLog.Error("Unable to dispose the stem submix node.", exception);
        }
        finally
        {
            _stemSubmixNode = null;
        }
    }

    private void ConnectStemNodes()
    {
        _stemSubmixNode = _audioGraph.CreateSubmixNode();
        _vocalsInputNode!.AddOutgoingConnection(_stemSubmixNode);
        _instrumentalInputNode!.AddOutgoingConnection(_stemSubmixNode);
        _stemSubmixNode.AddOutgoingConnection(_deviceOutputNode);
        _stemSubmixNode.AddOutgoingConnection(_frameOutputNode);
    }

    private void ApplyStemMixGains()
    {
        var balance = Math.Clamp(_stemMixBalance, -1d, 1d);
        if (_vocalsInputNode is not null)
            _vocalsInputNode.OutgoingGain = (float)Math.Min(1d, 1d + balance);
        if (_instrumentalInputNode is not null)
            _instrumentalInputNode.OutgoingGain = (float)Math.Min(1d, 1d - balance);

        LogStemDiagnostic("gain-applied");
    }

    private void LogStemDiagnostic(string reason)
    {
        if (!IsStemMixing)
            return;

        AppLog.Information(
            $"StemDiag {reason}. GraphGeneration='{_audioGraphGeneration}', State='{_playState}', TimelineMs='{_lastPosition.TotalMilliseconds:F0}', " +
            $"VocalsMs='{_vocalsInputNode!.Position.TotalMilliseconds:F0}', " +
            $"InstrumentalMs='{_instrumentalInputNode!.Position.TotalMilliseconds:F0}', " +
            $"Balance='{_stemMixBalance:F3}', VocalsGain='{_vocalsInputNode.OutgoingGain:F3}', " +
            $"InstrumentalGain='{_instrumentalInputNode.OutgoingGain:F3}', DeviceGain='{_deviceOutputNode?.OutgoingGain:F3}', " +
            $"Samples='{_totalSamplesProcessed}', SampleRate='{_sampleRate}', AnalysisRunning='{_analysisRunning}'.");
    }

    private void ScheduleStemDiagnosticSnapshots()
    {
        var sessionId = Interlocked.Increment(ref _stemDiagnosticSessionId);
        foreach (var delay in new[] { 250, 500, 1000, 2000, 5000 })
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay);
                if (sessionId == Interlocked.Read(ref _stemDiagnosticSessionId))
                    LogStemDiagnostic($"post-switch-snapshot-{delay}ms");
            });
        }
    }

    private void LogAudioGraphState(string reason)
    {
        AppLog.Information(
            $"AudioGraphDiag {reason}. Generation='{_audioGraphGeneration}', State='{_playState}', " +
            $"MainNode='{_audioInputNode is not null}', MainMs='{_audioInputNode?.Position.TotalMilliseconds:F0}', " +
            $"VocalsNode='{_vocalsInputNode is not null}', VocalsMs='{_vocalsInputNode?.Position.TotalMilliseconds:F0}', " +
            $"InstrumentalNode='{_instrumentalInputNode is not null}', InstrumentalMs='{_instrumentalInputNode?.Position.TotalMilliseconds:F0}', " +
            $"SubmixNode='{_stemSubmixNode is not null}', Track='{_currentTrackPath}'.");
    }

    private static TimeSpan GetShortestDuration(TimeSpan first, TimeSpan second)
    {
        if (first == TimeSpan.Zero)
            return second;
        if (second == TimeSpan.Zero)
            return first;
        return first <= second ? first : second;
    }

    private void SetInitialPlaybackPosition(MediaSourceAudioInputNode node, TimeSpan? startPosition)
    {
        var position = startPosition.GetValueOrDefault();
        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        if (TrackDuration != TimeSpan.Zero && position > TrackDuration)
            position = TrackDuration;

        node.Seek(position);
        lock (_timelineSynchronization)
        {
            _totalSamplesProcessed = (long)(position.TotalSeconds * _sampleRate);
            _lastPosition = position;
            _lastStemDiagnosticSecond = (int)position.TotalSeconds;
        }
    }

    private void ClearStemMixSources()
    {
        _vocalsInputNode = null;
        _instrumentalInputNode = null;
        _vocalsSourcePath = null;
        _instrumentalSourcePath = null;
    }

    private void ReleaseInputNodes()
    {
        // Explicitly release old sources before creating a new graph input. This avoids
        // a previous main-track source remaining audible after changing to stem mixing.
        LogAudioGraphState("release-input-nodes-before-dispose");
        DisposeInputNode(_audioInputNode);
        DisposeInputNode(_vocalsInputNode);
        DisposeInputNode(_instrumentalInputNode);
        DisposeStemSubmixNode();

        _audioInputNode = null;
        ClearStemMixSources();
        LogAudioGraphState("release-input-nodes-after-dispose");
    }

    private static void DisposeInputNode(MediaSourceAudioInputNode? node)
    {
        if (node is null)
            return;

        try
        {
            node.Stop();
            node.Dispose();
        }
        catch (Exception exception)
        {
            AppLog.Error("Unable to release an audio input node.", exception);
        }
    }

    private async Task FadeVolumeAsync(
    float from,
    float to,
    int durationMs,
    CancellationToken token)
    {
        const int steps = 30;
        int delay = durationMs / steps;

        for (int i = 0; i <= steps; i++)
        {
            token.ThrowIfCancellationRequested();

            float t = (float)i / steps;
            float value = from + (to - from) * t;

            if (_deviceOutputNode != null)
                _deviceOutputNode.OutgoingGain = value;

            await Task.Delay(delay, token);
        }
    }

    private async Task FadeInAsync(float targetVolume)
    {
        // Temporary diagnostic bypass: keep the output at its target gain and remove
        // every asynchronous fade from the playback transition.
        if (_deviceOutputNode is not null)
            _deviceOutputNode.OutgoingGain = targetVolume;
        await Task.CompletedTask;
        return;

        await _fadeTransitionLock.WaitAsync();
        try
        {
            LogStemDiagnostic($"fade-in-begin-target-{targetVolume:F3}");
            if (_deviceOutputNode != null)
                _deviceOutputNode.OutgoingGain = 0f;

            await FadeVolumeAsync(
                from: 0f,
                to: targetVolume,
                durationMs: FadeDurationMs,
                token: CancellationToken.None);
            LogStemDiagnostic("fade-in-complete");
        }
        finally
        {
            _fadeTransitionLock.Release();
        }
    }

    private async Task FadeOutAsync()
    {
        // Temporary diagnostic bypass: source switching must not wait for or be
        // affected by a device-output fade.
        if (_deviceOutputNode is not null)
            _deviceOutputNode.OutgoingGain = 0f;
        await Task.CompletedTask;
        return;

        await _fadeTransitionLock.WaitAsync();
        try
        {
            float current = ((float?)_deviceOutputNode?.OutgoingGain) ?? 0f;
            LogStemDiagnostic($"fade-out-begin-current-{current:F3}");

            await FadeVolumeAsync(
                from: current,
                to: 0f,
                durationMs: FadeDurationMs,
                token: CancellationToken.None);
            LogStemDiagnostic("fade-out-complete");
        }
        finally
        {
            _fadeTransitionLock.Release();
        }
    }

    private double _lastVolume { get; set; }

    public async Task PauseAsync()
    {
        await _playbackTransitionLock.WaitAsync();
        try
        {
            await PauseCoreAsync();
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private async Task PauseCoreAsync()
    {
        if (_playState != AudioPlayerState.Playing)
            return;

        LogStemDiagnostic("pause-before");
        _lastVolume = Volume;
        SetPlayState(AudioPlayerState.Paused);
        await FadeOutAsync();

        _audioGraph.Stop();
        StopAudioAnalysis();

        _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;

        // Update taskbar to paused state
        if (TrackDuration != TimeSpan.Zero)
        {
            double percentage = _lastPosition.TotalSeconds / TrackDuration.TotalSeconds;
            percentage = Math.Max(0, Math.Min(1, percentage));
            _taskbarService.SetProgress(percentage, TaskbarProgressState.Paused);
        }

        LogStemDiagnostic("pause-after");

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
            await StopCoreAsync();
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private async Task<TimeSpan?> StopForSourceSwitchAsync()
    {
        await _playbackTransitionLock.WaitAsync();
        try
        {
            return await StopCoreAsync(preserveHandoffPosition: true);
        }
        finally
        {
            _playbackTransitionLock.Release();
        }
    }

    private async Task<TimeSpan?> StopCoreAsync(bool preserveHandoffPosition = false)
    {
        if (_playState == AudioPlayerState.Idle && _audioInputNode is null && !IsStemMixing)
            return null;

        _lastVolume = Volume;
        LogAudioGraphState("stop-before-fade");

        await FadeOutAsync();

        // Read it after FadeOutAsync: this is the final position that was rendered by
        // the outgoing source, not the older UI value from the click event.
        // Audio input-node positions are decoder cursors and can be substantially
        // different from the audible output. The frame-output clock is authoritative.
        var handoffPosition = _lastPosition;
        AppLog.Information($"StemDiag source-switch-handoff. Preserve='{preserveHandoffPosition}', HandoffMs='{handoffPosition.TotalMilliseconds:F0}', LastTimelineMs='{_lastPosition.TotalMilliseconds:F0}'.");

        StopAudioAnalysis();
        _audioGraph.Stop();
        ReleaseInputNodes();
        _audioGraph.ResetAllNodes();
        LogAudioGraphState("stop-after-reset");

        if (!preserveHandoffPosition)
        {
            _lastPosition = TimeSpan.Zero;
            _totalSamplesProcessed = 0;
        }

        _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;

        SetPlayState(AudioPlayerState.Idle);

        // Clear taskbar progress on stop
        _taskbarService.ClearProgress();
        return preserveHandoffPosition ? handoffPosition : null;
    }

    public async Task ChangeOutputDeviceAsync(DeviceInformation newDevice)
    {
        ArgumentNullException.ThrowIfNull(newDevice);

        AppLog.Information($"Audio output change requested. Name='{newDevice.Name}', Id='{newDevice.Id}', State='{_playState}'.");
        await _outputDeviceChangeLock.WaitAsync();
        try
        {
            var previousState = _playState;
            var currentSource = _currentTrackPath;
            var wasStemMixing = IsStemMixing;
            var vocalsSourcePath = _vocalsSourcePath;
            var instrumentalSourcePath = _instrumentalSourcePath;
            var resumePosition = _lastPosition;
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
                    if (wasStemMixing && vocalsSourcePath is not null && instrumentalSourcePath is not null)
                    {
                        var vocalsSource = await CreateMediaSourceAsync(vocalsSourcePath);
                        var instrumentalSource = await CreateMediaSourceAsync(instrumentalSourcePath);
                        var vocalsResult = await newGraph.CreateMediaSourceAudioInputNodeAsync(vocalsSource);
                        var instrumentalResult = await newGraph.CreateMediaSourceAudioInputNodeAsync(instrumentalSource);
                        if (vocalsResult.Status != MediaSourceAudioInputNodeCreationStatus.Success ||
                            instrumentalResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                            throw new InvalidOperationException("Unable to restore the audio stems on the selected device.");

                        newVocalsInputNode = vocalsResult.Node;
                        newInstrumentalInputNode = instrumentalResult.Node;
                        newStemSubmixNode = newGraph.CreateSubmixNode();
                        newVocalsInputNode.AddOutgoingConnection(newStemSubmixNode);
                        newInstrumentalInputNode.AddOutgoingConnection(newStemSubmixNode);
                        newStemSubmixNode.AddOutgoingConnection(newOutputNode);
                        newStemSubmixNode.AddOutgoingConnection(newFrameNode);
                        newVocalsInputNode.OutgoingGain = (float)Math.Min(1d, 1d + _stemMixBalance);
                        newInstrumentalInputNode.OutgoingGain = (float)Math.Min(1d, 1d - _stemMixBalance);
                        TrackDuration = GetShortestDuration(vocalsSource.Duration ?? newVocalsInputNode.Duration,
                            instrumentalSource.Duration ?? newInstrumentalInputNode.Duration);
                        resumePosition = ClampPosition(resumePosition, TrackDuration);
                        newVocalsInputNode.Seek(resumePosition);
                        newInstrumentalInputNode.Seek(resumePosition);
                    }
                    else
                    {
                        var source = await CreateMediaSourceAsync(currentSource);
                        var inputResult = await newGraph.CreateMediaSourceAudioInputNodeAsync(source);
                        if (inputResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                            throw new InvalidOperationException("Unable to restore the current track on the selected device.");

                        newInputNode = inputResult.Node;
                        newInputNode.AddOutgoingConnection(newOutputNode);
                        newInputNode.AddOutgoingConnection(newFrameNode);
                        TrackDuration = source.Duration ?? newInputNode.Duration;
                        resumePosition = ClampPosition(resumePosition, newInputNode.Duration);
                        newInputNode.Seek(resumePosition);
                    }
                }
            }
            catch
            {
                newGraph.Dispose();
                throw;
            }

            if (previousState == AudioPlayerState.Playing)
                await FadeOutAsync();

            StopAudioAnalysis();
            if (previousGraph is not null)
                previousGraph.QuantumStarted -= AudioGraph_QuantumStarted;
            previousGraph?.Stop();
            previousGraph?.Dispose();

            _audioGraph = newGraph;
            _deviceOutputNode = newOutputNode;
            _frameOutputNode = newFrameNode;
            _audioGraph.QuantumStarted += AudioGraph_QuantumStarted;
            _audioInputNode = newInputNode;
            _vocalsInputNode = newVocalsInputNode;
            _instrumentalInputNode = newInstrumentalInputNode;
            _stemSubmixNode = newStemSubmixNode;
            _lastPosition = resumePosition;
            _totalSamplesProcessed = (long)(resumePosition.TotalSeconds * _audioGraph.EncodingProperties.SampleRate);
            _sampleRate = (int)_audioGraph.EncodingProperties.SampleRate;
            _channelCount = (int)_audioGraph.EncodingProperties.ChannelCount;

            await InitializeSmtcAsync();

            if (newInputNode is null && newVocalsInputNode is null)
                return;

            UpdateSmtcTimeline(resumePosition);
            UpdateTaskbarProgress(resumePosition);

            if (previousState == AudioPlayerState.Playing)
            {
                _audioGraph.Start();
                StartAudioAnalysis();
        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                SetPlayState(AudioPlayerState.Playing);
                await FadeInAsync((float)Volume);
            }
            else
            {
        _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
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
            _outputDeviceChangeLock.Release();
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
            AppLog.Information($"StemDiag seek-ignored-during-source-switch. TargetMs='{position.TotalMilliseconds:F0}'.");
            return;
        }

        if (_audioInputNode == null && !IsStemMixing)
            return;

        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        if (position > TrackDuration)
            position = TrackDuration;

        AppLog.Information($"StemDiag seek-request. TargetMs='{position.TotalMilliseconds:F0}', State='{_playState}', IsStemMixing='{IsStemMixing}'.");
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
            _lastStemDiagnosticSecond = (int)position.TotalSeconds;
        }

        UpdateSmtcTimeline(position);
        UpdateTaskbarProgress(position);
        LogStemDiagnostic("seek-applied");
    }

    private void StartAudioAnalysis()
    {
        if (_analysisRunning)
            return;

        _analysisRunning = true;
    }

    private bool _analysisRunning;


    private void StopAudioAnalysis()
    {
        _analysisRunning = false;
    }

    private void AudioGraph_QuantumStarted(AudioGraph sender, object args)
    {
        if (!_analysisRunning ||
            Volatile.Read(ref _isSourceSwitching) != 0 ||
            !ReferenceEquals(sender, _audioGraph))
            return;

        try
        {
            // GetFrame returns the audio accumulated since the previous read. It must
            // be consumed from QuantumStarted, not an arbitrary wall-clock timer, so
            // the timeline follows the actual render quanta after a seek/switch.
            using var frame = _frameOutputNode.GetFrame();
            ProcessAudioFrame(frame);
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _frameProcessingFailureLogged, 1) == 0)
                AppLog.Error("Audio frame processing failed in AudioGraph.QuantumStarted.", exception);
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
        _taskbarService.SetProgress(percentage, TaskbarProgressState.Normal);

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


    private unsafe void ProcessAudioFrame(AudioFrame frame)
    {
        if (!_analysisRunning || Volatile.Read(ref _isSourceSwitching) != 0)
            return;

        using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        var byteAccess = reference.As<IMemoryBufferByteAccess>();

        byte* dataInBytes;
        uint capacity;
        byteAccess.GetBuffer(out dataInBytes, out capacity);

        int floatCount = (int)capacity / sizeof(float);
        int samplesPerChannel = floatCount / _channelCount;

        TimeSpan position;
        lock (_timelineSynchronization)
        {
            if (!_analysisRunning || Volatile.Read(ref _isSourceSwitching) != 0)
                return;

            // Timeline is committed atomically with respect to a seek/source switch.
            _totalSamplesProcessed += samplesPerChannel;
            position = TimeSpan.FromSeconds((double)_totalSamplesProcessed / _sampleRate);
            _lastPosition = position;
        }

        if (IsStemMixing)
        {
            var wholeSecond = (int)position.TotalSeconds;
            if (wholeSecond != _lastStemDiagnosticSecond)
            {
                _lastStemDiagnosticSecond = wholeSecond;
                LogStemDiagnostic("render-tick");
            }
        }

        UpdateTaskbarProgress(position);

        PositionUpdated?.Invoke(
            this,
            new PositionUpdatedEventArgs(position)
        );

        UpdateSmtcTimeline(position);

        if (TrackDuration != TimeSpan.Zero && position >= TrackDuration)
            QueueTrackEndTransition();
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
                        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                        return;
                    }

                    StopAudioAnalysis();
                    _audioGraph.Stop();
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
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

