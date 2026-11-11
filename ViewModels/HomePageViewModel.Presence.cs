using System;
using System.ComponentModel;
using Mizon.Windows.Services;

namespace Mizon.Windows.ViewModels;

public partial class HomePageViewModel
{
    private readonly ProfilePresenceService _profilePresence;
    private bool _presenceAttached;

    public bool IsProfileOnline { get => field; private set => SetField(ref field, value); }
    public string ProfilePresenceLabel => LocalizationService.Current.GetString(
        IsProfileOnline ? "ProfileMenu/PresenceOnline" : "ProfileMenu/PresenceOffline");

    private void StartProfilePresence()
    {
        if (!_presenceAttached)
        {
            _presenceAttached = true;
            _profilePresence.StateChanged += ProfilePresenceChanged;
            LocalizationService.Current.PropertyChanged += ProfilePresenceLanguageChanged;
        }
        _profilePresence.SetSession(true, TaskbarProfileBridge.CurrentSession);
        UpdateProfilePresence();
    }

    internal void StopProfilePresence()
    {
        if (_presenceAttached)
        {
            _presenceAttached = false;
            _profilePresence.StateChanged -= ProfilePresenceChanged;
            LocalizationService.Current.PropertyChanged -= ProfilePresenceLanguageChanged;
        }
        _profilePresence.SetSession(false, TaskbarProfileBridge.CurrentSession);
        IsProfileOnline = false;
        OnPropertyChanged(nameof(ProfilePresenceLabel));
        OnPropertyChanged(nameof(ProfileButtonLabel));
    }

    private void ProfilePresenceChanged(object? sender, EventArgs args)
    {
        if (_dispatcherQueue.HasThreadAccess) UpdateProfilePresence();
        else _dispatcherQueue.TryEnqueue(UpdateProfilePresence);
    }

    private void UpdateProfilePresence()
    {
        if (!_presenceAttached) return;
        IsProfileOnline = _profilePresence.ReadState().IsOnline;
        OnPropertyChanged(nameof(ProfilePresenceLabel));
        OnPropertyChanged(nameof(ProfileButtonLabel));
    }

    private void ProfilePresenceLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision)) return;
        OnPropertyChanged(nameof(ProfilePresenceLabel));
        OnPropertyChanged(nameof(ProfileButtonLabel));
    }
}
