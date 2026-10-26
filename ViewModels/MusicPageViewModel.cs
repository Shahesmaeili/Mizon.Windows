using FluentIcons.Common;
using Microsoft.UI.Dispatching;
using Mizon.API;
using Mizon.API.Client.Operations.Musics;
using Mizon.API.Client.Requests.Musics;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using Color = Windows.UI.Color;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Responses.Media;
using System.IO;
using System.Buffers;
using Mizon.API.Client.Requests.Media;
using Mizon.API.Client.Operations.Stories;
using Mizon.API.Client.Requests.Stories;

namespace Mizon.Windows.ViewModels;

public enum PlayState
{
    Playing, Paused, Idle,
}

public class MusicPageViewModel : MainViewModel
{
    private readonly TeachingTipService _teachingTipService;
    private readonly IShortcutService _shortcutService;
    private readonly Mizon.API.Client.Operations.Media.MediaDownloader _mediaDownloader;
    private readonly RgbMusicLightingService _rgbMusicLightingService;

    private DeviceWatcher? _audioRenderDeviceWatcher;
    private readonly DispatcherQueueTimer _audioDeviceDetailsTimer;
    private readonly DispatcherQueueTimer _musicListenerPositionTimer;
    private bool _isAudioRenderWatcherRunning;
    private readonly HashSet<string> _batteryCheckedDeviceIds = [];
    private readonly HashSet<Guid> _musicListenerAvatarMediaIds = [];
    private readonly Dictionary<Guid, string> _musicListenerAvatarPaths = [];
    private long _lastMusicPresencePublishTick;
    private TimeSpan _lastMusicPosition;
    public RelayCommand PlayCommand { get; }
    public RelayCommand CopyLinkCommand { get; }
    public RelayCommand DesktopShortcutCommand { get; }
    public RelayCommand ReactionCommand { get; }



    private readonly DispatcherQueue _dispatcherQueue;
    public Func<double, string> EmptyLabeler => _ => "";

    private Guid _musicPublicId { get; set; }
    private CancellationTokenSource? _musicLoadCancellationTokenSource;
    private Guid _musicFilePublicId { get; set; }
    private Guid _musicMediaPublicId { get; set; }
    private TimeSpan _musicDuration;
    private MediaResponse? _musicMediaResponse;
    private Task? _musicDownloadTask;
    private bool _isPlaybackTransitioning;
    private bool _isStemMixerSwitching;
    private int _stemMixBalanceUpdateVersion;
    private Guid? _vocalsMediaPublicId;
    private Guid? _instrumentalMediaPublicId;
    private string? _vocalsCachedPath;
    private string? _instrumentalCachedPath;
    private readonly SemaphoreSlim _musicPlaylistMembershipLock = new(1, 1);

    public ObservableCollection<StoryItemViewModel> Stories { get; } = [];

    public bool IsMusicDownloading
    {
        get => field;
        set => SetField(ref field, value);
    }

    public double MusicDownloadProgress
    {
        get => field;
        set => SetField(ref field, value);
    }

    public double[] Values { get; set; } =
       [2, 1, 3, 4, 3, 4, 6];
    public string Title
    {
        get => field;
        set => SetField(ref field, value);
    }

    public string? PersianTitle
    {
        get => field;
        set => SetField(ref field, value);
    }

    public string? AlbumTitle
    {
        get => field;
        set => SetField(ref field, value);
    }

    public Guid? AlbumPublicId
    {
        get => field;
        set => SetField(ref field, value);
    }

    private string? _coverImagePath;
    public string? CoverImagePath
    {
        get => _coverImagePath;
        set => SetField(ref _coverImagePath, value);
    }

    private double _coverDownloadProgress;
    public double CoverDownloadProgress
    {
        get => _coverDownloadProgress;
        set => SetField(ref _coverDownloadProgress, value);
    }

    private double _coverDownloadSpeed;
    public double CoverDownloadSpeed
    {
        get => _coverDownloadSpeed;
        set => SetField(ref _coverDownloadSpeed, value);
    }

    public Guid? LablePublicId
    {
        get => field;
        set => SetField(ref field, value);
    }


    public string? LableTitle
    {
        get => field;
        set => SetField(ref field, value);
    }

    public int ViewsCount
    {
        get => field;
        set => SetField(ref field, value);
    }

    public int LiveViewsCount
    {
        get => field;
        set => SetField(ref field, value);
    }

    public int LikesCount
    {
        get => field;
        set => SetField(ref field, value);
    }

    public PlayState PlayType
    {
        get => field;
        set => SetField(ref field, value);
    } = PlayState.Idle;

    public bool IsPlaying
    {
        get => field;
        set => SetField(ref field, value);
    }

    public string CurrentPosition
    {
        get => field;
        set => SetField(ref field, value);
    } = "0:00:00";

    public string Duration
    {
        get => field;
        set => SetField(ref field, value);
    }

    public float ServerWaveformIntensity
    {
        get => field;
        set => SetField(ref field, value);
    }

