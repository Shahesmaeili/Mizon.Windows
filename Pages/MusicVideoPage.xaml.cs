using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Operations.Musics;
using Mizon.API.Client.Responses.Musics;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;

namespace Mizon.Windows.Pages;

public sealed partial class MusicVideoPage : Page
{
    private readonly MizonApi _api;
    private readonly MediaDownloader _downloader;
    private readonly IAudioPlayerService _audio;
    private readonly NavigationService _navigation;
    private CancellationTokenSource? _loadCancellation;
    private Task<bool> _load = Task.FromResult(false);
    private MediaPlayer? _player;
    private MediaSource? _source;
    private string? _title;
    private string? _persianTitle;
    private string? _messageKey;

    internal Guid MusicPublicId { get; private set; }
    public LocalizationService Localization => LocalizationService.Current;

    public MusicVideoPage()
    {
        var services = ((App)Application.Current).Services;
        _api = services.GetRequiredService<MizonApi>();
        _downloader = services.GetRequiredService<MediaDownloader>();
        _audio = services.GetRequiredService<IAudioPlayerService>();
        _navigation = services.GetRequiredService<NavigationService>();
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private string Text(string key, int revision) => Localization.GetString("MusicDetails/" + key, revision);

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        RetirePlayback();
        Localization.PropertyChanged += OnLanguageChanged;
        MusicPublicId = args.Parameter is Dictionary<string, object> parameters &&
            parameters.TryGetValue(PageConstants.MUSIC_ID, out var value) && value is Guid musicId ? musicId : Guid.Empty;
        _messageKey = null;
        VideoMessage.Visibility = Visibility.Collapsed;
        VideoLoading.IsActive = true;
        VideoLoading.Visibility = Visibility.Visible;
        _title = _persianTitle = null;
        UpdateLanguage();
        var request = new CancellationTokenSource();
        _loadCancellation = request;
        var video = args.Parameter is Dictionary<string, object> payload &&
            payload.TryGetValue(PageConstants.MUSIC_VIDEO, out var suppliedVideo) ? suppliedVideo as MusicVideoNavigationData : null;
        _load = LoadAsync(MusicPublicId, video, request.Token);
    }

    internal Task<bool> WaitForContentAsync(CancellationToken token) => _load.WaitAsync(token);

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        RetirePlayback();
        base.OnNavigatedFrom(args);
    }

    private async Task<bool> LoadAsync(Guid musicId, MusicVideoNavigationData? supplied, CancellationToken token)
    {
        try
        {
            if (musicId == Guid.Empty)
                return ShowError("MusicVideoUnavailable");

            MusicVideoResponse? video = supplied?.Video;
            _title = supplied?.Title;
            _persianTitle = supplied?.PersianTitle;
            if (video is null)
            {
                var track = await _api.SendRequestAsync(new MusicOperation(new() { MusicPublicId = musicId }), token);
                token.ThrowIfCancellationRequested();
                if (!track.IsSuccess || track.Content is not { MusicVideo: { } item } details)
                    return ShowError("MusicVideoUnavailable");

                video = item;
                _title = details.Title;
                _persianTitle = details.PersianTitle;
            }

            UpdateLanguage();
            var result = await _api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = video.MediaPublicId }), token);
            token.ThrowIfCancellationRequested();
            if (!result.IsSuccess || !Uri.TryCreate(result.Content?.StoragePath, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp && !uri.IsFile))
                return ShowError("MusicVideoUnavailable");

            var source = MediaSource.CreateFromUri(uri);
            var player = new MediaPlayer { AutoPlay = false, Source = source };
            _source = source;
            _player = player;
            player.MediaFailed += Player_MediaFailed;
            player.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
            VideoPlayer.SetMediaPlayer(player);
            VideoPlayer.Visibility = Visibility.Visible;
            VideoLoading.IsActive = false;
            VideoLoading.Visibility = Visibility.Collapsed;
            if (video.CoverMediaPublicId is Guid coverId && coverId != Guid.Empty)
                _ = LoadPosterAsync(coverId, token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            AppLog.Error("The music video could not be loaded.", error);
            return !token.IsCancellationRequested && ShowError("MusicVideoUnavailable");
        }
    }

    private async Task LoadPosterAsync(Guid coverId, CancellationToken token)
    {
        try
        {
            var poster = await ProfileImageLoader.LoadAsync(_api, _downloader, coverId, token);
            if (!token.IsCancellationRequested)
                VideoPlayer.PosterSource = poster.Image;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("The music-video cover could not be loaded.", error); }
    }

    private void Player_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(sender, _player))
                ShowError("MusicVideoPlaybackFailed");
        });

    private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession session, object args) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_player is null || !ReferenceEquals(session, _player.PlaybackSession) ||
                session.PlaybackState != MediaPlaybackState.Playing || _audio.AudioPlayerState != AudioPlayerState.Playing)
                return;

            try { await _audio.PauseAsync(); }
            catch (Exception error) { AppLog.Error("Unable to pause the track while playing its music video.", error); }
        });

    private bool ShowError(string key)
    {
        _messageKey = key;
        VideoLoading.IsActive = false;
        VideoLoading.Visibility = Visibility.Collapsed;
        VideoMessage.Visibility = Visibility.Visible;
        UpdateLanguage();
        return false;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.Revision))
            UpdateLanguage();
    }

    private void UpdateLanguage()
    {
        TrackTitle.Text = Localization.IsPersian && !string.IsNullOrWhiteSpace(_persianTitle)
            ? _persianTitle : _title ?? Text("MusicVideo", Localization.Revision);
        if (_messageKey is not null)
            VideoMessage.Text = Text(_messageKey, Localization.Revision);
    }

    private void VideoSurface_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Width > 0)
            VideoSurface.Height = Math.Max(180, args.NewSize.Width * 9 / 16);
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.OriginalKey != VirtualKey.GamepadB || !_navigation.CanInternalGoBack) return;
        args.Handled = true;
        if (!args.KeyStatus.WasKeyDown)
            _navigation.InternalGoBack();
    }

    private void RetirePlayback()
    {
        Localization.PropertyChanged -= OnLanguageChanged;
        var request = _loadCancellation;
        _loadCancellation = null;
        request?.Cancel();
        request?.Dispose();
        var player = _player;
        _player = null;
        if (player is not null)
        {
            player.MediaFailed -= Player_MediaFailed;
            player.PlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
            player.Source = null;
            VideoPlayer.SetMediaPlayer(null);
            player.Dispose();
        }
        _source?.Dispose();
        _source = null;
        VideoPlayer.PosterSource = null;
        VideoPlayer.Visibility = Visibility.Collapsed;
    }
}

internal sealed record MusicVideoNavigationData(MusicVideoResponse Video, string Title, string? PersianTitle);
