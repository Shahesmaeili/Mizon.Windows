using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Symbol = FluentIcons.Common.Symbol;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage
{
    private readonly Dictionary<AudioDeviceItemViewModel, RadioButton> _audioOutputItems = [];
    private AudioDeviceItemViewModel? _observedAudioOutput;
    private bool _syncingOutputSlider;
    private bool _audioOutputMenuOpen;

    private void AudioRenderMenuFlyout_Opening(object sender, object args)
    {
        if (_audioOutputMenuOpen || !IsMusicDetailsActive) return;

        _audioOutputMenuOpen = true;
        ViewModel.AudioDevices.CollectionChanged += AudioDevices_CollectionChanged;
        ViewModel.OutputVolumeChangeFailed += AudioOutputVolumeChangeFailed;
        SyncAudioOutputMenu();
        SyncSelectedOutputVolume();
    }

    private void AudioRenderMenuFlyout_Closed(object sender, object args) => CloseAudioOutputMenu();

    private void CloseAudioOutputMenu()
    {
        if (!_audioOutputMenuOpen) return;

        _audioOutputMenuOpen = false;
        ViewModel.AudioDevices.CollectionChanged -= AudioDevices_CollectionChanged;
        ViewModel.OutputVolumeChangeFailed -= AudioOutputVolumeChangeFailed;
        foreach (var pair in _audioOutputItems)
        {
            pair.Key.PropertyChanged -= AudioDevice_PropertyChanged;
            pair.Value.Click -= AudioOutputItem_Click;
        }
        _audioOutputItems.Clear();
        AudioOutputItemsPanel.Children.Clear();
    }

    private void AudioDevices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => SyncAudioOutputMenu();

    private void AudioDevice_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(AudioDeviceItemViewModel.Title) or
            nameof(AudioDeviceItemViewModel.OutputVolume) or nameof(AudioDeviceItemViewModel.BatteryLevel)))
            return;

        if (sender is AudioDeviceItemViewModel device && _audioOutputItems.TryGetValue(device, out var item))
            UpdateAudioOutputItem(device, item);
    }

    private void SyncAudioOutputMenu()
    {
        if (!_audioOutputMenuOpen) return;

        var menu = AudioOutputItemsPanel.Children;
        foreach (var device in _audioOutputItems.Keys.Where(device => !ViewModel.AudioDevices.Contains(device)).ToArray())
        {
            device.PropertyChanged -= AudioDevice_PropertyChanged;
            _audioOutputItems.Remove(device, out var item);
            item!.Click -= AudioOutputItem_Click;
            menu.Remove(item);
        }

        NoAudioOutputsText.Visibility = ViewModel.AudioDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var device in ViewModel.AudioDevices)
        {
            if (!_audioOutputItems.TryGetValue(device, out var item))
            {
                item = new RadioButton
                {
                    Tag = device,
                    GroupName = "MizonTrackAudioOutputs",
                    UseSystemFocusVisuals = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    MinHeight = 34
                };
                item.Click += AudioOutputItem_Click;
                device.PropertyChanged += AudioDevice_PropertyChanged;
                _audioOutputItems.Add(device, item);
                menu.Add(item);
            }
            UpdateAudioOutputItem(device, item);
        }
    }

    private void ApplyAudioMenuLanguage(Control item)
    {
        item.FlowDirection = Localization.FlowDirection;
        item.Language = Localization.LanguageTag;
        item.FontFamily = Localization.TextFontFamily;
    }

    private void UpdateAudioOutputItem(AudioDeviceItemViewModel device, RadioButton item)
    {
        var volume = device.OutputVolume is { } state ? OutputStateLabel(state) : null;
        var battery = device.HasBatteryLevel ? Localization.Format("MusicDetails/AudioOutputBattery", device.BatteryLevel!) : null;
        var details = volume is null ? battery : battery is null ? volume : $"{volume}  ·  {battery}";
        var label = details is null ? device.Title : $"{device.Title}  ·  {details}";
        item.Content = label;
        item.IsChecked = ViewModel.SelectedAudioDevice?.Id == device.Id;
        item.IsEnabled = !ViewModel.IsAudioDeviceSwitchPending;
        ApplyAudioMenuLanguage(item);
        AutomationProperties.SetName(item, label);
    }

    private string OutputStateLabel(AudioEndpointVolumeState state) =>
        state.IsMuted ? Text("AudioOutputMuted") : state.Percentage == 0 ? Text("AudioOutputSilent") :
            Localization.Format("MusicDetails/AudioOutputVolume", state.Percentage);

    private void ObserveSelectedAudioOutput()
    {
        UnobserveSelectedAudioOutput();
        _observedAudioOutput = ViewModel.SelectedAudioDevice;
        if (_observedAudioOutput is not null)
            _observedAudioOutput.PropertyChanged += SelectedAudioOutput_PropertyChanged;
        SyncSelectedOutputVolume();
    }

    private void UnobserveSelectedAudioOutput()
    {
        if (_observedAudioOutput is not null)
            _observedAudioOutput.PropertyChanged -= SelectedAudioOutput_PropertyChanged;
        _observedAudioOutput = null;
    }

    private void SelectedAudioOutput_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AudioDeviceItemViewModel.OutputVolume))
            SyncSelectedOutputVolume();
    }

    private void SyncSelectedOutputVolume()
    {
        var state = _observedAudioOutput?.OutputVolume;
        var silent = state is { IsSilent: true };
        var alert = silent ? OutputStateLabel(state!.Value) : string.Empty;
        AudioOutputWarning.Text = alert;
        AudioOutputWarning.Visibility = silent ? Visibility.Visible : Visibility.Collapsed;
        AudioOutputGlyph.Symbol = silent ? Symbol.SpeakerMute : Symbol.Speaker2;
        AutomationProperties.SetName(AudioOutputButton, silent ? $"{Text("AudioOutput")} · {alert}" : Text("AudioOutput"));

        if (!_audioOutputMenuOpen) return;

        _syncingOutputSlider = true;
        try
        {
            AudioOutputSlider.IsEnabled = state.HasValue && !ViewModel.IsAudioDeviceSwitchPending;
            var percentage = state is { IsMuted: false } current ? current.Percentage : 0;
            AudioOutputSlider.Value = percentage;
            AudioOutputVolumeValue.Text = state.HasValue ? Localization.Format("MusicDetails/AudioOutputPercent", percentage) : string.Empty;
            AudioOutputVolumeHint.Text = !state.HasValue ? Text("AudioOutputVolumeUnavailable") : alert;
            AudioOutputVolumeHint.Visibility = string.IsNullOrEmpty(AudioOutputVolumeHint.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _syncingOutputSlider = false; }
    }

    private void AudioOutputSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (!_audioOutputMenuOpen || _syncingOutputSlider || !AudioOutputSlider.IsEnabled) return;

        var percentage = (int)Math.Round(args.NewValue);
        AudioOutputVolumeValue.Text = Localization.Format("MusicDetails/AudioOutputPercent", percentage);
        ViewModel.QueueSelectedOutputVolume(percentage);
    }

    private void AudioOutputVolumeChangeFailed(object? sender, EventArgs args)
    {
        if (!_audioOutputMenuOpen) return;

        SyncSelectedOutputVolume();
        AudioOutputVolumeHint.Text = Text("AudioOutputVolumeFailed");
        AudioOutputVolumeHint.Visibility = Visibility.Visible;
    }

    private async void AudioOutputItem_Click(object sender, RoutedEventArgs args)
    {
        if (!IsMusicDetailsActive || sender is not RadioButton { Tag: AudioDeviceItemViewModel device }) return;

        var musicId = MusicPublicId;
        if (!await ViewModel.SelectAudioDeviceAsync(device) && IsMusicDetailsActive && MusicPublicId == musicId)
            ((App)Application.Current).Services.GetRequiredService<TeachingTipService>()
                .ShowMessage(Text("AudioOutput"), Text("AudioOutputFailed"), Text("Dismiss"));
    }

    private void TrackDetailsLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision)) return;
        SyncAudioOutputMenu();
        SyncSelectedOutputVolume();
        if (CommentNavigationNotice.IsOpen && _commentNoticeKey is { } key)
            CommentNavigationNotice.Message = Text(key);

        foreach (var item in MusicPlaylistsMenuFlyout.Items.OfType<MenuFlyoutItem>())
        {
            ApplyAudioMenuLanguage(item);
            if (item.DataContext is MusicPlaylistItemViewModel playlist)
                playlist.RefreshLanguage();
        }
    }
}
