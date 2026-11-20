using FluentIcons.Common;
using Microsoft.UI.Dispatching;
using Symbol = FluentIcons.Common.Symbol;
using Mizon.API;
using Mizon.API.Client.Operations.Musics;
using Mizon.API.Client.Requests.Musics;
using Mizon.API.Client.Responses.Musics;
using Mizon.Windows.Pages;
using Mizon.Windows.Common;
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
using WaveformResponse = Mizon.API.Client.Responses.Musics.WaveformResponse;

namespace Mizon.Windows.ViewModels;

public enum PlayState
{
    Playing, Paused, Idle,
}

public partial class MusicPageViewModel : MainViewModel
{
    private readonly TeachingTipService _teachingTipService;
    private readonly IShortcutService _shortcutService;
    private bool _isCreatingDesktopShortcut;
    private readonly Mizon.API.Client.Operations.Media.MediaDownloader _mediaDownloader;
    private readonly RgbMusicLightingService _rgbMusicLightingService;

    private DeviceWatcher? _audioRenderDeviceWatcher;
    private readonly DispatcherQueueTimer _audioDeviceDetailsTimer;
    private readonly DispatcherQueueTimer _musicListenerPositionTimer;
    private (AudioDeviceItemViewModel Device, int Percentage)? _pendingEndpointVolume;
    private AudioDeviceItemViewModel? _writingEndpointVolumeDevice;
    private bool _isAudioRenderWatcherRunning;
    private bool _isAudioDeviceDetailsRefreshing;
    private bool _audioDeviceDetailsRefreshPending;
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

    private Guid _musicPublicId { get; set; }
    private CancellationTokenSource? _musicLoadCancellationTokenSource;
    private Guid _musicFilePublicId { get; set; }
    private Guid _musicMediaPublicId { get; set; }
    private TimeSpan _musicDuration;
    private MediaResponse? _musicMediaResponse;
    private bool _isPlaybackTransitioning;
    private bool _isStemMixerSwitching;
    private Guid? _vocalsMediaPublicId;
    private Guid? _instrumentalMediaPublicId;
    private string? _vocalsCachedPath;
    private string? _instrumentalCachedPath;
    private readonly SemaphoreSlim _musicPlaylistMembershipLock = new(1, 1);

    public ObservableCollection<StoryItemViewModel> Stories { get; } = [];
    public ObservableCollection<StoryItemViewModel> HomeStories { get; } = [];
    public bool IsHomeStoriesLoading { get => field; private set => SetField(ref field, value); } = true;
    public string HomeStoriesMessage { get => string.IsNullOrEmpty(field) ? string.Empty : LocalizationService.Current.GetString(field); private set => SetField(ref field, value); } = string.Empty;

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

    public string Title
    {
        get => field;
        set => SetField(ref field, value);
    } = string.Empty;

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

    public string? PrimaryAccentColor
    {
        get => field;
        private set => SetField(ref field, value);
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
    } = string.Empty;

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

    public MusicVideoResponse? MusicVideo
    {
        get => field;
        private set
        {
            if (SetField(ref field, value))
                OnPropertyChanged(nameof(HasMusicVideo));
        }
    }

    public bool HasMusicVideo => MusicVideo is { } video && video.PublicId != Guid.Empty && video.MediaPublicId != Guid.Empty;

    public ImageSource? MusicVideoCover
    {
        get => field;
        private set => SetField(ref field, value);
    }

    public bool IsStemMixAvailable
    {
        get => field;
        private set
        {
            if (!SetField(ref field, value)) return;
            OnPropertyChanged(nameof(CanToggleStemMixer));
            OnPropertyChanged(nameof(ShowLyricsOrStemMixer));
        }
    }

    public bool IsStemMixerLoading
    {
        get => field;
        private set
        {
            if (SetField(ref field, value))
                OnPropertyChanged(nameof(CanToggleStemMixer));
        }
    }

    public bool CanToggleStemMixer => IsStemMixAvailable && !IsStemMixerLoading;
    public bool ShowLyricsOrStemMixer => HasLyrics || IsStemMixAvailable;

    private bool HasCurrentTrackSource => _musicPublicId != Guid.Empty && _playingMusicPublicId == _musicPublicId;
    internal bool IsStemMixerActive => HasCurrentTrackSource && _audioPlayerService.IsStemMixing &&
        _audioPlayerService.AudioPlayerState != AudioPlayerState.Idle;

    private bool _isStemMixerEnabled;
    public bool IsStemMixerEnabled
    {
        get => _isStemMixerEnabled;
        set
        {
            if (value == _isStemMixerEnabled)
                return;

            if (!CanToggleStemMixer || _isPlaybackTransitioning || _isStemMixerSwitching)
            {
                OnPropertyChanged(nameof(IsStemMixerEnabled));
                return;
            }

            SetStemMixerEnabled(value);
            if (HasCurrentTrackSource && _audioPlayerService.AudioPlayerState != AudioPlayerState.Idle)
                _ = SwitchStemMixerAsync(value, _musicPublicId, _musicLoadCancellationTokenSource?.Token ?? CancellationToken.None);
        }
    }

    private void SetStemMixerEnabled(bool value) => SetField(ref _isStemMixerEnabled, value, nameof(IsStemMixerEnabled));