    public double Volume
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
                _audioPlayerService.Volume = value / 100d;
        }
    } = 100;

    public bool IsStemMixAvailable
    {
        get => field;
        private set => SetField(ref field, value);
    }

    public bool IsStemMixerLoading
    {
        get => field;
        private set => SetField(ref field, value);
    }

    private bool _isStemMixerEnabled;
    public bool IsStemMixerEnabled
    {
        get => _isStemMixerEnabled;
        set
        {
            if (!SetField(ref _isStemMixerEnabled, value) || _musicPublicId == Guid.Empty)
                return;

            _ = SwitchStemMixerAsync(value, _musicPublicId);
        }
    }

    public double StemMixBalance
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
                QueueStemMixBalanceUpdate(value);
        }
    }

    private void QueueStemMixBalanceUpdate(double value)
    {
        var version = Interlocked.Increment(ref _stemMixBalanceUpdateVersion);
        _ = ApplyStemMixBalanceAsync(value, version);
    }

    private async Task ApplyStemMixBalanceAsync(double value, int version)
    {
        // Slider bindings update for every pointer pixel. Coalescing prevents a rapid
        // stream of cross-thread AudioGraph gain changes while preserving the last value.
        await Task.Delay(TimeSpan.FromMilliseconds(33));
        if (version != Volatile.Read(ref _stemMixBalanceUpdateVersion))
            return;

        AppLog.Information($"StemDiag slider-apply. Value='{value:F2}', Version='{version}', MusicPublicId='{_musicPublicId}'.");
        _audioPlayerService.StemMixBalance = value / 100d;
    }

    public bool IsReverbEffectDefinitionEnabled
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
            {
                if (value)
                    _audioPlayerService.AddReverbEffectDefinition();
                else
                    _audioPlayerService.RemoveReverbEffectDefinition();
            }
        }
    } = false;

    public bool IsRepeatEnabled
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
            {
                _audioPlayerService.AudioPlayeRepeatMode =
                    value ? AudioPlayeRepeatMode.RepeatOne : AudioPlayeRepeatMode.Off;
            }
        }
    }

    public MusicPageViewModel(MizonApi mizonApi,
        MizonSocket mizonSocket,
        NavigationService navigationService,
        DispatcherQueue dispatcherQueue,
        IAudioPlayerService audioPlayerService,
        Mizon.API.Client.Operations.Media.MediaDownloader downloader,
        RgbMusicLightingService rgbMusicLightingService,
        IShortcutService shortcutService,
        TeachingTipService teachingTipService)
        : base(mizonApi, mizonSocket, navigationService, audioPlayerService)
    {
        _mediaDownloader = downloader;
        _rgbMusicLightingService = rgbMusicLightingService;
        _dispatcherQueue = dispatcherQueue;
        _audioDeviceDetailsTimer = _dispatcherQueue.CreateTimer();
        _audioDeviceDetailsTimer.Interval = TimeSpan.FromSeconds(1);
        _audioDeviceDetailsTimer.Tick += AudioDeviceDetailsTimer_Tick;
        _musicListenerPositionTimer = _dispatcherQueue.CreateTimer();
        _musicListenerPositionTimer.Interval = TimeSpan.FromMilliseconds(50);
        _musicListenerPositionTimer.Tick += MusicListenerPositionTimer_Tick;
        NavigationService.InternalNavigationChanged += NavigationService_NavigationChanged;
        MizonSocket.EntityViewersCountChanged += MizonSocket_EntityViewersCountChanged;
        MizonSocket.MusicListenersSnapshotReceived += MizonSocket_MusicListenersSnapshotReceived;
        MizonSocket.MusicListenerUpdated += MizonSocket_MusicListenerUpdated;
        MizonSocket.MusicListenerRemoved += MizonSocket_MusicListenerRemoved;
        PlayCommand = new RelayCommand(PlayAsync);
        CopyLinkCommand = new RelayCommand(CopyLinkAsync);
        DesktopShortcutCommand = new RelayCommand(DesktopShortcutAsync);
        ReactionCommand = new RelayCommand(ReactionAsync);

        _audioPlayerService.PositionUpdated += AudioPlayerService_PositionUpdated;
        _audioPlayerService.StateUpdated += AudioPlayerService_StateUpdated;
        _teachingTipService = teachingTipService;
        _shortcutService = shortcutService;
    }

    private void AudioPlayerService_StateUpdated(object? sender, StateUpdatedEventArgs e)
    {
        PublishMusicPlaybackPresence(_lastMusicPosition, force: true);

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (e.AudioPlayerState == AudioPlayerState.Playing)
            {
                _rgbMusicLightingService.Resume();
                IsPlaying = true;
                PlayType = PlayState.Playing;
                _lastSecond = -1;
            }
            else
            {
                IsPlaying = false;
                ServerWaveformIntensity = 0f;
                PlayType = e.AudioPlayerState == AudioPlayerState.Paused
                    ? PlayState.Paused
                    : PlayState.Idle;

                if (e.AudioPlayerState == AudioPlayerState.Paused)
                    _rgbMusicLightingService.SetPaused();
                else
                    _rgbMusicLightingService.SetIdle();
            }

            UpdateColors();
        });

    }

    private int _lastSecond = -1;
    private int _lastLyricPositionInMilliseconds = -1;
    private MusicLyricLineItemViewModel? _activeLyricLine;
    private MusicLyricWordItemViewModel? _activeLyricWord;

    private void AudioPlayerService_PositionUpdated(object? sender, PositionUpdatedEventArgs e)
    {
        // Use high-resolution position (milliseconds) and map to Bars index for smooth sync.
        var position = e.Position;
        _lastMusicPosition = position;
        _rgbMusicLightingService.Update(position, IsPlaying);
        UpdateCoverPulseFromServerWaveform(position);
        PublishMusicPlaybackPresence(position, force: false);

        var lyricPositionInMilliseconds = (int)Math.Max(0, position.TotalMilliseconds);
        if (Math.Abs(lyricPositionInMilliseconds - _lastLyricPositionInMilliseconds) >= 50)
        {
            _lastLyricPositionInMilliseconds = lyricPositionInMilliseconds;
            _dispatcherQueue.TryEnqueue(() => UpdateLyricPlaybackState(lyricPositionInMilliseconds));
        }

        // If track duration is available, compute progress-based index.
        int playedIndex = 0;

        if (_audioPlayerService.TrackDuration != TimeSpan.Zero)
        {
            double totalMs = _audioPlayerService.TrackDuration.TotalMilliseconds;
            double posMs = position.TotalMilliseconds;

            double progress = totalMs > 0 ? (posMs / totalMs) : 0.0;
            progress = Math.Clamp(progress, 0.0, 1.0);

            playedIndex = (int)Math.Round(progress * (BarCount - 1));
        }
        else if (_waveforms?.Count > 0)
        {
            // Fallback: map to waveform offsets if TrackDuration missing
            int idx = _waveforms.FindIndex(w => w.OffsetInMilliseconds >= position.TotalMilliseconds);
            if (idx < 0) idx = _waveforms.Count - 1;

            playedIndex = (int)((double)idx * BarCount / Math.Max(1, _waveforms.Count) );
        }

        // Avoid flooding UI thread: only update if index changed
        if (playedIndex == _lastSecond)
            return;

        _lastSecond = playedIndex;

        _dispatcherQueue.TryEnqueue(() =>
        {
            CurrentPosition = position.ToString(@"h\:mm\:ss");
            UpdatePlayedPosition(playedIndex);
        });
    }
    private const double WaveformContentWidth = 1500d;
    private const double CommentMarkerSize = 28d;
    private const double MusicListenerMarkerSize = 28d;
    private const double TopBarMarkerCanvasTop = 22d;

    // ارتفاع waveform برابر 110 و ردیف پایینی یک‌سوم آن است. این نقطه مرکز
    // نوارهای پایین است؛ پس مرکز آواتار دقیقاً روی همان نوار قرار می‌گیرد.
    private const double BottomBarMarkerCenterY = 92d;

    private void LoadHardCodedVocalSegments()
    {
        VocalSegments.Clear();
        VocalSegments.Add(new()
        {
            CanvasLeft = 20,
            Width = 320,
            Color = new SolidColorBrush(Color.FromArgb(220, 255, 77, 109))
        });
        VocalSegments.Add(new()
        {
            CanvasLeft = 370,
            Width = 290,
            Color = new SolidColorBrush(Color.FromArgb(220, 0, 180, 255))
        });
        VocalSegments.Add(new()
        {
            CanvasLeft = 630,
            Width = 120,
            Color = new SolidColorBrush(Color.FromArgb(220, 255, 193, 7))
        });
        VocalSegments.Add(new()
        {
            CanvasLeft = 1010,
            Width = 420,
            Color = new SolidColorBrush(Color.FromArgb(220, 156, 39, 176))
        });
    }

    private void PublishMusicPlaybackPresence(TimeSpan position, bool force)
    {
        if (_musicPublicId == Guid.Empty)
            return;

        var nowTick = Environment.TickCount64;
        if (!force && nowTick - _lastMusicPresencePublishTick < 350)
            return;

        _lastMusicPresencePublishTick = nowTick;
        _ = MizonSocket.PublishMusicPlaybackAsync(
            _musicPublicId,
            position,
            _audioPlayerService.AudioPlayerState == AudioPlayerState.Playing);
    }
    private int _maxZIndex = 0;

    public void EqualizerCommentBringToFront(EqualizerCommentItemViewModel equalizerCommentItemViewModel)
    {
        foreach (var btn in EqualizerComments)
        {
            if (btn != equalizerCommentItemViewModel)
            {
                btn.ZIndex--;
            }
        }

        _maxZIndex++;
        equalizerCommentItemViewModel.ZIndex = _maxZIndex;
    }
    public AudioDeviceItemViewModel? SelectedAudioDevice
    {
        get => field;
        set
        {
            if (SetField(ref field, value) && value is not null)
                _ = ChangeOutputDeviceAsync(value);
        }
    }



    public ObservableCollection<AudioDeviceItemViewModel> AudioDevices { get; } = [];
    public ObservableCollection<EqualizerBarItemViewModel> Bars { get; } = [];
    public ObservableCollection<EqualizerCommentItemViewModel> EqualizerComments { get; } = [];
    public ObservableCollection<MusicListenerMarkerItemViewModel> MusicListeners { get; } = [];
    public ObservableCollection<MusicVocalSegmentItemViewModel> VocalSegments { get; } = [];
    public ObservableCollection<MusicGenreItemViewModel> Genres { get; } = [];
    public ObservableCollection<MusicGenreTypeItemViewModel> MusicGenreTypes { get; } = [];
    public ObservableCollection<MusicDetailsMusicItemViewModel> AlbumMusics { get; } = [];
    public ObservableCollection<MusicDetailsMusicItemViewModel> ContributorMusics { get; } = [];
    public ObservableCollection<MusicDetailsCommentItemViewModel> MusicDetailsComments { get; } = [];

    public ObservableCollection<MusicReactionItemViewModel> Reactions { get; } = [];
    public ObservableCollection<MusicContributorItemViewModel> Contributors { get; } = new();
    public ObservableCollection<MusicPlaylistItemViewModel> MusicPlaylists { get; } = [];
    public ObservableCollection<MusicLyricLineItemViewModel> Lyrics { get; } = [];

    public bool HasMusicAlbum
    {
        get => field;
        private set => SetField(ref field, value);
    }

    public bool IsAlbumDetailsSelected
    {
        get => field;
        private set => SetField(ref field, value);
    }

    public bool IsContributorDetailsSelected
    {
        get => field;
        private set => SetField(ref field, value);
    }

    public bool IsCommentsDetailsSelected
    {
        get => field;
        private set => SetField(ref field, value);
    }

    public bool HasLyrics
    {
        get => field;
        private set => SetField(ref field, value);
    }

    public string CurrentLyricText
    {
        get => field;
        private set => SetField(ref field, value);
    } = string.Empty;

    private const int BarCount = 250;
    private const int MaxHeight = 70;
    private const int MinHeight = 7;

    private static readonly SolidColorBrush NormalBrush =
       new(Color.FromArgb(120, 255, 255, 255));

    private static readonly SolidColorBrush PlayedBrush =
        new(Color.FromArgb(255, 0, 180, 255)); // آبی ملایم

    private static readonly SolidColorBrush PausedBrush =
    new(Color.FromArgb(180, 230, 255, 13)); // آبی ملایم

    private static readonly SolidColorBrush HoverBrush =
        new(Color.FromArgb(180, 30, 144, 255));  // آبی پررنگ‌تر

    private static readonly SolidColorBrush BottomNormalBrush =
        new(Color.FromArgb(50, 255, 255, 255));

    private static readonly SolidColorBrush BottomHoverBrush =
        new(Color.FromArgb(220, 255, 77, 109));

    // Cached waveform frames fetched from server (used to map play position to bar index)
    private List<Mizon.API.Client.Responses.Musics.WaveformResponse> _waveforms = new();
    private long _lastCoverPulseUpdateTick;

    private void UpdateCoverPulseFromServerWaveform(TimeSpan position)
    {
        if (!IsPlaying || _waveforms.Count == 0)
            return;

        var tick = Environment.TickCount64;
        if (tick - _lastCoverPulseUpdateTick < 33)
            return;

        _lastCoverPulseUpdateTick = tick;
        var waveform = _waveforms[FindWaveformIndex((int)Math.Max(0d, position.TotalMilliseconds))];
        // Keep the original cover-motion amplitude: it was driven by a raw RMS value.
        // Only the source changed; it now comes from the server waveform frame.
        var intensity = Math.Clamp(Math.Abs(waveform.RMS), 0f, 1f);

        _dispatcherQueue.TryEnqueue(() => ServerWaveformIntensity = intensity);
    }

    private int FindWaveformIndex(int offsetInMilliseconds)
    {
        var low = 0;
        var high = _waveforms.Count - 1;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (_waveforms[middle].OffsetInMilliseconds <= offsetInMilliseconds)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }



    private void GenerateFallbackBars()
    {
        Bars.Clear();
        var random = new Random();

        for (int i = 0; i < BarCount; i++)
        {
            int topHeight = random.Next(MinHeight, MaxHeight);

            Bars.Add(new EqualizerBarItemViewModel
            {
                TopHeight = topHeight,
                BottomHeight = (int)(topHeight * 0.5),
                TopColor = NormalBrush,
                BottomColor = BottomNormalBrush
            });
        }
    }


    private void UpdateColors()
    {
        foreach (var bar in Bars)
        {
            if (bar.IsHover)
                bar.TopColor = HoverBrush;
            else if (bar.IsPlayed)
            {
                if (PlayType == PlayState.Playing)
                    bar.TopColor = PlayedBrush;
                else bar.TopColor = PausedBrush;
            }

            else
                bar.TopColor = NormalBrush;
        }
    }

    public void UpdateHoverPreview(int hoverIndex)
    {
        for (int i = 0; i < Bars.Count; i++)
            Bars[i].IsHover = i <= hoverIndex;

        UpdateColors();
    }

    public void ClearHoverPreview()
    {
        foreach (var bar in Bars)
            bar.IsHover = false;

        UpdateColors();
    }

    public void UpdateBottomBarHover(EqualizerBarItemViewModel? hoveredBar)
    {
        foreach (var bar in Bars)
        {
            var isHovered = ReferenceEquals(bar, hoveredBar);
            if (bar.IsBottomHover == isHovered)
                continue;

            bar.IsBottomHover = isHovered;
            bar.BottomColor = isHovered ? BottomHoverBrush : BottomNormalBrush;
        }
    }

    public TimeSpan GetCommentTimestampForBarIndex(int index)
    {
        var duration = _audioPlayerService.TrackDuration > TimeSpan.Zero
            ? _audioPlayerService.TrackDuration
            : _musicDuration;

        if (duration <= TimeSpan.Zero || Bars.Count <= 1)
            return TimeSpan.Zero;

        var clampedIndex = Math.Clamp(index, 0, Bars.Count - 1);
        var progress = (double)clampedIndex / (Bars.Count - 1);
        return TimeSpan.FromMilliseconds(duration.TotalMilliseconds * progress);
    }

    public async Task<bool> CreateMusicCommentAsync(string text, int barIndex, CancellationToken cancellationToken = default)
    {
        var trimmedText = text.Trim();
        var musicPublicId = _musicPublicId;
        if (musicPublicId == Guid.Empty || string.IsNullOrWhiteSpace(trimmedText))
            return false;

        var response = await MizonApi.SendRequestAsync(new MusicCommentCreateOperation(new()
        {
            MusicPublicId = musicPublicId,
            Text = trimmedText,
            Timestamp = GetCommentTimestampForBarIndex(barIndex)
        }), cancellationToken);

        if (!response.IsSuccess || _musicPublicId != musicPublicId)
            return false;

        await GetMusicCommentsAsync(musicPublicId, cancellationToken);
        return true;
    }
    public void SeekByProgress(TimeSpan position, int index)
    {
        // Compute correct target position based on bar index to ensure sync.
        if (_audioPlayerService.TrackDuration != TimeSpan.Zero)
        {
            int clampedIndex = Math.Max(0, Math.Min(index, BarCount - 1));
            double progress = (double)clampedIndex / Math.Max(1, BarCount - 1);
            var target = TimeSpan.FromMilliseconds(_audioPlayerService.TrackDuration.TotalMilliseconds * progress);
            _audioPlayerService.SeekTo(target);
            UpdatePlayedPosition(clampedIndex);
            return;
        }

        // Fallback: if we have waveform offsets, map bar index to nearest waveform offset
        if (_waveforms is { Count: > 0 })
        {
            int clampedIndex = Math.Max(0, Math.Min(index, BarCount - 1));
            int wfIndex = (int)((double)clampedIndex * _waveforms.Count / Math.Max(1, BarCount));
            if (wfIndex >= _waveforms.Count) wfIndex = _waveforms.Count - 1;

            var offsetMs = _waveforms[wfIndex].OffsetInMilliseconds;
            var target = TimeSpan.FromMilliseconds(offsetMs);
            _audioPlayerService.SeekTo(target);
            UpdatePlayedPosition(clampedIndex);
            return;
        }

        // If no duration and no waveform data, do nothing.
    }

    private void UpdatePlayedPosition(int playedIndex)
    {
        for (int i = 0; i < Bars.Count; i++)
            Bars[i].IsPlayed = i <= playedIndex;

        UpdateColors();
    }

    private void NavigationService_NavigationChanged(object? sender, NavigationChangedEventArgs e)
    {
        if (e.IsEnteringPage)
        {
            if (e.ToPageType == typeof(MusicPage))
            {
                _ = GetMusicGenreTypesAsync();
                return;
            }

            if (e.ToPageType == typeof(MusicsPage))
            {
                if (e.Parameter?.TryGetValue(PageConstants.MUSIC_ID, out object? music_public_id) == true)
                {
                    if (Guid.TryParse(music_public_id?.ToString(), out Guid musicPublicId))
                    {
                        // The page/view-model is cached. Navigating to the same
                        // song again must not reset playback or re-fetch data.
                        if (musicPublicId == _musicPublicId)
                            return;

                        _musicLoadCancellationTokenSource?.Cancel();
                        _musicLoadCancellationTokenSource?.Dispose();
                        _musicLoadCancellationTokenSource = new CancellationTokenSource();

                        ResetMusicPresentation();
                        _musicPublicId = musicPublicId;
                        _ = LoadMusicAsync(musicPublicId, _musicLoadCancellationTokenSource.Token);
                    }
                    else
                    {
                        NavigationService.InternalGoBack();
                        return;
                    }
                }
                else
                {
                    NavigationService.InternalGoBack();
                    return;
                }

            }
        }
        else
        {
            _musicLoadCancellationTokenSource?.Cancel();
            StopAudioRenderWatching();
            _musicListenerPositionTimer.Stop();
            MusicListeners.Clear();
            _musicListenerAvatarMediaIds.Clear();
            _musicListenerAvatarPaths.Clear();
            // leave current entity group
            _ = MizonSocket.LeaveEntityAsync();

        }
    }

    private async Task LoadMusicAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        try
        {
            _ = GetMusicAsync(musicPublicId, cancellationToken);
            _ = GetMusicLyricAsync(musicPublicId, cancellationToken);
            _ = GetMusicContributorsAsync(musicPublicId, cancellationToken);
            _ = GetMusicReactionsAsync(musicPublicId, cancellationToken);
            _ = GetMusicStoriesAsync(musicPublicId, cancellationToken);
            _ = ViewAsync(musicPublicId, cancellationToken);

            await GetMusicFileAsync(musicPublicId, cancellationToken);
            if (!IsCurrentMusicLoad(musicPublicId, cancellationToken))
                return;

            await GetMusicStemMixAsync(musicPublicId, cancellationToken);
            if (!IsCurrentMusicLoad(musicPublicId, cancellationToken))
                return;

            await GetMusicFileWaveformAsync(musicPublicId, _musicFilePublicId, cancellationToken);
            if (!IsCurrentMusicLoad(musicPublicId, cancellationToken))
                return;

            _ = LoadAudioRenders();
            StartAudioRenderWatching();
            LoadHardCodedVocalSegments();
            await GetMusicCommentsAsync(musicPublicId, cancellationToken);
            if (IsCurrentMusicLoad(musicPublicId, cancellationToken))
                _musicListenerPositionTimer.Start();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer music navigation superseded this load session.
        }
        catch (Exception exception)
        {
            AppLog.Error($"Music page load failed. MusicPublicId='{musicPublicId}'.", exception);
        }
    }

    private async Task GetMusicGenreTypesAsync()
    {
        var response = await MizonApi.SendRequestAsync(new MusicGenreTypesOperation(new()));
        if (!response.IsSuccess)
            return;

        MusicGenreTypes.Clear();
        response.Content!.MusicGenreTypes.ForEach(musicGenreType =>
        {
            MusicGenreTypes.Add(new()
            {
                PublicId = musicGenreType.PublicId,
                Title = musicGenreType.Title,
                PersianTitle = musicGenreType.PersianTitle,
                AccentColorHex = musicGenreType.AccentColorHex,
                IconSymbolName = musicGenreType.IconSymbol,
                MusicsCount = musicGenreType.MusicsCount
            });
        });
    }

    public Task SelectAlbumDetailsAsync()
    {
        if (!HasMusicAlbum)
            return Task.CompletedTask;

        IsAlbumDetailsSelected = true;
        IsContributorDetailsSelected = false;
        IsCommentsDetailsSelected = false;
        return GetAlbumMusicsAsync(_musicPublicId, _musicLoadCancellationTokenSource?.Token ?? CancellationToken.None);
    }

    public Task SelectContributorDetailsAsync()
    {
        IsAlbumDetailsSelected = false;
        IsContributorDetailsSelected = true;
        IsCommentsDetailsSelected = false;
        return GetContributorMusicsAsync(_musicPublicId, _musicLoadCancellationTokenSource?.Token ?? CancellationToken.None);
    }

    public Task SelectCommentsDetailsAsync()
    {
        IsAlbumDetailsSelected = false;
        IsContributorDetailsSelected = false;
        IsCommentsDetailsSelected = true;
        return GetMusicDetailsCommentsAsync(_musicPublicId, _musicLoadCancellationTokenSource?.Token ?? CancellationToken.None);
    }

    private async Task GetAlbumMusicsAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        if (musicPublicId == Guid.Empty)
            return;

        var response = await MizonApi.SendRequestAsync(new MusicAlbumMusicsOperation(new()
        {
            MusicPublicId = musicPublicId
        }), cancellationToken);

        if (!response.IsSuccess || !IsCurrentMusicLoad(musicPublicId, cancellationToken))
            return;

        await SetMusicDetailsMusicsAsync(AlbumMusics, response.Content!.Musics, cancellationToken);
    }

    private async Task GetContributorMusicsAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        if (musicPublicId == Guid.Empty)
            return;

        var response = await MizonApi.SendRequestAsync(new MusicContributorMusicsOperation(new()
        {
            MusicPublicId = musicPublicId
        }), cancellationToken);

        if (!response.IsSuccess || !IsCurrentMusicLoad(musicPublicId, cancellationToken))
            return;

        await SetMusicDetailsMusicsAsync(ContributorMusics, response.Content!.Musics, cancellationToken);
    }

    private Task SetMusicDetailsMusicsAsync(ObservableCollection<MusicDetailsMusicItemViewModel> target, List<Mizon.API.Client.Responses.Musics.MusicTabMusicItemResponse> musics, CancellationToken cancellationToken)
    {
        target.Clear();

        foreach (var music in musics)
        {
            var item = new MusicDetailsMusicItemViewModel
            {
                PublicId = music.PublicId,
                Title = music.Title,
                PersianTitle = music.PersianTitle,
                LikesCount = music.LikesCount,
                CoverMediaPublicId = music.CoverMediaPublicId
            };

            music.Contributors.ForEach(contributor => item.Contributors.Add(new()
            {
                FirstName = contributor.FirstName,
                LastName = contributor.LastName
            }));

            target.Add(item);

            if (item.CoverMediaPublicId is Guid coverMediaPublicId)
                _ = LoadMusicDetailsCoverAsync(item, coverMediaPublicId, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private async Task LoadMusicDetailsCoverAsync(MusicDetailsMusicItemViewModel music, Guid mediaPublicId, CancellationToken cancellationToken)
    {
        var mediaResponse = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
        {
            MediaPublicId = mediaPublicId
        }), cancellationToken);

        if (!mediaResponse.IsSuccess || mediaResponse.Content is null)
            return;

        var imagePath = await _mediaDownloader.DownloadToCacheAsync(
            mediaResponse.Content,
            new Uri(mediaResponse.Content.StoragePath),
            cancellationToken: cancellationToken);

        _dispatcherQueue.TryEnqueue(() => music.CoverImagePath = imagePath);
    }

    private async Task GetMusicDetailsCommentsAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        if (musicPublicId == Guid.Empty)
            return;

        var response = await MizonApi.SendRequestAsync(new MusicCommentsOperation(new()
        {
            MusicPublicId = musicPublicId
        }), cancellationToken);

        if (!response.IsSuccess || !IsCurrentMusicLoad(musicPublicId, cancellationToken))
            return;

        MusicDetailsComments.Clear();
        foreach (var comment in response.Content!.MusicComments)
        {
            var item = new MusicDetailsCommentItemViewModel
            {
                PublicId = comment.PublicId,
                ClientPublicId = comment.ClientPublicId,
                FirstName = comment.FirstName,
                LastName = comment.LastName,
                Username = comment.Username,
                Text = comment.Text,
                Timestamp = comment.Timestamp,
                ProfileMediaPublicId = comment.ProfileMediaPublicId
            };

            MusicDetailsComments.Add(item);
            if (item.ProfileMediaPublicId is Guid profileMediaPublicId)
                _ = LoadMusicDetailsCommentProfileAsync(item, profileMediaPublicId, cancellationToken);
        }
    }

    private async Task LoadMusicDetailsCommentProfileAsync(MusicDetailsCommentItemViewModel comment, Guid mediaPublicId, CancellationToken cancellationToken)
    {
        var mediaResponse = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
        {
            MediaPublicId = mediaPublicId
        }), cancellationToken);

        if (!mediaResponse.IsSuccess || mediaResponse.Content is null)
            return;

        var imagePath = await _mediaDownloader.DownloadToCacheAsync(
            mediaResponse.Content,
            new Uri(mediaResponse.Content.StoragePath),
            cancellationToken: cancellationToken);

        _dispatcherQueue.TryEnqueue(() => comment.ProfileImagePath = imagePath);
    }

    private bool IsCurrentMusicLoad(Guid musicPublicId, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && _musicPublicId == musicPublicId;

    private void ResetMusicPresentation()
    {
        _musicFilePublicId = Guid.Empty;
        _musicMediaPublicId = Guid.Empty;
        _musicDuration = TimeSpan.Zero;
        _musicMediaResponse = null;
        _vocalsMediaPublicId = null;
        _instrumentalMediaPublicId = null;
        _vocalsCachedPath = null;
        _instrumentalCachedPath = null;
        IsStemMixAvailable = false;
        SetField(ref _isStemMixerEnabled, false, nameof(IsStemMixerEnabled));
        IsStemMixerLoading = false;
        StemMixBalance = 0;
        _waveforms.Clear();
        _lastSecond = -1;
        _lastLyricPositionInMilliseconds = -1;
        _lastMusicPosition = TimeSpan.Zero;
        _lastMusicPresencePublishTick = 0;

        Title = string.Empty;
        PersianTitle = null;
        AlbumTitle = null;
        AlbumPublicId = null;
        LableTitle = null;
        LablePublicId = null;
        CoverImagePath = null;
        CoverDownloadProgress = 0;
        CoverDownloadSpeed = 0;
        Duration = string.Empty;
        CurrentPosition = "0:00:00";
        ViewsCount = 0;
        LiveViewsCount = 0;
        LikesCount = 0;
        ServerWaveformIntensity = 0;

        Stories.Clear();
        Genres.Clear();
        AlbumMusics.Clear();
        ContributorMusics.Clear();
        MusicDetailsComments.Clear();
        HasMusicAlbum = false;
        IsAlbumDetailsSelected = false;
        IsContributorDetailsSelected = false;
        IsCommentsDetailsSelected = false;
        Contributors.Clear();
        Reactions.Clear();
        MusicPlaylists.Clear();
        Lyrics.Clear();
        HasLyrics = false;
        CurrentLyricText = string.Empty;
        _activeLyricLine = null;
        _activeLyricWord = null;
        Bars.Clear();
        EqualizerComments.Clear();
        MusicListeners.Clear();
        VocalSegments.Clear();
        _musicListenerAvatarMediaIds.Clear();
        _musicListenerAvatarPaths.Clear();
    }

    public async Task MarkStoryAsViewedAsync(StoryItemViewModel story, CancellationToken cancellationToken = default)
    {
        if (story.ViewedByMe)
            return;

        var response = await MizonApi.SendRequestAsync(new StoryViewOperation(new() { StoryPublicId = story.PublicId }), cancellationToken);
        if (response.IsSuccess)
            _dispatcherQueue.TryEnqueue(() => story.ViewedByMe = true);
    }

    public async Task<Mizon.API.Client.Responses.Stories.StoryResponse?> GetStoryDetailsAsync(StoryItemViewModel story, CancellationToken cancellationToken = default)
    {
        var response = await MizonApi.SendRequestAsync(new StoryDetailsOperation(new() { StoryPublicId = story.PublicId }), cancellationToken);
        return response.IsSuccess ? response.Content : null;
    }

    public async Task<string?> PrepareStoryPlaybackAsync(Guid contentMediaPublicId, CancellationToken cancellationToken = default)
    {
        var response = await MizonApi.SendRequestAsync(new MediaOperation(new() { MediaPublicId = contentMediaPublicId }), cancellationToken);
        if (!response.IsSuccess || response.Content is null)
            return null;

        _ = CacheStoryMediaAsync(response.Content, cancellationToken);
        return response.Content.StoragePath;
    }

    private async Task GetMusicStoriesAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        var response = await MizonApi.SendRequestAsync(new MusicStoriesOperation(new() { MusicPublicId = musicPublicId }), cancellationToken);
        if (!response.IsSuccess || response.Content is null || !IsCurrentMusicLoad(musicPublicId, cancellationToken))
            return;

        var items = response.Content.Stories.Select(story => new StoryItemViewModel
        {
            PublicId = story.PublicId,
            PreviewMediaPublicId = story.PreviewMediaPublicId,
            CreatorFirstName = story.CreatorFirstName,
            CreatorLastName = story.CreatorLastName,
            Title = story.Title,
            ViewedByMe = story.ViewedByMe,
        }).ToList();

        _dispatcherQueue.TryEnqueue(() =>
        {
            Stories.Clear();
            foreach (var item in items)
                Stories.Add(item);
        });

        foreach (var pair in response.Content.Stories.Zip(items))
        {
            _ = LoadStoryImageAsync(pair.Second, pair.First.PreviewMediaPublicId, false, cancellationToken);
            if (pair.First.CreatorProfileMediaPublicId is Guid profileMediaPublicId)
                _ = LoadStoryImageAsync(pair.Second, profileMediaPublicId, true, cancellationToken);
        }
    }

    private async Task CacheStoryMediaAsync(MediaResponse media, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var storageUri))
            return;

        await _mediaDownloader.DownloadToCacheAsync(
            media,
            storageUri,
            cancellationToken: cancellationToken);
    }

    private async Task LoadStoryImageAsync(StoryItemViewModel story, Guid mediaPublicId, bool profile, CancellationToken cancellationToken)
    {
        var response = await MizonApi.SendRequestAsync(new MediaOperation(new() { MediaPublicId = mediaPublicId }), cancellationToken);
        if (!response.IsSuccess || response.Content is null || !Uri.TryCreate(response.Content.StoragePath, UriKind.Absolute, out var storageUri))
            return;

        var imagePath = await _mediaDownloader.DownloadToCacheAsync(
            response.Content,
            storageUri,
            cancellationToken: cancellationToken);

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (profile)
                story.CreatorProfileImagePath = imagePath;
            else
                story.PreviewImagePath = imagePath;
        });
    }

    private void MizonSocket_EntityViewersCountChanged(object? sender, MizonSocket.EntityViewersCountChangedEventArgs e)
    {
        // Only update when the event is for current music
        if (string.Equals(e.EntityType, "Music", StringComparison.OrdinalIgnoreCase) && e.EntityId == _musicPublicId)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                LiveViewsCount = e.Count;
            });
        }
    }

    private async Task CopyLinkAsync(object? parameter, CancellationToken cancellationToken)
    {

        var dataPackage = new DataPackage();


        dataPackage.SetUri(new Uri("https://www.example.com"));

        Clipboard.SetContent(dataPackage);

        _teachingTipService.ShowMessage("کپی شد", "لینک این آهنگ کپی شد.", "باشه!");
    }

    private async Task DesktopShortcutAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_musicPublicId == Guid.Empty)
            return;

        try
        {
            await _shortcutService.CreateMusicDesktopShortcutAsync(_musicPublicId, Title, PersianTitle, CoverImagePath);
            _teachingTipService.ShowMessage("ایجاد شد", "شورتکات این آهنگ در دسکتاپ شما ساخته شد.", "باشه!");
        }
        catch (Exception exception)
        {
            AppLog.Error($"Music desktop shortcut could not be created. MusicPublicId='{_musicPublicId}'.", exception);
            _teachingTipService.ShowMessage("خطا", "ساخت شورتکات روی دسکتاپ انجام نشد.", "باشه!");
        }
    }

    private void MizonSocket_MusicListenersSnapshotReceived(object? sender, MizonSocket.MusicListenersSnapshotEventArgs e)
    {
        if (e.MusicPublicId != _musicPublicId)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            MusicListeners.Clear();

            foreach (var listener in e.Listeners)
                UpsertMusicListener(listener);
        });
    }

    private void MizonSocket_MusicListenerUpdated(object? sender, MizonSocket.MusicListenerUpdatedEventArgs e)
    {
        if (e.MusicPublicId != _musicPublicId)
            return;

        _dispatcherQueue.TryEnqueue(() => UpsertMusicListener(e.Listener));
    }

    private void MizonSocket_MusicListenerRemoved(object? sender, MizonSocket.MusicListenerRemovedEventArgs e)
    {
        if (e.MusicPublicId != _musicPublicId || e.ClientPublicId == MizonSocket.CurrentClientPublicId)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            var marker = MusicListeners.FirstOrDefault(x => x.ClientPublicId == e.ClientPublicId);
            if (marker is not null)
                MusicListeners.Remove(marker);
        });
    }

    private void UpsertMusicListener(Mizon.API.MusicListenerPresence listener)
    {
        // The local client must never render itself, even when it has multiple open windows.
        if (listener.ClientPublicId == MizonSocket.CurrentClientPublicId)
            return;

        var now = DateTimeOffset.UtcNow;
        var marker = MusicListeners.FirstOrDefault(x => x.ClientPublicId == listener.ClientPublicId);
        if (marker is null)
        {
            marker = new MusicListenerMarkerItemViewModel
            {
                ClientPublicId = listener.ClientPublicId,
                FirstName = listener.FirstName,
                LastName = listener.LastName,
                Username = listener.Username,
                ProfileMediaPublicId = listener.ProfileMediaPublicId,
                PositionMilliseconds = listener.PositionMilliseconds,
                PositionReceivedAt = now,
                LastServerUpdateAtUtc = listener.UpdatedAtUtc,
                IsPlaying = listener.IsPlaying,
            };

            MusicListeners.Add(marker);
        }
        else
        {
            // SignalR messages can arrive out of order. Never replace a newer presence state
            // with an older one, otherwise the marker visibly jumps back on the waveform.
            if (listener.UpdatedAtUtc < marker.LastServerUpdateAtUtc)
                return;

            marker.FirstName = listener.FirstName;
            marker.LastName = listener.LastName;
            marker.Username = listener.Username;
            marker.ProfileMediaPublicId = listener.ProfileMediaPublicId;
            var predictedPositionMilliseconds = marker.PositionMilliseconds;
            if (marker.IsPlaying)
                predictedPositionMilliseconds += Math.Max(0, (long)(now - marker.PositionReceivedAt).TotalMilliseconds);

            // Regular updates merely refresh the same playback timeline. Preserve its locally
            // interpolated position for small transport-timing differences; meaningful seeks
            // still apply immediately in either direction.
            var positionDifference = listener.PositionMilliseconds - predictedPositionMilliseconds;
            if (marker.IsPlaying && listener.IsPlaying && Math.Abs(positionDifference) <= 750)
                marker.PositionMilliseconds = Math.Max(predictedPositionMilliseconds, listener.PositionMilliseconds);
            else
                marker.PositionMilliseconds = listener.PositionMilliseconds;

            marker.PositionReceivedAt = now;
            marker.LastServerUpdateAtUtc = listener.UpdatedAtUtc;
            marker.IsPlaying = listener.IsPlaying;
        }

        if (listener.ProfileMediaPublicId is Guid profileMediaPublicId
            && _musicListenerAvatarPaths.TryGetValue(profileMediaPublicId, out var cachedImagePath))
        {
            marker.ImageSource = cachedImagePath;
        }
        else if (listener.ProfileMediaPublicId is Guid profileMediaPublicIdToLoad
            && _musicListenerAvatarMediaIds.Add(profileMediaPublicIdToLoad))
        {
            _ = LoadMusicListenerAvatarAsync(profileMediaPublicIdToLoad, CancellationToken.None);
        }

        UpdateMusicListenerMarkerPosition(marker, now);
    }

    private void MusicListenerPositionTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var marker in MusicListeners)
            UpdateMusicListenerMarkerPosition(marker, now);
    }

    private void UpdateMusicListenerMarkerPosition(MusicListenerMarkerItemViewModel marker, DateTimeOffset now)
    {
        var duration = _audioPlayerService.TrackDuration > TimeSpan.Zero
            ? _audioPlayerService.TrackDuration
            : _musicDuration;

        if (duration <= TimeSpan.Zero)
            return;

        var positionMilliseconds = marker.PositionMilliseconds;
        if (marker.IsPlaying)
            positionMilliseconds += Math.Max(0, (long)(now - marker.PositionReceivedAt).TotalMilliseconds);

        marker.CanvasLeft = CalculateMusicListenerCanvasLeft(positionMilliseconds, duration);
    }

    private static double CalculateMusicListenerCanvasLeft(long positionMilliseconds, TimeSpan duration)
    {
        var ratio = Math.Clamp(positionMilliseconds / duration.TotalMilliseconds, 0d, 1d);
        return Math.Clamp(
            (ratio * WaveformContentWidth) - (MusicListenerMarkerSize / 2d),
            0d,
            WaveformContentWidth - MusicListenerMarkerSize);
    }

    private async Task LoadMusicListenerAvatarAsync(Guid mediaPublicId, CancellationToken cancellationToken)
    {
        var mediaResponse = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
        {
            MediaPublicId = mediaPublicId
        }), cancellationToken);

        if (!mediaResponse.IsSuccess || mediaResponse.Content is null)
            return;

        var avatarImagePath = await _mediaDownloader.DownloadToCacheAsync(
            mediaResponse.Content,
            new Uri(mediaResponse.Content.StoragePath),
            cancellationToken: cancellationToken);

        _dispatcherQueue.TryEnqueue(() =>
        {
            _musicListenerAvatarPaths[mediaPublicId] = avatarImagePath;
            foreach (var listener in MusicListeners.Where(x => x.ProfileMediaPublicId == mediaPublicId))
                listener.ImageSource = avatarImagePath;
        });
    }

    private async Task ReactionAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(parameter?.ToString(), out Guid musicReactionTypePublicId))
        {
            var musicReactionItemViewModel = Reactions.Where(r => r.MusicReactionTypePublicId == musicReactionTypePublicId).FirstOrDefault();

            if (musicReactionItemViewModel is null)
                return;

            musicReactionItemViewModel.ReactedByMe = !musicReactionItemViewModel.ReactedByMe;

            if (musicReactionItemViewModel.ReactedByMe)
                musicReactionItemViewModel.Count++;
            else musicReactionItemViewModel.Count--;

            var baseApiResponse = await MizonApi.SendRequestAsync(new MusicReactionOperation(new()
            {
                MusicPublicId = _musicPublicId,
                MusicReactionTypePublicId = musicReactionTypePublicId,
                Reacted = musicReactionItemViewModel.ReactedByMe,
            }), cancellationToken);
        }
    }

    private async Task ViewAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        await MizonApi.SendRequestAsync(new MusicViewOperation(new()
        {
            MusicPublicId = musicPublicId
        }), cancellationToken);
    }

    private async Task GetMusicContributorsAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        Contributors.Clear();

        var baseApiResponse = await MizonApi.SendRequestAsync(new MusicContributorsOperation(new()
        {
            MusicPublicId = _musicPublicId,
        }), cancellationToken);

        if (baseApiResponse.IsSuccess)
        {
            var response = baseApiResponse.Content;

            foreach (var c in response!.Contributors)
            {
                Contributors.Add(new()
                {
                    PersonPublicId = c.PersonPublicId,
                    FirstName = c.FirstName,
                    LastName = c.LastName,
                    ProfileMediaPublicId = c.ProfileMediaPublicId,
                    MusicContributorTypePublicId = c.MusicContributorTypePublicId,
                    MusicContributorTypeTitle = c.MusicContributorTypeTitle,
                    MusicContributorTypePersianTitle = c.MusicContributorTypePersianTitle,
                });

                if (c.ProfileMediaPublicId is Guid profileMediaPublicId)
                    _ = LoadContributorProfileImageAsync(Contributors[^1], profileMediaPublicId, cancellationToken);
            }

            await UpdateNowPlayingMetadataAsync();
        }
    }

    private async Task LoadContributorProfileImageAsync(MusicContributorItemViewModel contributor, Guid mediaPublicId, CancellationToken cancellationToken)
    {
        var mediaResponse = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
        {
            MediaPublicId = mediaPublicId
        }), cancellationToken);

        if (!mediaResponse.IsSuccess || mediaResponse.Content is null)
            return;

        var profileImagePath = await _mediaDownloader.DownloadToCacheAsync(
            mediaResponse.Content,
            new Uri(mediaResponse.Content.StoragePath),
            cancellationToken: cancellationToken);

        _dispatcherQueue.TryEnqueue(() => contributor.ImageSource = profileImagePath);
    }

    private Task UpdateNowPlayingMetadataAsync()
    {
        var artist = Contributors.Count > 0
            ? string.Join(", ", Contributors.Select(contributor => $"{contributor.FirstName} {contributor.LastName}".Trim()))
            : null;

        return _audioPlayerService.UpdateNowPlayingAsync(new()
        {
            Title = Title ?? string.Empty,
            Artist = artist,
            AlbumTitle = AlbumTitle,
            ThumbnailPath = CoverImagePath,
        });
    }

    private async Task PlayAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_isPlaybackTransitioning)
            return;

        _isPlaybackTransitioning = true;

        try
        {
            switch (_audioPlayerService.AudioPlayerState)
            {
                case AudioPlayerState.Playing:
                    await _audioPlayerService.PauseAsync();
                    break;

                case AudioPlayerState.Paused:
                    await _audioPlayerService.ResumeAsync();
                    break;

                case AudioPlayerState.Idle:
                    if (IsStemMixerEnabled && IsStemMixAvailable)
                    {
                        var stemMixStarted = await PlayStemMixAsync(TimeSpan.Zero, AudioPlayerState.Playing, cancellationToken);
                        if (stemMixStarted)
                            break;
                    }

                    var mediaResponse = await GetMusicMediaAsync(cancellationToken);
                    if (mediaResponse is null)
                        return;

                    var cachedFilePath = _mediaDownloader.GetCachedFilePath(mediaResponse);
                    if (File.Exists(cachedFilePath))
                    {
                        await _audioPlayerService.PlayAsync(cachedFilePath);
                        break;
                    }

                    _musicDownloadTask ??= DownloadMusicAsync(mediaResponse);
                    var storageUri = new Uri(mediaResponse.StoragePath);
                    if (storageUri.IsFile)
                        await _audioPlayerService.PlayAsync(storageUri.LocalPath);
                    else
                        await _audioPlayerService.PlayAsync(storageUri);

                    break;
            }

            UpdateColors();
        }
        finally
        {
            _isPlaybackTransitioning = false;
        }
    }



    public async Task LoadMusicPlaylistsAsync(bool preserveExistingItems = false, CancellationToken cancellationToken = default)
    {
        if (_musicPublicId == Guid.Empty)
            return;

        var response = await MizonApi.SendRequestAsync(new MusicPlaylistsOperation(new()
        {
            MusicPublicId = _musicPublicId
        }), cancellationToken);

        if (!response.IsSuccess)
            return;

        var existingPlaylists = preserveExistingItems
            ? MusicPlaylists.ToDictionary(x => x.PublicId)
            : [];

        if (!preserveExistingItems)
            MusicPlaylists.Clear();

        foreach (var playlistResponse in response.Content!.MusicPlaylists)
        {
            if (!existingPlaylists.TryGetValue(playlistResponse.PublicId, out var playlist))
            {
                playlist = new()
                {
                    PublicId = playlistResponse.PublicId
                };
                MusicPlaylists.Add(playlist);
            }

            var coverChanged = playlist.LastMusicCoverMediaPublicId != playlistResponse.LastMusicCoverMediaPublicId;
            playlist.Title = playlistResponse.Title;
            playlist.MusicsCount = playlistResponse.MusicsCount;
            playlist.ContainsCurrentMusic = playlistResponse.ContainsCurrentMusic;
            playlist.LastMusicCoverMediaPublicId = playlistResponse.LastMusicCoverMediaPublicId;

            if (!coverChanged)
                continue;

            playlist.LastMusicCoverImagePath = null;
            if (playlistResponse.LastMusicCoverMediaPublicId is Guid coverMediaPublicId)
                _ = LoadMusicPlaylistCoverAsync(playlist, coverMediaPublicId, cancellationToken);
        }

        if (!preserveExistingItems)
            return;

        var responsePlaylistPublicIds = response.Content.MusicPlaylists
            .Select(x => x.PublicId)
            .ToHashSet();

        foreach (var playlist in MusicPlaylists.Where(x => !responsePlaylistPublicIds.Contains(x.PublicId)).ToList())
            MusicPlaylists.Remove(playlist);
    }

    public async Task UpdateMusicPlaylistMembershipAsync(MusicPlaylistItemViewModel? changedPlaylist = null, CancellationToken cancellationToken = default)
    {
        if (_musicPublicId == Guid.Empty)
            return;

        var previousMusicsCount = changedPlaylist?.MusicsCount;
        var previousCoverImagePath = changedPlaylist?.LastMusicCoverImagePath;
        if (changedPlaylist is not null)
        {
            changedPlaylist.MusicsCount = changedPlaylist.ContainsCurrentMusic
                ? changedPlaylist.MusicsCount + 1
                : Math.Max(0, changedPlaylist.MusicsCount - 1);

            if (changedPlaylist.ContainsCurrentMusic && !string.IsNullOrWhiteSpace(CoverImagePath))
                changedPlaylist.LastMusicCoverImagePath = CoverImagePath;
        }

        var selectedPlaylistPublicIds = MusicPlaylists
            .Where(playlist => playlist.ContainsCurrentMusic)
            .Select(playlist => playlist.PublicId)
            .ToList();

        await _musicPlaylistMembershipLock.WaitAsync(cancellationToken);
        try
        {
            var response = await MizonApi.SendRequestAsync(new MusicPlaylistMembershipOperation(new()
            {
                MusicPublicId = _musicPublicId,
                MusicPlaylistPublicIds = selectedPlaylistPublicIds
            }), cancellationToken);

            if (response.IsSuccess)
                await LoadMusicPlaylistsAsync(true, cancellationToken);
            else if (changedPlaylist is not null && previousMusicsCount is int musicsCount)
            {
                changedPlaylist.MusicsCount = musicsCount;
                changedPlaylist.LastMusicCoverImagePath = previousCoverImagePath;
            }
        }
        finally
        {
            _musicPlaylistMembershipLock.Release();
        }
    }

    public async Task<bool> CreateMusicPlaylistAsync(string title, CancellationToken cancellationToken = default)
    {
        if (_musicPublicId == Guid.Empty)
            return false;

        var response = await MizonApi.SendRequestAsync(new MusicPlaylistCreateOperation(new()
        {
            MusicPublicId = _musicPublicId,
            Title = title
        }), cancellationToken);

        if (!response.IsSuccess)
            return false;

        await LoadMusicPlaylistsAsync(cancellationToken: cancellationToken);
        return true;
    }

    private async Task LoadMusicPlaylistCoverAsync(
        MusicPlaylistItemViewModel playlist,
        Guid coverMediaPublicId,
        CancellationToken cancellationToken)
    {
        var response = await MizonApi.SendRequestAsync(
            new MediaOperation(new MediaRequest { MediaPublicId = coverMediaPublicId }),
            cancellationToken);

        if (!response.IsSuccess)
            return;

        var media = response.Content!;
        if (!Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var storageUri))
            return;

        try
        {
            var coverImagePath = await _mediaDownloader.DownloadToCacheAsync(
                media,
                storageUri,
                cancellationToken: cancellationToken);

            _dispatcherQueue.TryEnqueue(() => playlist.LastMusicCoverImagePath = coverImagePath);
        }
        catch (Exception exception)
        {
            AppLog.Error($"Music playlist cover could not be downloaded. MediaPublicId='{coverMediaPublicId}'.", exception);
        }
    }

    private async Task LoadAudioRenders()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            AppLog.Information($"Audio render enumeration completed. Count={devices.Count}. Devices=[{string.Join(" | ", devices.Select(device => $"{device.Name} ({device.Id})"))}].");
            _dispatcherQueue.TryEnqueue(() =>
            {
                SynchronizeAudioDevices(devices);
                _ = RefreshAudioDeviceDetailsAsync();
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to enumerate audio render devices: {exception}");
            AppLog.Error("Unable to enumerate audio render devices.", exception);
        }
    }

    private void StartAudioRenderWatching()
    {
        if (_isAudioRenderWatcherRunning)
            return;

        _audioRenderDeviceWatcher = DeviceInformation.CreateWatcher(MediaDevice.GetAudioRenderSelector());
        _audioRenderDeviceWatcher.Added += AudioRenderDeviceWatcherAdded;
        _audioRenderDeviceWatcher.Removed += AudioRenderDeviceWatcherRemoved;
        _audioRenderDeviceWatcher.Updated += AudioRenderDeviceWatcherUpdated;
        _audioRenderDeviceWatcher.Start();
        _audioDeviceDetailsTimer.Start();
        _isAudioRenderWatcherRunning = true;
        AppLog.Information("Audio render device watcher started.");
    }

    private void StopAudioRenderWatching()
    {
        if (_audioRenderDeviceWatcher is not null)
        {
            _audioRenderDeviceWatcher.Added -= AudioRenderDeviceWatcherAdded;
            _audioRenderDeviceWatcher.Removed -= AudioRenderDeviceWatcherRemoved;
            _audioRenderDeviceWatcher.Updated -= AudioRenderDeviceWatcherUpdated;
            _audioRenderDeviceWatcher.Stop();
        }

        _audioRenderDeviceWatcher = null;
        _audioDeviceDetailsTimer.Stop();
        _isAudioRenderWatcherRunning = false;
        AppLog.Information("Audio render device watcher stopped.");
    }

    private void AudioRenderDeviceWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        AppLog.Information($"Audio render device removed. Id='{args.Id}'.");
        _dispatcherQueue.TryEnqueue(() =>
        {
            var audioDevice = AudioDevices.FirstOrDefault(device => device.Id == args.Id);
            if (audioDevice is null)
                return;

            var wasSelected = SelectedAudioDevice?.Id == args.Id;
            AudioDevices.Remove(audioDevice);

            if (wasSelected)
                SelectDefaultAudioDevice();
        });
    }

    private void AudioRenderDeviceWatcherAdded(DeviceWatcher sender, DeviceInformation args)
    {
        AppLog.Information($"Audio render device added. Name='{args.Name}', Id='{args.Id}'.");
        _dispatcherQueue.TryEnqueue(() => UpsertAudioDevice(args));
    }

    private void AudioRenderDeviceWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate args) =>
        _ = RefreshAudioRenderAfterUpdateAsync(args.Id);

    private async Task RefreshAudioRenderAfterUpdateAsync(string deviceId)
    {
        AppLog.Information($"Audio render device updated. Id='{deviceId}'.");
        _batteryCheckedDeviceIds.Remove(deviceId);
        await LoadAudioRenders();
    }

    private void SynchronizeAudioDevices(IEnumerable<DeviceInformation> devices)
    {
        var currentDevices = devices.ToDictionary(device => device.Id, StringComparer.Ordinal);

        for (var index = AudioDevices.Count - 1; index >= 0; index--)
        {
            if (!currentDevices.ContainsKey(AudioDevices[index].Id))
            {
                _batteryCheckedDeviceIds.Remove(AudioDevices[index].Id);
                AudioDevices.RemoveAt(index);
            }
        }

        foreach (var device in currentDevices.Values)
            UpsertAudioDevice(device);

        if (SelectedAudioDevice is null || !AudioDevices.Any(device => device.Id == SelectedAudioDevice.Id))
            SelectDefaultAudioDevice();
    }

    private void UpsertAudioDevice(DeviceInformation device)
    {
        var existing = AudioDevices.FirstOrDefault(audioDevice => audioDevice.Id == device.Id);
        if (existing is not null)
        {
            existing.Title = device.Name;
            existing.DeviceInformation = device;
            return;
        }

        AudioDevices.Add(new AudioDeviceItemViewModel
        {
            Id = device.Id,
            Title = device.Name,
            DeviceInformation = device
        });
    }

    private async void AudioDeviceDetailsTimer_Tick(DispatcherQueueTimer sender, object args) =>
        await RefreshAudioDeviceDetailsAsync();

    private async Task RefreshAudioDeviceDetailsAsync()
    {
        var devices = AudioDevices.ToArray();
        var hasUncheckedBatteryDevices = devices.Any(device => !_batteryCheckedDeviceIds.Contains(device.Id));
        var bluetoothBatteryDevices = hasUncheckedBatteryDevices
            ? await BluetoothBatteryReader.GetPairedDevicesAsync()
            : [];

        var volumeTasks = devices.Select(async device => new
        {
            Device = device,
            VolumePercentage = await Task.Run(() => AudioEndpointVolumeReader.TryGetVolumePercentage(device.Id)),
            BatteryLevel = await TryReadBatteryLevelAsync(device, bluetoothBatteryDevices)
        });

        var results = await Task.WhenAll(volumeTasks);
        foreach (var result in results)
        {
            var volumeChanged = result.Device.VolumePercentage != result.VolumePercentage;
            var batteryChanged = result.BatteryLevel.HasValue && result.Device.BatteryLevel != result.BatteryLevel;
            result.Device.VolumePercentage = result.VolumePercentage;
            if (result.BatteryLevel.HasValue)
                result.Device.BatteryLevel = result.BatteryLevel;

            if (volumeChanged || batteryChanged)
                AppLog.Information($"Audio device details. Name='{result.Device.Title}', Volume={result.VolumePercentage?.ToString() ?? "unavailable"}, Battery={result.BatteryLevel?.ToString() ?? "unavailable"}.");
        }
    }

    private async Task<byte?> TryReadBatteryLevelAsync(
        AudioDeviceItemViewModel device,
        IReadOnlyList<BluetoothBatteryDevice> bluetoothBatteryDevices)
    {
        if (_batteryCheckedDeviceIds.Contains(device.Id))
            return null;

        _batteryCheckedDeviceIds.Add(device.Id);
        var audioDeviceDetails = await BluetoothBatteryReader.GetAudioDeviceDetailsAsync(device.Id);
        var batteryLevel = BluetoothBatteryReader.ResolveBatteryLevel(audioDeviceDetails, device.Title, bluetoothBatteryDevices);
        AppLog.Information($"Battery resolution. AudioDevice='{device.Title}', Value={batteryLevel?.ToString() ?? "unavailable"}.");
        return batteryLevel;
    }

    private void SelectDefaultAudioDevice()
    {
        SelectedAudioDevice = AudioDevices.FirstOrDefault();
    }

    private async Task ChangeOutputDeviceAsync(AudioDeviceItemViewModel device)
    {
        try
        {
            await _audioPlayerService.ChangeOutputDeviceAsync(device.DeviceInformation);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to switch the audio output device: {exception}");
            AppLog.Error($"Unable to switch audio output from the music page. Device='{device.Title}', Id='{device.Id}'.", exception);
        }
    }

    private async Task GetMusicAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        var baseApiResponse = await MizonApi.SendRequestAsync(new MusicOperation(new()
        {
            MusicPublicId = _musicPublicId,
        }), cancellationToken);

        if (baseApiResponse.IsSuccess && IsCurrentMusicLoad(musicPublicId, cancellationToken))
        {
            var musicResponse = baseApiResponse.Content!;

            Title = musicResponse.Title;

            if (!string.IsNullOrWhiteSpace(musicResponse.PersianTitle))
                PersianTitle = $"({musicResponse.PersianTitle})";

            ViewsCount = musicResponse.ViewsCount;
            LikesCount = musicResponse.LikesCount;
            AlbumTitle = musicResponse.MusicAlbum?.Title;
            AlbumPublicId = musicResponse.MusicAlbum?.PublicId;
            HasMusicAlbum = AlbumPublicId is not null;
            LableTitle = musicResponse.MusicLabel?.Title;
            LablePublicId = musicResponse.MusicLabel?.PublicId;
            
            Genres.Clear();
            musicResponse.MusicGenres.ForEach(g =>
            {
                Genres.Add(new()
                {
                    PublicId = g.PublicId,
                    Title = g.Title,
                    PersianTitle = g.PersianTitle
                });
            });

            // After successfully loading music info, join SignalR entity group
            // so this client will receive live viewer count updates for this music.
            _ = MizonSocket.JoinEntityAsync("Music", _musicPublicId, cancellationToken);

            _ = GetMusicCoverFileAsync(_musicPublicId, musicResponse.MusicCover.MediaPublicId, cancellationToken);
            if (HasMusicAlbum)
                _ = SelectAlbumDetailsAsync();
            else
                _ = SelectContributorDetailsAsync();
            await UpdateNowPlayingMetadataAsync();
        }
    }

    private async Task GetMusicFileAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        var baseApiResponse = await MizonApi.SendRequestAsync(new MusicFileOperation(new()
        {
            MusicPublicId = _musicPublicId,
        }), cancellationToken);

        if (baseApiResponse.IsSuccess)
        {
            var musicFileResponse = baseApiResponse.Content!;

            _musicFilePublicId = musicFileResponse.MusicFilePublicId;
            _musicMediaPublicId = musicFileResponse.MediaPublicId;
            _musicDuration = musicFileResponse.Duration;
            Duration = musicFileResponse.Duration.ToString();
            _musicMediaResponse = new()
            {
                MediaPublicId = musicFileResponse.MediaPublicId,
                TotalChunks = musicFileResponse.TotalChunks,
                ChunkSize = musicFileResponse.ChunkSize,
                FileHash = musicFileResponse.FileHash,
                FileName = musicFileResponse.FileName,
                FileSize = musicFileResponse.FileSize,
                StoragePath = musicFileResponse.StoragePath,
                Category = "music",
            };
        }
    }

    private async Task GetMusicStemMixAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        var response = await MizonApi.SendRequestAsync(new MusicStemMixOperation(new()
        {
            MusicPublicId = musicPublicId
        }), cancellationToken);

        if (!response.IsSuccess || response.Content is null || !IsCurrentMusicLoad(musicPublicId, cancellationToken))
            return;

        IsStemMixAvailable = response.Content.IsAvailable &&
            response.Content.VocalsMediaPublicId is not null &&
            response.Content.InstrumentalMediaPublicId is not null;
        _vocalsMediaPublicId = response.Content.VocalsMediaPublicId;
        _instrumentalMediaPublicId = response.Content.InstrumentalMediaPublicId;
    }

    private async Task SwitchStemMixerAsync(bool enable, Guid musicPublicId)
    {
        if (_isStemMixerSwitching || !IsCurrentMusicLoad(musicPublicId, CancellationToken.None))
            return;

        _isStemMixerSwitching = true;
        IsStemMixerLoading = true;
        AppLog.Information($"StemDiag mixer-switch-request. Enabled='{enable}', MusicPublicId='{musicPublicId}', State='{_audioPlayerService.AudioPlayerState}', PositionMs='{_lastMusicPosition.TotalMilliseconds:F0}'.");

        try
        {
            var stateBeforeSwitch = _audioPlayerService.AudioPlayerState;
            var positionBeforeSwitch = _lastMusicPosition;

            if (enable)
            {
                if (!IsStemMixAvailable)
                {
                    IsStemMixerEnabled = false;
                    return;
                }

                var paths = await GetStemCachedPathsAsync(CancellationToken.None);
                if (paths is null)
                {
                    IsStemMixerEnabled = false;
                    return;
                }

                if (stateBeforeSwitch != AudioPlayerState.Idle)
                    await PlayStemMixFromPathsAsync(paths.Value, positionBeforeSwitch, stateBeforeSwitch);
            }
            else if (_audioPlayerService.IsStemMixing && stateBeforeSwitch != AudioPlayerState.Idle)
            {
                var media = await GetMusicMediaAsync(CancellationToken.None);
                if (media is null)
                    return;

                await PlayMainMusicAsync(media, positionBeforeSwitch, stateBeforeSwitch, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error($"Stem mixer switch failed. MusicPublicId='{musicPublicId}', Enabled='{enable}'.", exception);
            if (enable && IsCurrentMusicLoad(musicPublicId, CancellationToken.None))
                IsStemMixerEnabled = false;
        }
        finally
        {
            AppLog.Information($"StemDiag mixer-switch-finished. Enabled='{IsStemMixerEnabled}', IsMixing='{_audioPlayerService.IsStemMixing}', State='{_audioPlayerService.AudioPlayerState}', PositionMs='{_lastMusicPosition.TotalMilliseconds:F0}'.");
            IsStemMixerLoading = false;
            _isStemMixerSwitching = false;
        }
    }

    private async Task<bool> PlayStemMixAsync(TimeSpan position, AudioPlayerState desiredState, CancellationToken cancellationToken)
    {
        var paths = await GetStemCachedPathsAsync(cancellationToken);
        if (paths is null)
            return false;

        await PlayStemMixFromPathsAsync(paths.Value, position, desiredState);
        return true;
    }

    private async Task PlayStemMixFromPathsAsync(
        (string VocalsPath, string InstrumentalPath) paths,
        TimeSpan position,
        AudioPlayerState desiredState)
    {
        await _audioPlayerService.PlayStemMixAsync(paths.VocalsPath, paths.InstrumentalPath, position);

        if (desiredState == AudioPlayerState.Paused)
            await _audioPlayerService.PauseAsync();
    }

    private async Task<(string VocalsPath, string InstrumentalPath)?> GetStemCachedPathsAsync(CancellationToken cancellationToken)
    {
        if (_vocalsCachedPath is not null && _instrumentalCachedPath is not null &&
            File.Exists(_vocalsCachedPath) && File.Exists(_instrumentalCachedPath))
            return (_vocalsCachedPath, _instrumentalCachedPath);

        if (_vocalsMediaPublicId is null || _instrumentalMediaPublicId is null)
            return null;

        var vocalsTask = MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
        {
            MediaPublicId = _vocalsMediaPublicId.Value
        }), cancellationToken).AsTask();
        var instrumentalTask = MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
        {
            MediaPublicId = _instrumentalMediaPublicId.Value
        }), cancellationToken).AsTask();
        await Task.WhenAll(vocalsTask, instrumentalTask);

        var vocals = await vocalsTask;
        var instrumental = await instrumentalTask;
        if (!vocals.IsSuccess || vocals.Content is null || !instrumental.IsSuccess || instrumental.Content is null)
            return null;

        if (!Uri.TryCreate(vocals.Content.StoragePath, UriKind.Absolute, out var vocalsUri) ||
            !Uri.TryCreate(instrumental.Content.StoragePath, UriKind.Absolute, out var instrumentalUri))
            return null;

        var vocalsPathTask = _mediaDownloader.DownloadToCacheAsync(vocals.Content, vocalsUri, cancellationToken: cancellationToken).AsTask();
        var instrumentalPathTask = _mediaDownloader.DownloadToCacheAsync(instrumental.Content, instrumentalUri, cancellationToken: cancellationToken).AsTask();
        await Task.WhenAll(vocalsPathTask, instrumentalPathTask);

        _vocalsCachedPath = await vocalsPathTask;
        _instrumentalCachedPath = await instrumentalPathTask;
        return (_vocalsCachedPath, _instrumentalCachedPath);
    }

    private async Task PlayMainMusicAsync(
        MediaResponse media,
        TimeSpan position,
        AudioPlayerState desiredState,
        CancellationToken cancellationToken)
    {
        var cachedPath = _mediaDownloader.GetCachedFilePath(media);
        if (File.Exists(cachedPath))
        {
            await _audioPlayerService.PlayAsync(cachedPath, position);
        }
        else
        {
            var storageUri = new Uri(media.StoragePath);
            if (storageUri.IsFile)
                await _audioPlayerService.PlayAsync(storageUri.LocalPath, position);
            else
                await _audioPlayerService.PlayAsync(storageUri, position);
        }

        if (desiredState == AudioPlayerState.Paused)
            await _audioPlayerService.PauseAsync();
    }

    private async Task<MediaResponse?> GetMusicMediaAsync(CancellationToken cancellationToken)
    {
        if (_musicMediaResponse is not null)
            return _musicMediaResponse;

        if (_musicMediaPublicId == Guid.Empty)
        {
            await GetMusicFileAsync(_musicPublicId, cancellationToken);
            if (_musicMediaPublicId == Guid.Empty)
                return null;
        }

        var baseApiResponse = await MizonApi.SendRequestAsync(
            new MediaOperation(new MediaRequest { MediaPublicId = _musicMediaPublicId }),
            cancellationToken);

        if (!baseApiResponse.IsSuccess)
            return null;

        _musicMediaResponse = baseApiResponse.Content!;
        return _musicMediaResponse;
    }

    private async Task DownloadMusicAsync(MediaResponse mediaResponse)
    {
        IsMusicDownloading = true;
        MusicDownloadProgress = 0;

        try
        {
            await foreach (var chunk in _mediaDownloader.DownloadAsync(
                mediaResponse,
                new Uri(mediaResponse.StoragePath)))
            {
                MusicDownloadProgress = chunk.FileSize > 0
                    ? Math.Min(100, (double)chunk.TotalDownloaded / chunk.FileSize * 100)
                    : 0;
            }
        }
        finally
        {
            IsMusicDownloading = false;
            MusicDownloadProgress = 100;
            _musicDownloadTask = null;
        }
    }

    private async Task GetMusicCoverFileAsync(Guid musicPublicId, Guid mediaPublicId, CancellationToken cancellationToken)
    {
        var baseApiResponse = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest { MediaPublicId = mediaPublicId }), cancellationToken);


        if (baseApiResponse.IsSuccess)
        {
            var mediaResponse = baseApiResponse.Content;
            var progress = new Progress<MediaDownloader.MediaDownloadProgress>(downloadProgress =>
            {
                CoverDownloadProgress = downloadProgress.FileSize > 0
                    ? Math.Min(1d, (double)downloadProgress.TotalDownloaded / downloadProgress.FileSize)
                    : 0d;
                CoverDownloadSpeed = downloadProgress.InstantaneousSpeedBytesPerSecond;
            });

            CoverDownloadProgress = 0;
            var coverImagePath = await _mediaDownloader.DownloadToCacheAsync(
                mediaResponse,
                new Uri(mediaResponse.StoragePath),
                progress: progress,
                cancellationToken: cancellationToken);

            CoverDownloadProgress = 1;
            CoverImagePath = coverImagePath;
            await UpdateNowPlayingMetadataAsync();
        }
    }

    private async Task GetMusicFileWaveformAsync(Guid musicPublicId, Guid musicFilePublicId, CancellationToken cancellationToken)
    {
        Bars.Clear();

        var baseResponse = await MizonApi.SendRequestAsync(new MusicFileWaveformOperation(new MusicFileWaveformRequest
        {
            MusicFilePublicId = _musicFilePublicId
        }), cancellationToken);

        if (baseResponse.IsSuccess)
        {
            var waveformResponse = baseResponse.Content!;
            var waveforms = waveformResponse.Waveforms.OrderBy(x => x.OffsetInMilliseconds).ToList();
            _waveforms = waveforms;
            _rgbMusicLightingService.Configure(waveformResponse.LightingProfile, waveforms);

            if (waveforms.Count == 0)
            {
                GenerateFallbackBars();
                return;
            }



            // Use RMS to compute heights. Find max RMS to normalize.
            float maxRms = waveforms.Max(w => Math.Abs(w.RMS));
            if (maxRms <= 0) maxRms = waveforms.Max(w => Math.Abs(w.Peak));
            if (maxRms <= 0) maxRms = 1f;

            for (int i = 0; i < BarCount; i++)
            {
                // map i to waveform index (resample)
                int idx = (int)((double)i * waveforms.Count / BarCount);
                if (idx >= waveforms.Count) idx = waveforms.Count - 1;

                var w = waveforms[idx];

                // normalize RMS to height range
                float normalized = Math.Clamp(Math.Abs(w.RMS) / maxRms, 0f, 1f);
                int topHeight = MinHeight + (int)(normalized * (MaxHeight - MinHeight));

                Bars.Add(new EqualizerBarItemViewModel
                {
                    TopHeight = topHeight,
                    BottomHeight = (int)(topHeight * 0.5),
                    TopColor = NormalBrush,
                    BottomColor = BottomNormalBrush
                });
            }

            return;
        }


        // fallback
        GenerateFallbackBars();
    }

    private async Task GetMusicCommentsAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        var baseResponse = await MizonApi.SendRequestAsync(new MusicCommentsOperation(new()
        {
            MusicPublicId = _musicPublicId
        }), cancellationToken);

        if (!baseResponse.IsSuccess)
            return;

        // کامنت‌ها پیش از شروع پخش هم لود می‌شوند؛ در آن لحظه TrackDuration هنوز
        // صفر است. مدت پاسخ MusicFile همان مرجع پایدار موقعیت‌گذاری است.
        var duration = _audioPlayerService.TrackDuration > TimeSpan.Zero
            ? _audioPlayerService.TrackDuration
            : _musicDuration;
        var comments = baseResponse.Content!.MusicComments;

        _dispatcherQueue.TryEnqueue(() => EqualizerComments.Clear());

        for (var index = 0; index < comments.Count; index++)
        {
            var comment = comments[index];
            var horizontalPosition = CalculateCommentCanvasLeft(comment.Timestamp, duration, index);
            var marker = new EqualizerCommentItemViewModel
            {
                PublicId = comment.PublicId,
                Timestamp = comment.Timestamp,
                Text = comment.Text,
                CanvasLeft = horizontalPosition,
                Y = BottomBarMarkerCenterY,
                Command = PlayCommand
            };

            _dispatcherQueue.TryEnqueue(() => EqualizerComments.Add(marker));

            if (comment.ProfileMediaPublicId is not null)
                _ = LoadCommentAvatarAsync(marker, comment.ProfileMediaPublicId.Value, cancellationToken);
        }
    }

    private static double CalculateCommentCanvasLeft(TimeSpan? timestamp, TimeSpan duration, int fallbackIndex)
    {
        if (timestamp is null)
            return 16 + (fallbackIndex * 32);

        if (duration <= TimeSpan.Zero)
            return 16;

        var ratio = Math.Clamp(timestamp.Value.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d);
        // Canvas.Left لبهٔ چپ کنترل است؛ نصف عرض را کم می‌کنیم تا مرکز آواتار
        // دقیقاً با ثانیهٔ کامنت هم‌راستا شود.
        return Math.Clamp(
            (ratio * WaveformContentWidth) - (CommentMarkerSize / 2d),
            0d,
            WaveformContentWidth - CommentMarkerSize);
    }

    private async Task LoadCommentAvatarAsync(EqualizerCommentItemViewModel marker, Guid mediaPublicId, CancellationToken cancellationToken)
    {
        var mediaResponse = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
        {
            MediaPublicId = mediaPublicId
        }), cancellationToken);

        if (!mediaResponse.IsSuccess)
            return;

        var imagePath = await _mediaDownloader.DownloadToCacheAsync(
            mediaResponse.Content!,
            new Uri(mediaResponse.Content!.StoragePath),
            cancellationToken: cancellationToken);

        _dispatcherQueue.TryEnqueue(() => marker.ImageSource = imagePath);
    }

    private async Task GetMusicLyricAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        HasLyrics = true;
        //var baseApiResponse = await MizonApi.SendRequestAsync(new MusicLyricOperation(new()
        //{
        //    MusicPublicId = musicPublicId,
        //}), cancellationToken);

        //if (!baseApiResponse.IsSuccess || baseApiResponse.Content is null || !IsCurrentMusicLoad(musicPublicId, cancellationToken))
        //    return;

        //// Persian is preferred when it exists; otherwise a music can still expose its
        //// only available lyric language without any client-side special case.
        //var lyric = baseApiResponse.Content.MusicLyricLanguages
        //    .OrderByDescending(item => string.Equals(item.LanguagePersianTitle, "فارسی", StringComparison.Ordinal) ||
        //                               string.Equals(item.LanguageTitle, "Persian", StringComparison.OrdinalIgnoreCase))
        //    .ThenBy(item => item.LanguageTitle)
        //    .FirstOrDefault();

        //if (lyric is null)
        //    return;

        //var linesResponse = await MizonApi.SendRequestAsync(new MusicLyricLineOperation(new()
        //{
        //    MusicLyricPublicId = lyric.MusicLyricPublicId,
        //}), cancellationToken);

        //if (!linesResponse.IsSuccess || linesResponse.Content is null || !IsCurrentMusicLoad(musicPublicId, cancellationToken))
        //    return;

        //var lyricLines = linesResponse.Content.MusicLyricLines
        //    .OrderBy(line => line.LineOrder)
        //    .Select(line =>
        //    {
        //        var item = new MusicLyricLineItemViewModel
        //        {
        //            StartTimeInMilliseconds = line.StartTimeInMilliseconds,
        //            EndTimeInMilliseconds = line.EndTimeInMilliseconds,
        //            LineOrder = line.LineOrder,
        //            Text = line.Text,
        //        };

        //        foreach (var word in line.MusicLyricLineWords.OrderBy(word => word.WordOrder))
        //        {
        //            item.Words.Add(new()
        //            {
        //                StartTimeInMilliseconds = word.StartTimeInMilliseconds,
        //                EndTimeInMilliseconds = word.EndTimeInMilliseconds,
        //                WordOrder = word.WordOrder,
        //                Text = word.Text,
        //            });
        //        }

        //        return item;
        //    })
        //    .ToList();

        //_dispatcherQueue.TryEnqueue(() =>
        //{
        //    if (!IsCurrentMusicLoad(musicPublicId, cancellationToken))
        //        return;

        //    Lyrics.Clear();
        //    foreach (var line in lyricLines)
        //        Lyrics.Add(line);

        //    HasLyrics = Lyrics.Count > 0;
        //    UpdateLyricPlaybackState((int)Math.Max(0, _lastMusicPosition.TotalMilliseconds));
        //});
    }

    private void UpdateLyricPlaybackState(int positionInMilliseconds)
    {
        if (!HasLyrics)
            return;

        var activeLine = Lyrics.FirstOrDefault(line =>
            positionInMilliseconds >= line.StartTimeInMilliseconds &&
            positionInMilliseconds < Math.Max(line.EndTimeInMilliseconds, line.StartTimeInMilliseconds + 1));

        if (!ReferenceEquals(_activeLyricLine, activeLine))
        {
            if (_activeLyricLine is not null)
                _activeLyricLine.IsActive = false;

            _activeLyricLine = activeLine;
            if (_activeLyricLine is not null)
                _activeLyricLine.IsActive = true;
        }

        var activeWord = activeLine?.Words.FirstOrDefault(word =>
            positionInMilliseconds >= word.StartTimeInMilliseconds &&
            positionInMilliseconds < Math.Max(word.EndTimeInMilliseconds, word.StartTimeInMilliseconds + 1));

        if (!ReferenceEquals(_activeLyricWord, activeWord))
        {
            if (_activeLyricWord is not null)
                _activeLyricWord.IsActive = false;

            _activeLyricWord = activeWord;
            if (_activeLyricWord is not null)
                _activeLyricWord.IsActive = true;
        }

        CurrentLyricText = activeLine?.Text ?? string.Empty;
    }

    private async Task GetMusicReactionsAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        var reactionTypesResponse = await MizonApi.SendRequestAsync(new MusicReactionTypesOperation(new()), cancellationToken);

        if (!reactionTypesResponse.IsSuccess)
            return;

        var reactionsResponse = await MizonApi.SendRequestAsync(new MusicReactionsOperation(new()
        {
            MusicPublicId = musicPublicId,
        }), cancellationToken);

        // The view model instance is retained while navigating between songs.
        // Ignore any response that belongs to an earlier navigation.
        if (!reactionsResponse.IsSuccess || _musicPublicId != musicPublicId)
            return;

        var reactions = reactionTypesResponse.Content!.MusicReactionTypes
            .Select(type => new MusicReactionItemViewModel
            {
                Count = 0,
                Emoji = type.Emoji,
                MusicReactionTypePublicId = type.PublicId,
                ReactedByMe = false,
            })
            .ToList();

        foreach (var item in reactionsResponse.Content!.MusicReactionItems)
        {
            var reaction = reactions
                .FirstOrDefault(reaction => reaction.MusicReactionTypePublicId == item.MusicReactionTypePublicId);

            if (reaction is not null)
            {
                reaction.Count = item.Count;
                reaction.ReactedByMe = item.ReactedByMe;
            }
        }

        if (_musicPublicId != musicPublicId)
            return;

        Reactions.Clear();
        foreach (var reaction in reactions)
            Reactions.Add(reaction);
    }
}
