using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage
{
    private static readonly string[] EnglishAudienceNames = ["Ava", "Nima", "Sara", "Arman", "Darya"];
    private static readonly string[] PersianAudienceNames = ["آوا", "نیما", "سارا", "آرمان", "دریا"];
    private static readonly string[] SamplePlatforms = ["Windows", "Android", "iOS", "macOS", "Web"];
    private readonly Dictionary<Guid, TrackAudienceItemViewModel> _liveAudienceById = [];
    private readonly HashSet<Guid> _activeAudienceIds = [];
    private readonly List<Guid> _departedAudienceIds = [];
    private ObservableCollection<TrackAudienceItemViewModel> AudienceItems { get; } = [];
    private TrackAudienceItemViewModel MyAudienceHistory { get; } = new();
    private DispatcherQueueTimer? _liveAudienceTimer;
    private string? _openAudience;
    private bool _audienceIsOpen;
    private bool _liveAudienceShowsSamples;

    private void TrackViews_Click(object sender, RoutedEventArgs args) => SetTrackAudience("views");
    private void TrackLive_Click(object sender, RoutedEventArgs args) => SetTrackAudience("live");

    private bool SetTrackAudience(string view)
    {
        if (view == "closed")
        {
            CloseTrackAudience();
            return true;
        }
        if (!IsMusicDetailsActive || TrackMetadataContent.Visibility != Visibility.Visible) return false;
        if (_audienceIsOpen)
        {
            if (_openAudience == view) return true;
            ReleaseAudienceRows();
            _openAudience = view;
            RefreshTrackAudience();
            StartTrackAudienceTimer();
            return true;
        }
        var anchor = view == "live" ? TrackLiveButton : TrackViewsButton;
        if (!anchor.IsLoaded || anchor.XamlRoot is null) return false;
        _openAudience = view;
        RefreshTrackAudience();
        try
        {
            TrackAudienceFlyout.ShowAt(anchor);
            return true;
        }
        catch (Exception error)
        {
            ReleaseTrackAudience();
            AppLog.Error("The track audience popup could not be opened.", error);
            return false;
        }
    }

    private void TrackAudienceFlyout_Opened(object sender, object args)
    {
        _audienceIsOpen = true;
        StartTrackAudienceTimer();
    }

    private void StartTrackAudienceTimer()
    {
        if (_openAudience != "live") return;
        _liveAudienceTimer = DispatcherQueue.CreateTimer();
        _liveAudienceTimer.Interval = TimeSpan.FromSeconds(1);
        _liveAudienceTimer.Tick += TrackAudienceTimer_Tick;
        _liveAudienceTimer.Start();
    }

    private void TrackAudienceTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!IsMusicDetailsActive)
        {
            CloseTrackAudience();
            return;
        }
        if (XamlRoot?.IsHostVisible == true) SyncLiveAudience();
    }

    private void TrackAudienceFlyout_Closed(object sender, object args) => ReleaseTrackAudience();

    private void CloseTrackAudience()
    {
        if (_openAudience is null) return;
        TrackAudienceFlyout.Hide();
        ReleaseTrackAudience();
    }

    private void ReleaseTrackAudience()
    {
        _openAudience = null;
        _audienceIsOpen = false;
        ReleaseAudienceRows();
    }

    private void ReleaseAudienceRows()
    {
        if (_liveAudienceTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= TrackAudienceTimer_Tick;
            _liveAudienceTimer = null;
        }
        _liveAudienceShowsSamples = false;
        _liveAudienceById.Clear();
        _activeAudienceIds.Clear();
        _departedAudienceIds.Clear();
        AudienceItems.Clear();
        MyAudienceHistory.Portrait = null;
    }

    private void RefreshTrackAudience()
    {
        if (_openAudience is null) return;
        var live = _openAudience == "live";
        AudiencePopupTitle.Text = Text(live ? "LiveAudience" : "ViewsAudience");
        AutomationProperties.SetName(AudiencePopup, AudiencePopupTitle.Text);
        MyAudienceHistoryPanel.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        if (live)
        {
            if (_liveAudienceShowsSamples)
            {
                AudienceItems.Clear();
                _liveAudienceShowsSamples = false;
            }
            SyncLiveAudience();
        }
        else
        {
            AudiencePopupNotice.Text = Text("AudienceHistorySample");
            FillSampleAudience(live: false);
            var profile = ((App)App.Current).Services.GetRequiredService<HomePageViewModel>();
            MyAudienceHistory.Name = string.IsNullOrWhiteSpace(profile.DisplayName) ? Text("AudienceYou") : profile.DisplayName;
            MyAudienceHistory.Portrait = profile.ProfileImage;
            var random = new Random(MusicPublicId.GetHashCode());
            var starts = random.Next(5, 28);
            MyAudienceHistory.Details = Localization.Format("MusicDetails/AudienceOwnCounts", starts, random.Next(2, starts + 1));
        }
    }

    private void SyncLiveAudience()
    {
        var listeners = ViewModel.MusicListeners;
        if (listeners.Count == 0)
        {
            if (!_liveAudienceShowsSamples)
            {
                _liveAudienceById.Clear();
                FillSampleAudience(live: true);
                _liveAudienceShowsSamples = true;
            }
            AudiencePopupNotice.Text = Text("LiveAudienceEmptySample");
            return;
        }
        if (_liveAudienceShowsSamples)
        {
            AudienceItems.Clear();
            _liveAudienceShowsSamples = false;
        }
        AudiencePopupNotice.Text = Text("LiveAudiencePlatformSample");

        _activeAudienceIds.Clear();
        foreach (var listener in listeners)
            _activeAudienceIds.Add(listener.ClientPublicId);
        _departedAudienceIds.Clear();
        foreach (var id in _liveAudienceById.Keys)
            if (!_activeAudienceIds.Contains(id))
                _departedAudienceIds.Add(id);
        foreach (var id in _departedAudienceIds)
        {
            AudienceItems.Remove(_liveAudienceById[id]);
            _liveAudienceById.Remove(id);
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var listener in listeners)
        {
            if (!_liveAudienceById.TryGetValue(listener.ClientPublicId, out var row))
            {
                row = new TrackAudienceItemViewModel();
                _liveAudienceById.Add(listener.ClientPublicId, row);
                AudienceItems.Add(row);
            }
            row.Name = string.IsNullOrWhiteSpace(listener.DisplayName) ? Text("AudienceListener") : listener.DisplayName;
            var elapsed = listener.IsPlaying ? Math.Max(0, (now - listener.PositionReceivedAt).TotalMilliseconds) : 0;
            var position = TimeSpan.FromMilliseconds(Math.Clamp(listener.PositionMilliseconds + elapsed, 0,
                TimeSpan.FromDays(1).TotalMilliseconds));
            var platform = SamplePlatforms[(int)(unchecked((uint)listener.ClientPublicId.GetHashCode()) % SamplePlatforms.Length)];
            row.Details = AudienceLiveDetails(platform, position, listener.IsPlaying);
            if (row.PortraitPath == listener.ImageSource) continue;
            row.PortraitPath = listener.ImageSource;
            row.Portrait = listener.ImageSource.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase)
                ? null
                : ImageSourceFactory.Create(listener.ImageSource, decodePixelWidth: 64);
        }
    }

    private string AudienceLiveDetails(string platform, TimeSpan position, bool playing) =>
        Localization.Format("MusicDetails/AudienceLiveDetails", platform,
            position.ToString(position.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss"),
            Text(playing ? "AudiencePlaying" : "AudiencePaused"));

    private void FillSampleAudience(bool live)
    {
        AudienceItems.Clear();
        var random = new Random(MusicPublicId.GetHashCode() ^ (live ? 71 : 173));
        var names = Localization.IsPersian ? PersianAudienceNames : EnglishAudienceNames;
        for (var index = 0; index < (live ? 3 : names.Length); index++)
        {
            var details = live
                ? AudienceLiveDetails(SamplePlatforms[random.Next(SamplePlatforms.Length)],
                    TimeSpan.FromSeconds(random.Next(10, 150)), random.Next(4) != 0)
                : SampleAudienceHistoryDetails(random);
            AudienceItems.Add(new() { Name = names[index], Details = details });
        }
    }

    private string SampleAudienceHistoryDetails(Random random)
    {
        var views = random.Next(2, 18);
        return Localization.Format("MusicDetails/AudienceHistoryDetails", views, random.Next(1, views + 1));
    }
}