    public double StemMixBalance
    {
        get => field;
        set
        {
            if (double.IsFinite(value) && SetField(ref field, Math.Clamp(value, -100d, 100d)) &&
                IsStemMixerEnabled && IsStemMixerActive)
                _audioPlayerService.StemMixBalance = field / 100d;
        }
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

    private readonly MusicPlaybackActivityService _playbackActivity;

    public MusicPageViewModel(MizonApi mizonApi,
        Lazy<MizonSocket> mizonSocket,
        NavigationService navigationService,
        DispatcherQueue dispatcherQueue,
        IAudioPlayerService audioPlayerService,
        Mizon.API.Client.Operations.Media.MediaDownloader downloader,
        RgbMusicLightingService rgbMusicLightingService,
        IShortcutService shortcutService,
        TeachingTipService teachingTipService,
        MusicPlaybackActivityService playbackActivity)
        : base(mizonApi, mizonSocket, navigationService, audioPlayerService)
    {
        _mediaDownloader = downloader;
        _playbackActivity = playbackActivity;
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
        DesktopShortcutCommand = new RelayCommand(DesktopShortcutAsync, (_, _) => !_isCreatingDesktopShortcut);
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
                _lastPositionSecond = -1;
            }
            else
            {
                ServerWaveformIntensity = 0f;

                if (e.AudioPlayerState == AudioPlayerState.Paused)
                    _rgbMusicLightingService.SetPaused();
                else
                    _rgbMusicLightingService.SetIdle();
            }

            UpdateTrackPlaybackPresentation();
        });

    }

    private int _paintedPlayedBar = -1;
    private int _paintedHoverBar = -1;
    private int _paintedBottomHoverBar = -1;
    private int _lastPositionSecond = -1;
    private int _lastLyricPositionInMilliseconds = -1;
    private MusicLyricLineItemViewModel? _activeLyricLine;
    private MusicLyricWordItemViewModel? _activeLyricWord;

    private void AudioPlayerService_PositionUpdated(object? sender, PositionUpdatedEventArgs e)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            UpdatePlaybackPosition(e.Position);
            return;
        }

        var musicId = _musicPublicId;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_musicPublicId == musicId)
                UpdatePlaybackPosition(_audioPlayerService.Position);
        });
    }

    private void UpdatePlaybackPosition(TimeSpan position)
    {
        if (!HasCurrentTrackSource)
            return;

        _lastMusicPosition = position;
        _rgbMusicLightingService.Update(position, IsPlaying);
        UpdateCoverPulseFromServerWaveform(position);
        PublishMusicPlaybackPresence(position, force: false);

        var positionMilliseconds = (int)Math.Max(0, position.TotalMilliseconds);
        if (Math.Abs(positionMilliseconds - _lastLyricPositionInMilliseconds) >= 50)
        {
            _lastLyricPositionInMilliseconds = positionMilliseconds;
            UpdateLyricPlaybackState(positionMilliseconds);
        }

        var duration = _audioPlayerService.TrackDuration;
        var playedBar = -1;
        if (duration > TimeSpan.Zero)
        {
            var ratio = Math.Clamp((double)position.Ticks / duration.Ticks, 0d, 1d);
            playedBar = MusicWaveformTimeline.GetBarIndex(ratio * WaveformContentWidth, Bars.Count);
        }
        else if (_waveforms is { Count: > 0 } waveform && Bars.Count != 0)
        {
            var frame = FindWaveformIndex(waveform, positionMilliseconds);
            playedBar = (int)((long)frame * Bars.Count / waveform.Count);
        }

        UpdatePlayedPosition(playedBar);
        var second = (int)position.TotalSeconds;
        if (_lastPositionSecond != second)
        {
            _lastPositionSecond = second;
            CurrentPosition = position.ToString(@"h\:mm\:ss");
        }
    }
    private const double WaveformContentWidth = MusicWaveformTimeline.Width;

    private const double MusicListenerMarkerSize = 28d;
    private const double TopBarMarkerCanvasTop = 22d;

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
    public AudioDeviceItemViewModel? SelectedAudioDevice
    {
        get => field;
        private set => SetField(ref field, value);
    }



    internal bool IsAudioDeviceSwitchPending
    {
        get => field;
        private set => SetField(ref field, value);
    }

    public ObservableCollection<AudioDeviceItemViewModel> AudioDevices { get; } = [];
    public ObservableCollection<EqualizerBarItemViewModel> Bars { get; } = [];
    public ObservableCollection<EqualizerCommentItemViewModel> EqualizerComments { get; } = [];
    private readonly Dictionary<TimeSpan, int> _commentStacks = [];
    public ObservableCollection<MusicListenerMarkerItemViewModel> MusicListeners { get; } = [];
    public ObservableCollection<MusicVocalSegmentItemViewModel> VocalSegments { get; } = [];
    public ObservableCollection<MusicGenreItemViewModel> Genres { get; } = [];
    public ObservableCollection<MusicGenreTypeItemViewModel> MusicGenreTypes { get; } = [];
    // The home page's existing preview data, shared by one card template.
    public IReadOnlyList<RecentlyPlayedMusicItemViewModel> RecentlyPlayedMusics { get; } =
    [
        new() { Title = "In The Dark", Artist = "Dua Lipa", DurationText = "3:42", CoverImagePath = "/Assets/image1.jpeg", LikesCount = 12480,
            Contributors = [new() { DisplayName = "Dua Lipa" }] },
        new() { Title = "After Hours", Artist = "The Weeknd", DurationText = "4:01", CoverImagePath = "/Assets/image2.jpeg", LikesCount = 28640,
            Contributors = [new() { DisplayName = "The Weeknd" }] },
        new() { Title = "Midnight City", Artist = "M83", DurationText = "4:04", CoverImagePath = "/Assets/image3.jpeg", LikesCount = 8952,
            Contributors = [new() { DisplayName = "M83" }] },
        new() { Title = "One More Night", Artist = "Maroon 5", DurationText = "3:39", CoverImagePath = "/Assets/image4.jpeg", LikesCount = 18320,
            Contributors = [new() { DisplayName = "Maroon 5" }] }
    ];
    public IReadOnlyList<MusicRecommendationItemViewModel> MusicRecommendations { get; } =
    [
        new() { Title = "ترندهای امروز", Description = "۲۴ آهنگ تازه", IconSymbol = Symbol.Fire, AccentBrush = new(Color.FromArgb(255, 251, 146, 90)) },
        new() { Title = "Focus Session", Description = "برای تمرکز امروز", IconSymbol = Symbol.Headphones, AccentBrush = new(Color.FromArgb(255, 86, 211, 174)) },
        new() { Title = "Mood: Dreamy", Description = "براساس سلیقهٔ تو", IconSymbol = Symbol.Heart, AccentBrush = new(Color.FromArgb(255, 192, 151, 245)) }
    ];
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
        private set
        {
            if (SetField(ref field, value))
                OnPropertyChanged(nameof(ShowLyricsOrStemMixer));
        }
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
    private List<WaveformResponse> _waveforms = [];
    private long _lastCoverPulseUpdateTick;

    private void UpdateCoverPulseFromServerWaveform(TimeSpan position)
    {
        if (!IsPlaying || _waveforms is not { Count: > 0 } waveforms)
            return;

        var tick = Environment.TickCount64;
        if (tick - _lastCoverPulseUpdateTick < 33)
            return;

        _lastCoverPulseUpdateTick = tick;
        var waveform = waveforms[FindWaveformIndex(waveforms, (int)Math.Max(0d, position.TotalMilliseconds))];
        // Keep the original cover-motion amplitude: it was driven by a raw RMS value.
        // Only the source changed; it now comes from the server waveform frame.
        var intensity = Math.Clamp(Math.Abs(waveform.RMS), 0f, 1f);

        _dispatcherQueue.TryEnqueue(() => ServerWaveformIntensity = intensity);
    }

    private static int FindWaveformIndex(List<WaveformResponse> waveforms, int offsetInMilliseconds)
    {
        var low = 0;
        var high = waveforms.Count - 1;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (waveforms[middle].OffsetInMilliseconds <= offsetInMilliseconds)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }



    private void GenerateFallbackBars()
    {
        ClearWaveformBars();
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
        for (var index = 0; index < Bars.Count; index++)
            RefreshBarColor(index);
    }

    private void RefreshBarColor(int index) =>
        Bars[index].TopColor = index <= _paintedHoverBar ? HoverBrush : index > _paintedPlayedBar ? NormalBrush
            : PlayType == PlayState.Playing ? PlayedBrush : PausedBrush;

    private void RefreshChangedBars(int previous, int current)
    {
        for (var index = Math.Min(previous, current) + 1; index <= Math.Max(previous, current); index++)
            RefreshBarColor(index);
    }

    internal void UpdateWaveformHover(double contentX, bool commentLane)
    {
        var index = MusicWaveformTimeline.GetHoverBarIndex(contentX, Bars.Count);
        SetTopBarHover(commentLane ? -1 : index);
        SetBottomBarHover(commentLane ? index : -1);
    }

    internal void ClearWaveformHover()
    {
        SetTopBarHover(-1);
        SetBottomBarHover(-1);
    }

    private void SetTopBarHover(int index)
    {
        var oldIndex = _paintedHoverBar;
        _paintedHoverBar = index;
        RefreshChangedBars(oldIndex, index);
    }

    private void SetBottomBarHover(int index)
    {
        if (index == _paintedBottomHoverBar) return;

        if (_paintedBottomHoverBar >= 0)
            Bars[_paintedBottomHoverBar].BottomColor = BottomNormalBrush;
        if (index >= 0)
            Bars[index].BottomColor = BottomHoverBrush;
        _paintedBottomHoverBar = index;
    }

    internal TimeSpan? GetWaveformCommentTimestamp(double contentX) =>
        MusicWaveformTimeline.GetTimestamp(contentX, _musicDuration);

    internal async Task<bool> CreateMusicCommentAsync(Guid musicPublicId, string text, TimeSpan timestamp, ImageSource? avatar)
    {
        var token = _musicLoadCancellationTokenSource?.Token ?? CancellationToken.None;
        var trimmedText = text.Trim();
        if (!IsCurrentMusicLoad(musicPublicId, token) || musicPublicId == Guid.Empty ||
            string.IsNullOrWhiteSpace(trimmedText) || timestamp < TimeSpan.Zero || timestamp > _musicDuration)
            return false;

        var response = await MizonApi.SendRequestAsync(new MusicCommentCreateOperation(new()
        {
            MusicPublicId = musicPublicId,
            Text = trimmedText,
            Timestamp = timestamp
        }), token);

        if (!response.IsSuccess || response.Content is not { } created || !IsCurrentMusicLoad(musicPublicId, token))
            return false;

        // Place the confirmed comment immediately, even if the list or avatar request is slow.
        if (!EqualizerComments.Any(marker => marker.PublicId == created.MusicCommentPublicId))
        {
            var marker = AddWaveformComment(created.MusicCommentPublicId, timestamp, trimmedText, isOwnComment: true);
            marker.ImageSource = avatar;
            if (avatar is null)
                _ = LoadPostedCommentAvatarAsync(musicPublicId, marker, token);
        }
        return true;
    }

    internal bool SeekRelative(int seconds)
    {
        var duration = _audioPlayerService.TrackDuration;
        if (!HasCurrentTrackSource || duration <= TimeSpan.Zero ||
            _audioPlayerService.AudioPlayerState is not (AudioPlayerState.Playing or AudioPlayerState.Paused))
            return false;

        var ticks = Math.Clamp(_audioPlayerService.Position.Ticks + seconds * TimeSpan.TicksPerSecond, 0, duration.Ticks);
        ApplyMusicSeek(TimeSpan.FromTicks(ticks));
        return true;
    }

    internal void SeekWaveform(double contentX)
    {
        if (!HasCurrentTrackSource) return;

        var index = MusicWaveformTimeline.GetBarIndex(contentX, Bars.Count);
        if (index < 0) return;

        var duration = _audioPlayerService.TrackDuration;
        if (MusicWaveformTimeline.GetTimestamp(contentX, duration) is { } target)
        {
            ApplyMusicSeek(target);
        }
        else if (_waveforms is { Count: > 0 } waveform)
        {
            var frame = (int)((long)index * waveform.Count / Bars.Count);
            ApplyMusicSeek(TimeSpan.FromMilliseconds(waveform[frame].OffsetInMilliseconds));
        }
    }

    private void ApplyMusicSeek(TimeSpan position)
    {
        _audioPlayerService.SeekTo(position);
        UpdatePlaybackPosition(_audioPlayerService.Position);
    }

    private void UpdatePlayedPosition(int index)
    {
        var oldIndex = _paintedPlayedBar;
        _paintedPlayedBar = index;
        RefreshChangedBars(oldIndex, index);
    }

    private void ClearWaveformBars()
    {
        _paintedPlayedBar = _paintedHoverBar = _paintedBottomHoverBar = -1;
        Bars.Clear();
    }

    private void NavigationService_NavigationChanged(object? sender, NavigationChangedEventArgs e)
    {
        if (e.IsEnteringPage)
        {
            if (e.ToPageType == typeof(MusicPage))
            {
                // The home page owns and cancels widget requests in its Loaded/Unloaded lifecycle.
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
                        if (musicPublicId == _musicPublicId && _musicLoadCancellationTokenSource?.IsCancellationRequested == false)
                            return;

                        BeginMusicLoad(musicPublicId);
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
        else if (e.FromPageType == typeof(MusicsPage))
        {
            // Library/list navigation must not cancel a background queue's song load.
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
            IsTrackWaveformLoading = false;

            if (HasCurrentTrackSource)
                UpdatePlaybackPosition(_audioPlayerService.Position);

            if (NavigationService.CurrentInternalPage is MusicsPage)
            {
                StartAudioRenderWatching();
                _ = LoadAudioRenders();
            }
            LoadHardCodedVocalSegments();
            await GetMusicCommentsAsync(musicPublicId, cancellationToken);
            if (IsCurrentMusicLoad(musicPublicId, cancellationToken) && NavigationService.CurrentInternalPage is MusicsPage)
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
        finally
        {
            FinishTrackTimelineLoad(musicPublicId, cancellationToken);
        }
    }

    public Task SelectAlbumDetailsAsync()
    {
        if (!HasMusicAlbum)
            return Task.CompletedTask;

        _commentDetailsRequestVersion++;
        ClearFocusedMusicComment();
        IsAlbumDetailsSelected = true;
        IsContributorDetailsSelected = false;
        IsCommentsDetailsSelected = false;
        return GetAlbumMusicsAsync(_musicPublicId, _musicLoadCancellationTokenSource?.Token ?? CancellationToken.None);
    }

    public Task SelectContributorDetailsAsync()
    {
        _commentDetailsRequestVersion++;
        ClearFocusedMusicComment();
        IsAlbumDetailsSelected = false;
        IsContributorDetailsSelected = true;
        IsCommentsDetailsSelected = false;
        return GetContributorMusicsAsync(_musicPublicId, _musicLoadCancellationTokenSource?.Token ?? CancellationToken.None);
    }

    public Task SelectCommentsDetailsAsync()
    {
        ClearFocusedMusicComment();
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

    private async Task<bool> GetMusicDetailsCommentsAsync(Guid musicPublicId, CancellationToken cancellationToken, Guid? commentPublicId = null)
    {
        if (musicPublicId == Guid.Empty)
            return false;

        var requestVersion = ++_commentDetailsRequestVersion;
        var response = await MizonApi.SendRequestAsync(new MusicCommentsOperation(new()
        {
            MusicPublicId = musicPublicId,
            MusicCommentPublicId = commentPublicId
        }), cancellationToken);

        if (!response.IsSuccess || response.Content is null || !IsCurrentMusicLoad(musicPublicId, cancellationToken)
            || requestVersion != _commentDetailsRequestVersion || !IsCommentsDetailsSelected)
            return false;

        if (commentPublicId is not null && response.Content!.MusicComments.Count == 0)
        {
            var unavailable = MusicDetailsComments.FirstOrDefault(x => x.PublicId == commentPublicId);
            if (unavailable is not null) MusicDetailsComments.Remove(unavailable);
            return false;
        }

        ClearFocusedMusicComment();
        if (commentPublicId is null) MusicDetailsComments.Clear();
        foreach (var comment in response.Content!.MusicComments)
        {
            var item = CreateCommentItem(comment);
            if (commentPublicId is not null)
            {
                var existing = MusicDetailsComments.FirstOrDefault(x => x.PublicId == comment.PublicId);
                if (existing is not null)
                    MusicDetailsComments[MusicDetailsComments.IndexOf(existing)] = item;
                else
                    MusicDetailsComments.Insert(0, item);
                item.IsActivityTarget = true;
                HasFocusedMusicComment = true;
            }
            else MusicDetailsComments.Add(item);
            if (item.ProfileMediaPublicId is Guid profileMediaPublicId)
                _ = LoadMusicDetailsCommentProfileAsync(item, profileMediaPublicId, cancellationToken);
        }
        return true;
    }

    private async Task LoadMusicDetailsCommentProfileAsync(MusicDetailsCommentItemViewModel comment, Guid mediaPublicId, CancellationToken cancellationToken)
    {
        try
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

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!cancellationToken.IsCancellationRequested && MusicDetailsComments.Contains(comment))
                    comment.ProfileImagePath = imagePath;
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("Music comment profile image could not be loaded.", exception); }
    }

    private bool IsCurrentMusicLoad(Guid musicPublicId, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && _musicPublicId == musicPublicId;

    private void ResetMusicPresentation()
    {
        IsMusicDownloading = false;
        MusicDownloadProgress = 0;
        MusicVideo = null;
        MusicVideoCover = null;
        _musicFilePublicId = Guid.Empty;
        _musicMediaPublicId = Guid.Empty;
        _musicDuration = TimeSpan.Zero;
        _musicMediaResponse = null;
        _vocalsMediaPublicId = null;
        _instrumentalMediaPublicId = null;
        _vocalsCachedPath = null;
        _instrumentalCachedPath = null;
        IsStemMixAvailable = false;
        SetStemMixerEnabled(false);
        IsStemMixerLoading = false;
        StemMixBalance = 0;
        // Playback callbacks may still be reading the previous track's samples.
        _waveforms = [];
        _lastPositionSecond = -1;
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
        PrimaryAccentColor = null;
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
        _commentDetailsRequestVersion++;
        HasFocusedMusicComment = false;
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
        ClearWaveformBars();
        _commentStacks.Clear();
        EqualizerComments.Clear();
        MusicListeners.Clear();
        VocalSegments.Clear();
        _musicListenerAvatarMediaIds.Clear();
        _musicListenerAvatarPaths.Clear();
    }

    public async Task<bool> MarkStoryAsViewedAsync(StoryItemViewModel story, CancellationToken cancellationToken = default)
    {
        if (story.ViewedByMe)
            return true;

        var response = await MizonApi.SendRequestAsync(new StoryViewOperation(new() { StoryPublicId = story.PublicId }), cancellationToken);
        if (response.IsSuccess)
            _dispatcherQueue.TryEnqueue(() =>
            {
                story.ViewedByMe = true;
                foreach (var item in HomeStories.Concat(Stories).Where(item => item.PublicId == story.PublicId))
                    item.ViewedByMe = true;
            });
        return response.IsSuccess;
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

        if (!Uri.TryCreate(response.Content.StoragePath, UriKind.Absolute, out var storageUri))
            return null;

        return await _mediaDownloader.DownloadToCacheAsync(response.Content, storageUri, cancellationToken: cancellationToken);
    }

    public async Task<Mizon.API.Client.Responses.Stories.StoryResponse?> SetStoryLikedAsync(Guid storyPublicId, bool liked, CancellationToken cancellationToken)
    {
        var response = await MizonApi.SendRequestAsync(new StoryLikeOperation(new() { StoryPublicId = storyPublicId, Liked = liked }), cancellationToken);
        return response.IsSuccess ? response.Content : null;
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
            CreatorProfileMediaPublicId = story.CreatorProfileMediaPublicId,
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
            _ = LoadStoryImageSafelyAsync(pair.Second, pair.First.PreviewMediaPublicId, false, cancellationToken);
            if (pair.First.CreatorProfileMediaPublicId is Guid profileMediaPublicId)
                _ = LoadStoryImageSafelyAsync(pair.Second, profileMediaPublicId, true, cancellationToken);
        }
    }

    private async Task LoadStoryImageSafelyAsync(StoryItemViewModel story, Guid mediaPublicId, bool profile,
        CancellationToken cancellationToken, Func<bool>? canApply = null)
    {
        try { await LoadStoryImageAsync(story, mediaPublicId, profile, cancellationToken, canApply); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("Story preview could not be loaded.", exception); }
    }

    private async Task LoadStoryImageAsync(StoryItemViewModel story, Guid mediaPublicId, bool profile,
        CancellationToken cancellationToken, Func<bool>? canApply = null)
    {
        if (canApply is not null && !await UpdateStoryImagesOnDispatcherAsync(static () => { }, cancellationToken, canApply))
            return;
        var response = await MizonApi.SendRequestAsync(new MediaOperation(new() { MediaPublicId = mediaPublicId }), cancellationToken);
        if (!response.IsSuccess || response.Content is null || !Uri.TryCreate(response.Content.StoragePath, UriKind.Absolute, out var storageUri))
            return;

        var imagePath = await _mediaDownloader.DownloadToCacheAsync(
            response.Content,
            storageUri,
            cancellationToken: cancellationToken);

        await UpdateStoryImagesOnDispatcherAsync(() =>
        {
            if (profile)
                story.CreatorProfileImagePath = imagePath;
            else
                story.PreviewImagePath = imagePath;
        }, cancellationToken, canApply);
    }

    private async Task<bool> UpdateStoryImagesOnDispatcherAsync(Action update, CancellationToken cancellationToken,
        Func<bool>? canApply = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcherQueue.HasThreadAccess)
        {
            if (canApply?.Invoke() == false) return false;
            update();
            return true;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        if (!_dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }
                if (canApply?.Invoke() == false)
                {
                    completion.TrySetResult(false);
                    return;
                }
                update();
                completion.TrySetResult(true);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        })) completion.TrySetResult(false);
        return await completion.Task.ConfigureAwait(false);
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

    private Task CopyLinkAsync(object? parameter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AppDeepLinkCatalog.TryCreate("music-details", _musicPublicId, null, null, null, out var route) ||
            route.ToUri() is not { } link) return Task.CompletedTask;

        var data = new DataPackage();
        data.SetText(link);
        Clipboard.SetContent(data);

        var localization = LocalizationService.Current;
        _teachingTipService.ShowMessage(localization.GetString("MusicDetails/CopiedTitle"),
            localization.GetString("MusicDetails/CopiedMessage"), localization.GetString("MusicDetails/Dismiss"));
        return Task.CompletedTask;
    }

    private async Task DesktopShortcutAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_musicPublicId == Guid.Empty || _isCreatingDesktopShortcut)
            return;

        var musicId = _musicPublicId;
        _isCreatingDesktopShortcut = true;
        DesktopShortcutCommand.RaiseCanExecuteChanged();
        try
        {
            await _shortcutService.CreateMusicDesktopShortcutAsync(musicId, Title, PersianTitle, CoverImagePath);
            var language = LocalizationService.Current;
            _teachingTipService.ShowMessage(language.GetString("MusicDetails/ShortcutCreated"),
                language.GetString("MusicDetails/ShortcutCreatedMessage"), language.GetString("MusicDetails/Dismiss"));
        }
        catch (Exception exception)
        {
            AppLog.Error($"Music desktop shortcut could not be created. MusicPublicId='{musicId}'.", exception);
            var language = LocalizationService.Current;
            _teachingTipService.ShowMessage(language.GetString("MusicDetails/ShortcutFailed"),
                language.GetString("MusicDetails/ShortcutFailedMessage"), language.GetString("MusicDetails/Dismiss"));
        }
        finally
        {
            _isCreatingDesktopShortcut = false;
            DesktopShortcutCommand.RaiseCanExecuteChanged();
        }
    }

    private void MizonSocket_MusicListenersSnapshotReceived(object? sender, MizonSocket.MusicListenersSnapshotEventArgs e)
    {
        if (e.MusicPublicId != _musicPublicId)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (e.MusicPublicId != _musicPublicId || _musicLoadCancellationTokenSource?.IsCancellationRequested != false)
                return;
            MusicListeners.Clear();

            foreach (var listener in e.Listeners)
                UpsertMusicListener(listener);
        });
    }

    private void MizonSocket_MusicListenerUpdated(object? sender, MizonSocket.MusicListenerUpdatedEventArgs e)
    {
        if (e.MusicPublicId != _musicPublicId)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (e.MusicPublicId == _musicPublicId && _musicLoadCancellationTokenSource?.IsCancellationRequested == false)
                UpsertMusicListener(e.Listener);
        });
    }

    private void MizonSocket_MusicListenerRemoved(object? sender, MizonSocket.MusicListenerRemovedEventArgs e)
    {
        if (e.MusicPublicId != _musicPublicId || e.ClientPublicId == MizonSocket.CurrentClientPublicId)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (e.MusicPublicId != _musicPublicId || _musicLoadCancellationTokenSource?.IsCancellationRequested != false)
                return;
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
        try
        {
            var response = await MizonApi.SendRequestAsync(new MusicContributorsOperation(new()
            {
                MusicPublicId = musicPublicId,
            }), cancellationToken);
            if (!response.IsSuccess || response.Content is not { } credits ||
                !IsCurrentMusicLoad(musicPublicId, cancellationToken)) return;

            Contributors.Clear();
            foreach (var credit in credits.Contributors)
            {
                Contributors.Add(new()
                {
                    PersonPublicId = credit.PersonPublicId,
                    FirstName = credit.FirstName,
                    LastName = credit.LastName,
                    ProfileMediaPublicId = credit.ProfileMediaPublicId,
                    MusicContributorTypePublicId = credit.MusicContributorTypePublicId,
                    MusicContributorTypeTitle = credit.MusicContributorTypeTitle,
                    MusicContributorTypePersianTitle = credit.MusicContributorTypePersianTitle,
                });
            }

            // One artist can be credited multiple times. Download and decode once per
            // portrait and assign it to all of that artist's roles.
            await Task.WhenAll(Contributors.Where(credit => credit.ProfileMediaPublicId.GetValueOrDefault() != Guid.Empty)
                .GroupBy(credit => credit.ProfileMediaPublicId!.Value)
                .Select(group => LoadContributorProfileImageAsync(group, musicPublicId, cancellationToken))
                .Append(UpdateNowPlayingMetadataAsync()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Music credits could not be loaded.", error); }
    }

    private async Task LoadContributorProfileImageAsync(IGrouping<Guid, MusicContributorItemViewModel> credits,
        Guid musicPublicId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest
            {
                MediaPublicId = credits.Key
            }), cancellationToken);

            if (!response.IsSuccess || response.Content is not { } media ||
                !Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri) ||
                !IsCurrentMusicLoad(musicPublicId, cancellationToken)) return;

            var path = await _mediaDownloader.DownloadToCacheAsync(media, uri, cancellationToken: cancellationToken);
            var portrait = await ImageSourceFactory.LoadNavigationCoverAsync(path, 72, cancellationToken);
            if (!IsCurrentMusicLoad(musicPublicId, cancellationToken)) return;
            foreach (var credit in credits) credit.Portrait = portrait;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("A music contributor portrait could not be loaded.", error); }
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
        if (_isPlaybackTransitioning || _isStemMixerSwitching || _musicPublicId == Guid.Empty)
            return;

        var musicPublicId = _musicPublicId;
        _isPlaybackTransitioning = true;
        try
        {
            if (HasCurrentTrackSource)
            {
                switch (_audioPlayerService.AudioPlayerState)
                {
                    case AudioPlayerState.Playing:
                        await _audioPlayerService.PauseAsync();
                        return;
                    case AudioPlayerState.Paused:
                        await _audioPlayerService.ResumeAsync();
                        return;
                }
            }

            if (!IsStemMixerEnabled || !await TryStartStemMixAsync(musicPublicId, cancellationToken))
            {
                var media = await GetMusicMediaAsync(cancellationToken);
                if (media is null || !IsCurrentMusicLoad(musicPublicId, cancellationToken))
                    return;

                _ = EnsureMusicDownloadAsync(media);
                await _audioPlayerService.PlayAsync(GetMainSourcePath(media), TimeSpan.Zero);
            }

            if (_audioPlayerService.AudioPlayerState != AudioPlayerState.Playing)
                return;

            if (!IsCurrentMusicLoad(musicPublicId, cancellationToken))
            {
                await _audioPlayerService.StopAsync();
                return;
            }

            _playingMusicPublicId = musicPublicId;
            UpdateTrackPlaybackPresentation();
            _playbackActivity.RecordStarted(musicPublicId);
        }
        finally
        {
            _isPlaybackTransitioning = false;
        }
    }

    private void UpdateTrackPlaybackPresentation()
    {
        var state = HasCurrentTrackSource ? _audioPlayerService.AudioPlayerState : AudioPlayerState.Idle;
        IsPlaying = state == AudioPlayerState.Playing;
        PlayType = state switch
        {
            AudioPlayerState.Playing => PlayState.Playing,
            AudioPlayerState.Paused => PlayState.Paused,
            _ => PlayState.Idle
        };
        UpdateColors();
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
            {
                HomeLibraryChanged?.Invoke(this, EventArgs.Empty);
                await LoadMusicPlaylistsAsync(true, cancellationToken);
            }
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

        HomeLibraryChanged?.Invoke(this, EventArgs.Empty);
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
        var watcher = _audioRenderDeviceWatcher;
        if (!IsCurrentAudioRenderWatcher(watcher))
            return;

        try
        {
            var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            AppLog.Information($"Audio render enumeration completed. Count={devices.Count}. Devices=[{string.Join(" | ", devices.Select(device => $"{device.Name} ({device.Id})"))}].");
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!IsCurrentAudioRenderWatcher(watcher))
                    return;

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
        _isAudioRenderWatcherRunning = false;
        _audioDeviceDetailsRefreshPending = false;
        _batteryCheckedDeviceIds.Clear();

        if (_audioRenderDeviceWatcher is not null)
        {
            _audioRenderDeviceWatcher.Added -= AudioRenderDeviceWatcherAdded;
            _audioRenderDeviceWatcher.Removed -= AudioRenderDeviceWatcherRemoved;
            _audioRenderDeviceWatcher.Updated -= AudioRenderDeviceWatcherUpdated;
            _audioRenderDeviceWatcher.Stop();
        }

        _audioRenderDeviceWatcher = null;
        _audioDeviceDetailsTimer.Stop();
        AppLog.Information("Audio render device watcher stopped.");
    }

    private bool IsCurrentAudioRenderWatcher(DeviceWatcher? watcher) =>
        _isAudioRenderWatcherRunning && watcher is not null && ReferenceEquals(watcher, _audioRenderDeviceWatcher);

    private void AudioRenderDeviceWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        AppLog.Information($"Audio render device removed. Id='{args.Id}'.");
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!IsCurrentAudioRenderWatcher(sender))
                return;

            _batteryCheckedDeviceIds.Remove(args.Id);
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
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (IsCurrentAudioRenderWatcher(sender))
                UpsertAudioDevice(args);
        });
    }

    private void AudioRenderDeviceWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate args) =>
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (IsCurrentAudioRenderWatcher(sender))
                _ = RefreshAudioRenderAfterUpdateAsync(args.Id);
        });

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
        if (!_isAudioRenderWatcherRunning)
            return;

        // Timer ticks and watcher bursts share one refresh. A request arriving during
        // an await asks for one fresh pass instead of starting overlapping device I/O.
        _audioDeviceDetailsRefreshPending = true;
        if (_isAudioDeviceDetailsRefreshing)
            return;

        _isAudioDeviceDetailsRefreshing = true;
        try
        {
            while (_audioDeviceDetailsRefreshPending && _isAudioRenderWatcherRunning)
            {
                _audioDeviceDetailsRefreshPending = false;
                var watcher = _audioRenderDeviceWatcher;
                try
                {
                    var devices = AudioDevices.ToArray();
                    var hasUncheckedBatteryDevices = devices.Any(device => !_batteryCheckedDeviceIds.Contains(device.Id));
                    var bluetoothBatteryDevices = hasUncheckedBatteryDevices
                        ? await BluetoothBatteryReader.GetPairedDevicesAsync()
                        : [];

                    if (!IsCurrentAudioRenderWatcher(watcher))
                        continue;

                    var volumeTasks = devices.Select(async device => new
                    {
                        Device = device,
                        VolumeRevision = device.VolumeRevision,
                        Volume = await Task.Run(() => AudioEndpointVolumeReader.TryGetState(device.Id)),
                        BatteryLevel = await TryReadBatteryLevelAsync(device, bluetoothBatteryDevices, watcher)
                    });

                    var results = await Task.WhenAll(volumeTasks);
                    if (!IsCurrentAudioRenderWatcher(watcher))
                        continue;

                    foreach (var result in results)
                    {
                        if (!AudioDevices.Contains(result.Device))
                            continue;

                        var canApplyVolume = result.VolumeRevision == result.Device.VolumeRevision &&
                            !ReferenceEquals(_writingEndpointVolumeDevice, result.Device);
                        var volumeChanged = canApplyVolume && result.Device.OutputVolume != result.Volume;
                        var batteryChanged = result.BatteryLevel.HasValue && result.Device.BatteryLevel != result.BatteryLevel;
                        if (canApplyVolume && result.Volume is not null)
                            result.Device.OutputVolume = result.Volume;
                        if (result.BatteryLevel.HasValue)
                            result.Device.BatteryLevel = result.BatteryLevel;

                        if (volumeChanged || batteryChanged)
                            AppLog.Information($"Audio device details. Name='{result.Device.Title}', Volume={result.Device.OutputVolume?.Percentage.ToString() ?? "unavailable"}, Battery={result.BatteryLevel?.ToString() ?? "unavailable"}.");
                    }
                }
                catch (Exception exception)
                {
                    AppLog.Error("Audio device details refresh failed.", exception);
                }
            }
        }
        finally
        {
            _isAudioDeviceDetailsRefreshing = false;
        }
    }

    private async Task<byte?> TryReadBatteryLevelAsync(
        AudioDeviceItemViewModel device,
        IReadOnlyList<BluetoothBatteryDevice> bluetoothBatteryDevices,
        DeviceWatcher? watcher)
    {
        if (!IsCurrentAudioRenderWatcher(watcher) || !AudioDevices.Contains(device)
            || _batteryCheckedDeviceIds.Contains(device.Id))
            return null;

        _batteryCheckedDeviceIds.Add(device.Id);
        var audioDeviceDetails = await BluetoothBatteryReader.GetAudioDeviceDetailsAsync(device.Id);
        var batteryLevel = BluetoothBatteryReader.ResolveBatteryLevel(audioDeviceDetails, device.Title, bluetoothBatteryDevices);
        AppLog.Information($"Battery resolution. AudioDevice='{device.Title}', Value={batteryLevel?.ToString() ?? "unavailable"}.");
        return batteryLevel;
    }

    private void SelectDefaultAudioDevice()
    {
        var systemDefaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        var device = AudioDevices.FirstOrDefault(item => item.Id == systemDefaultId) ?? AudioDevices.FirstOrDefault();
        if (device is null)
            SelectedAudioDevice = null;
        else
            _ = SelectAudioDeviceAsync(device);
    }

    internal async Task<bool> SelectAudioDeviceAsync(AudioDeviceItemViewModel device)
    {
        if (IsAudioDeviceSwitchPending || !AudioDevices.Contains(device))
            return false;
        if (SelectedAudioDevice?.Id == device.Id)
            return true;

        IsAudioDeviceSwitchPending = true;
        try
        {
            await _audioPlayerService.ChangeOutputDeviceAsync(device.DeviceInformation);
            var stillConnected = AudioDevices.Contains(device);
            SelectedAudioDevice = stillConnected ? device : null;
            return stillConnected;
        }
        catch (Exception exception)
        {
            AppLog.Error("Unable to switch audio output from the music page.", exception);
            return false;
        }
        finally
        {
            IsAudioDeviceSwitchPending = false;
        }
    }

    internal void QueueSelectedOutputVolume(int percentage)
    {
        if (!IsAudioDeviceSwitchPending && SelectedAudioDevice is { OutputVolume: not null } device)
        {
            device.VolumeRevision++;
            _pendingEndpointVolume = (device, Math.Clamp(percentage, 0, 100));
            if (_writingEndpointVolumeDevice is null)
                _ = ApplyOutputVolumeChangesAsync();
        }
    }

    private async Task ApplyOutputVolumeChangesAsync()
    {
        while (_pendingEndpointVolume is { } request)
        {
            _pendingEndpointVolume = null;
            if (!ReferenceEquals(SelectedAudioDevice, request.Device) || !AudioDevices.Contains(request.Device))
                continue;

            _writingEndpointVolumeDevice = request.Device;
            try
            {
                var state = await Task.Run(() =>
                    AudioEndpointVolumeReader.TrySetVolumePercentage(request.Device.Id, request.Percentage));
                request.Device.VolumeRevision++;
                if (_pendingEndpointVolume is null && ReferenceEquals(SelectedAudioDevice, request.Device))
                {
                    if (state is { } value)
                        request.Device.OutputVolume = value;
                    else
                        OutputVolumeChangeFailed?.Invoke(this, EventArgs.Empty);
                }

                // Coalesce fast dragging or a held gamepad direction without losing the final value.
                await Task.Delay(35);
            }
            finally
            {
                _writingEndpointVolumeDevice = null;
            }
        }
    }

    internal event EventHandler? OutputVolumeChangeFailed;

    private async Task<bool> GetMusicAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        var baseApiResponse = await MizonApi.SendRequestAsync(new MusicOperation(new()
        {
            MusicPublicId = musicPublicId,
        }), cancellationToken);

        if (baseApiResponse.IsSuccess && IsCurrentMusicLoad(musicPublicId, cancellationToken))
        {
            var musicResponse = baseApiResponse.Content!;
            MusicVideo = musicResponse.MusicVideo;
            if (HasMusicVideo && MusicVideo?.CoverMediaPublicId is Guid coverId && coverId != Guid.Empty)
                _ = LoadMusicVideoCoverAsync(musicPublicId, coverId, cancellationToken);

            Title = musicResponse.Title;
            PrimaryAccentColor = musicResponse.MusicCover?.PrimaryAccentColor;

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
            if (NavigationService.CurrentInternalPage is MusicsPage)
                _ = MizonSocket.JoinEntityAsync("Music", musicPublicId, cancellationToken);

            if (musicResponse.MusicCover is { } cover)
                _ = GetMusicCoverFileAsync(musicPublicId, cover.MediaPublicId, cancellationToken);
            // An explicit activity/comment destination or a tab selected while metadata
            // was loading takes precedence over the initial default tab.
            if (!IsAlbumDetailsSelected && !IsContributorDetailsSelected && !IsCommentsDetailsSelected)
            {
                if (HasMusicAlbum)
                    _ = SelectAlbumDetailsAsync();
                else
                    _ = SelectContributorDetailsAsync();
            }
            await UpdateNowPlayingMetadataAsync();
            return IsCurrentMusicLoad(musicPublicId, cancellationToken);
        }

        return false;
    }

    private async Task GetMusicFileAsync(Guid musicPublicId, CancellationToken cancellationToken)
    {
        var baseApiResponse = await MizonApi.SendRequestAsync(new MusicFileOperation(new()
        {
            MusicPublicId = musicPublicId,
        }), cancellationToken);

        if (baseApiResponse.IsSuccess && IsCurrentMusicLoad(musicPublicId, cancellationToken))
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

        var stems = response.Content;
        // The two stems and the original must use the same recording and timeline.
        IsStemMixAvailable = stems.IsAvailable && _musicFilePublicId != Guid.Empty &&
            stems.MusicFilePublicId == _musicFilePublicId &&
            stems.VocalsMediaPublicId.GetValueOrDefault() != Guid.Empty &&
            stems.InstrumentalMediaPublicId.GetValueOrDefault() != Guid.Empty &&
            stems.VocalsMediaPublicId != stems.InstrumentalMediaPublicId;

        if (!IsStemMixAvailable)
            return;

        _vocalsMediaPublicId = stems.VocalsMediaPublicId;
        _instrumentalMediaPublicId = stems.InstrumentalMediaPublicId;

        if (IsStemMixerActive)
        {
            StemMixBalance = _audioPlayerService.StemMixBalance * 100d;
            SetStemMixerEnabled(true);
        }
    }

    private async Task SwitchStemMixerAsync(bool enable, Guid musicPublicId, CancellationToken token)
    {
        _isStemMixerSwitching = true;
        IsStemMixerLoading = true;
        try
        {
            if (enable)
            {
                var paths = await GetStemCachedPathsAsync(musicPublicId, token);
                if (paths is not null && IsCurrentMusicLoad(musicPublicId, token) && HasCurrentTrackSource)
                {
                    await _audioPlayerService.SwitchStemMixAsync(paths.Value.VocalsPath, paths.Value.InstrumentalPath,
                        StemMixBalance / 100d, token);
                    if (IsCurrentMusicLoad(musicPublicId, token) && IsStemMixerActive)
                        _audioPlayerService.StemMixBalance = StemMixBalance / 100d;
                }
            }
            else
            {
                var media = await GetMusicMediaAsync(token);
                if (media is not null && IsCurrentMusicLoad(musicPublicId, token) && IsStemMixerActive)
                    await _audioPlayerService.SwitchToMainAsync(GetMainSourcePath(media), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("Unable to switch the active track's stem mix.", exception);
        }
        finally
        {
            if (IsCurrentMusicLoad(musicPublicId, token))
                SetStemMixerEnabled(IsStemMixerActive);
            _isStemMixerSwitching = false;
            IsStemMixerLoading = false;
        }
    }

    private async Task<bool> TryStartStemMixAsync(Guid musicPublicId, CancellationToken token)
    {
        IsStemMixerLoading = true;
        try
        {
            var paths = await GetStemCachedPathsAsync(musicPublicId, token);
            token.ThrowIfCancellationRequested();
            if (paths is not null && IsCurrentMusicLoad(musicPublicId, token))
            {
                await _audioPlayerService.PlayStemMixAsync(paths.Value.VocalsPath, paths.Value.InstrumentalPath,
                    StemMixBalance / 100d, token);
                return true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Error("Unable to start the track's stem mix.", exception);
        }

        finally
        {
            IsStemMixerLoading = false;
        }

        if (IsCurrentMusicLoad(musicPublicId, token))
            SetStemMixerEnabled(false);
        return false;
    }

    private async Task<(string VocalsPath, string InstrumentalPath)?> GetStemCachedPathsAsync(Guid musicPublicId, CancellationToken token)
    {
        if (_vocalsCachedPath is not null && _instrumentalCachedPath is not null &&
            File.Exists(_vocalsCachedPath) && File.Exists(_instrumentalCachedPath))
            return (_vocalsCachedPath, _instrumentalCachedPath);

        if (_vocalsMediaPublicId is not { } vocalsId || _instrumentalMediaPublicId is not { } instrumentalId)
            return null;

        var vocalsTask = DownloadStemAsync(vocalsId, token);
        var instrumentalTask = DownloadStemAsync(instrumentalId, token);
        await Task.WhenAll(vocalsTask, instrumentalTask);

        if (!IsCurrentMusicLoad(musicPublicId, token) || await vocalsTask is not { } vocalsPath ||
            await instrumentalTask is not { } instrumentalPath ||
            string.Equals(vocalsPath, instrumentalPath, StringComparison.OrdinalIgnoreCase))
            return null;

        _vocalsCachedPath = vocalsPath;
        _instrumentalCachedPath = instrumentalPath;
        return (vocalsPath, instrumentalPath);
    }

    private async Task<string?> DownloadStemAsync(Guid mediaId, CancellationToken token)
    {
        var response = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest { MediaPublicId = mediaId }), token);
        if (!response.IsSuccess || response.Content is not { } media ||
            !Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri))
            return null;

        var path = await _mediaDownloader.DownloadToCacheAsync(media, uri, cancellationToken: token);
        return File.Exists(path) ? path : null;
    }

    private string GetMainSourcePath(MediaResponse media)
    {
        var cachedPath = _mediaDownloader.GetCachedFilePath(media);
        if (File.Exists(cachedPath))
            return cachedPath;

        var uri = new Uri(media.StoragePath);
        return uri.IsFile ? uri.LocalPath : uri.AbsoluteUri;
    }

    private async Task<MediaResponse?> GetMusicMediaAsync(CancellationToken cancellationToken)
    {
        if (_musicMediaResponse is not null)
            return _musicMediaResponse;

        var musicPublicId = _musicPublicId;
        if (_musicMediaPublicId == Guid.Empty)
        {
            await GetMusicFileAsync(musicPublicId, cancellationToken);
            if (!IsCurrentMusicLoad(musicPublicId, cancellationToken) || _musicMediaPublicId == Guid.Empty)
                return null;
            if (_musicMediaResponse is not null)
                return _musicMediaResponse;
        }

        var baseApiResponse = await MizonApi.SendRequestAsync(
            new MediaOperation(new MediaRequest { MediaPublicId = _musicMediaPublicId }),
            cancellationToken);

        if (!baseApiResponse.IsSuccess || !IsCurrentMusicLoad(musicPublicId, cancellationToken))
            return null;

        _musicMediaResponse = baseApiResponse.Content!;
        return _musicMediaResponse;
    }

    private async Task DownloadMusicAsync(MediaResponse mediaResponse, Guid musicPublicId)
    {
        IsMusicDownloading = true;
        MusicDownloadProgress = 0;

        try
        {
            await foreach (var chunk in _mediaDownloader.DownloadAsync(
                mediaResponse,
                new Uri(mediaResponse.StoragePath)))
            {
                if (_musicPublicId == musicPublicId)
                    MusicDownloadProgress = chunk.FileSize > 0
                        ? Math.Min(100, (double)chunk.TotalDownloaded / chunk.FileSize * 100)
                        : 0;
            }
            if (_musicPublicId == musicPublicId) MusicDownloadProgress = 100;
        }
        finally
        {
            if (_musicPublicId == musicPublicId) IsMusicDownloading = false;
        }
    }

    private async Task GetMusicCoverFileAsync(Guid musicPublicId, Guid mediaPublicId, CancellationToken cancellationToken)
    {
        var baseApiResponse = await MizonApi.SendRequestAsync(new MediaOperation(new MediaRequest { MediaPublicId = mediaPublicId }), cancellationToken);


        if (baseApiResponse.IsSuccess && IsCurrentMusicLoad(musicPublicId, cancellationToken))
        {
            var mediaResponse = baseApiResponse.Content;
            var progress = new Progress<MediaDownloader.MediaDownloadProgress>(downloadProgress =>
            {
                if (!IsCurrentMusicLoad(musicPublicId, cancellationToken)) return;
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

            if (!IsCurrentMusicLoad(musicPublicId, cancellationToken)) return;
            CoverDownloadProgress = 1;
            CoverImagePath = coverImagePath;
            await UpdateNowPlayingMetadataAsync();
        }
    }

    private async Task LoadMusicVideoCoverAsync(Guid musicPublicId, Guid coverId, CancellationToken token)
    {
        try
        {
            var cover = await ProfileImageLoader.LoadAsync(MizonApi, _mediaDownloader, coverId, token);
            if (IsCurrentMusicLoad(musicPublicId, token))
                MusicVideoCover = cover.Image;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            AppLog.Error("Unable to load the small music-video cover.", error);
        }
    }

    private async Task GetMusicFileWaveformAsync(Guid musicPublicId, Guid musicFilePublicId, CancellationToken cancellationToken)
    {
        var baseResponse = await MizonApi.SendRequestAsync(new MusicFileWaveformOperation(new MusicFileWaveformRequest
        {
            MusicFilePublicId = musicFilePublicId
        }), cancellationToken);
        if (!IsCurrentMusicLoad(musicPublicId, cancellationToken)) return;

        ClearWaveformBars();
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

    private async Task GetMusicCommentsAsync(Guid musicPublicId, CancellationToken token)
    {
        var response = await MizonApi.SendRequestAsync(new MusicCommentsOperation(new()
        {
            MusicPublicId = musicPublicId
        }), token);

        if (!response.IsSuccess || response.Content is not { } content || !IsCurrentMusicLoad(musicPublicId, token))
            return;

        var loadedIds = EqualizerComments.Select(marker => marker.PublicId).ToHashSet();
        var currentClientId = MizonSocket.CurrentClientPublicId;
        foreach (var comment in content.MusicComments)
        {
            if (!loadedIds.Add(comment.PublicId))
                continue;

            var marker = AddWaveformComment(comment.PublicId, comment.Timestamp, comment.Text,
                MusicWaveformTimeline.IsOwnComment(comment.ClientPublicId, currentClientId));
            if (comment.ProfileMediaPublicId.GetValueOrDefault() != Guid.Empty)
                _ = LoadCommentAvatarAsync(musicPublicId, marker, comment.ProfileMediaPublicId!.Value, token);
        }
    }

    private EqualizerCommentItemViewModel AddWaveformComment(Guid id, TimeSpan? timestamp, string text, bool isOwnComment)
    {
        var stackIndex = 0;
        if (timestamp is { } position)
        {
            _commentStacks.TryGetValue(position, out stackIndex);
            _commentStacks[position] = stackIndex + 1;
        }

        var marker = new EqualizerCommentItemViewModel
        {
            PublicId = id,
            Timestamp = timestamp,
            Text = text,
            CanvasLeft = MusicWaveformTimeline.GetMarkerLeft(timestamp, _musicDuration, EqualizerComments.Count),
            CanvasTop = MusicWaveformTimeline.GetCommentMarkerTop(stackIndex),
            IsOwnComment = isOwnComment,
            Command = PlayCommand
        };

        EqualizerComments.Add(marker);
        return marker;
    }

    private async Task LoadPostedCommentAvatarAsync(Guid musicPublicId, EqualizerCommentItemViewModel marker, CancellationToken token)
    {
        try
        {
            var response = await MizonApi.SendRequestAsync(new MusicCommentsOperation(new()
            {
                MusicPublicId = musicPublicId,
                MusicCommentPublicId = marker.PublicId
            }), token);

            var comment = response.IsSuccess ? response.Content?.MusicComments.FirstOrDefault() : null;
            if (comment?.ProfileMediaPublicId is Guid mediaId && mediaId != Guid.Empty &&
                IsCurrentMusicLoad(musicPublicId, token))
                await LoadCommentAvatarAsync(musicPublicId, marker, mediaId, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("Unable to load the new waveform comment's portrait.", exception);
        }
    }

    private async Task LoadCommentAvatarAsync(Guid musicPublicId, EqualizerCommentItemViewModel marker, Guid mediaId, CancellationToken token)
    {
        try
        {
            var profile = await ProfileImageLoader.LoadAsync(MizonApi, _mediaDownloader, mediaId, token);
            if (IsCurrentMusicLoad(musicPublicId, token))
                marker.ImageSource = profile.Image;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("Unable to load a waveform comment portrait.", exception);
        }
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
