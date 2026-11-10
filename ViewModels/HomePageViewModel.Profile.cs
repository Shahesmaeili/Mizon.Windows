using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Mizon.API.Client.Operations.Clients;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;

namespace Mizon.Windows.ViewModels;

public partial class HomePageViewModel
{
    private Guid? _profileMediaPublicId;
    private Task? _profileRefreshTask;
    private CancellationToken _profileRefreshToken;
    private bool _hasAccount;
    private int _profileSession;
    private long _taskbarProfileSession;

    public ImageSource? ProfileImage { get => field; private set => SetField(ref field, value); }
    public string DisplayName { get => field; private set => SetField(ref field, value); } = "حساب من";
    public string UsernameLabel => string.IsNullOrWhiteSpace(Username) ? "بدون نام کاربری" : "@" + Username;
    public string ProfileButtonLabel => $"منوی حساب {DisplayName}";
    public string EmailVerification { get => field; private set => SetField(ref field, value); } = string.Empty;
    public string ProfileStatus { get => field; private set { SetField(ref field, value); OnPropertyChanged(nameof(ProfileStatusVisibility)); } } = string.Empty;
    public Visibility ProfileStatusVisibility => string.IsNullOrEmpty(ProfileStatus) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsSigningOut
    {
        get => field;
        private set
        {
            SetField(ref field, value);
            OnPropertyChanged(nameof(CanUseProfile));
            OnPropertyChanged(nameof(CanRefreshProfile));
            OnPropertyChanged(nameof(HasProfileActivity));
            OnPropertyChanged(nameof(ProfileBusyVisibility));
            LogoutCommand.RaiseCanExecuteChanged();
        }
    }
    public bool CanUseProfile => !IsSigningOut;
    public bool CanRefreshProfile => !IsProfileBusy && !IsSigningOut;
    public bool IsProfileBusy
    {
        get => field;
        private set { SetField(ref field, value); OnPropertyChanged(nameof(ProfileBusyVisibility)); OnPropertyChanged(nameof(CanRefreshProfile)); OnPropertyChanged(nameof(HasProfileActivity)); }
    }
    public bool HasProfileActivity => IsProfileBusy || IsSigningOut;
    public Visibility ProfileBusyVisibility => HasProfileActivity ? Visibility.Visible : Visibility.Collapsed;

    public void ApplyProfile(MeResponse account, string? profileImagePath, ImageSource? profileImage = null, long? taskbarSession = null, bool updateMood = true)
    {
        _taskbarProfileSession = taskbarSession ?? TaskbarProfileBridge.CurrentSession;
        _hasAccount = true;
        Username = account.Username;
        FirstName = account.FirstName;
        LastName = account.LastName;
        NickName = account.NickName;
        EmailAddress = account.EmailAddress;
        var fullName = $"{FirstName} {LastName}".Trim();
        DisplayName = !string.IsNullOrWhiteSpace(NickName) ? NickName.Trim()
            : !string.IsNullOrWhiteSpace(fullName) ? fullName : Username ?? "حساب من";
        _profileMediaPublicId = account.MeProfile?.MediaPublicId;
        ProfileImagePath = _profileMediaPublicId is null ? null : profileImagePath;
        ProfileImage = _profileMediaPublicId is null ? null : profileImage;
        EmailVerification = account.EmailAddressIsVerified ? "ایمیل تأییدشده" : "ایمیل تأیید نشده";
        OnPropertyChanged(nameof(UsernameLabel));
        OnPropertyChanged(nameof(ProfileButtonLabel));
        TaskbarProfileBridge.PublishProfile(_taskbarProfileSession, DisplayName, ProfileImagePath);
        if (updateMood) ApplyMood(account.CurrentMood);
    }

    public async Task RefreshProfileAsync(CancellationToken cancellationToken = default)
    {
        // A just-closed flyout can still be cancelling. Wait for it to release its
        // streams before a reopened menu starts a new request.
        if (_profileRefreshTask is { IsCompleted: false } pending)
        {
            var pendingToken = _profileRefreshToken;
            await pending.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (pendingToken.IsCancellationRequested) await RefreshProfileAsync(cancellationToken);
            return;
        }
        if (IsSigningOut || cancellationToken.IsCancellationRequested) return;
        await (_profileRefreshTask = RefreshProfileCoreAsync(cancellationToken));
    }

    private async Task RefreshProfileCoreAsync(CancellationToken cancellationToken)
    {
        var session = _profileSession;
        var moodRevision = _moodMutationRevision;
        var taskbarSession = TaskbarProfileBridge.CurrentSession;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shellCancellation?.Token ?? CancellationToken.None);
        linked.CancelAfter(TimeSpan.FromSeconds(10));
        var token = linked.Token;
        _profileRefreshToken = token;
        IsProfileBusy = true;
        ProfileStatus = string.Empty;
        try
        {
            var response = await MizonApi.SendRequestAsync(new MeOperation(new()), token);
            token.ThrowIfCancellationRequested();
            if (session != _profileSession) return;
            if (!response.IsSuccess || response.Content is not { } account)
            {
                ProfileStatus = "اطلاعات حساب به‌روز نشد؛ دوباره امتحان کن.";
                return;
            }

            var sameImage = account.MeProfile?.MediaPublicId == _profileMediaPublicId;
            ApplyProfile(account, sameImage ? ProfileImagePath : null, sameImage ? ProfileImage : null, taskbarSession,
                updateMood: moodRevision == _moodMutationRevision);
            if (_profileMediaPublicId is { } mediaPublicId && ProfileImage is null)
            {
                var profile = await ProfileImageLoader.LoadAsync(MizonApi, _mediaDownloader, mediaPublicId, token);
                token.ThrowIfCancellationRequested();
                if (session != _profileSession) return;
                ProfileImagePath = profile.Path;
                ProfileImage = profile.Image;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (session == _profileSession && !cancellationToken.IsCancellationRequested)
                ProfileStatus = "دریافت پروفایل طول کشید؛ دوباره امتحان کن.";
        }
        catch (Exception exception)
        {
            AppLog.Error("Account profile refresh failed.", exception);
            if (session == _profileSession) ProfileStatus = "دریافت عکس یا اطلاعات حساب ممکن نشد؛ دوباره امتحان کن.";
        }
        finally { if (session == _profileSession) IsProfileBusy = false; }
    }

    public async Task<bool> OpenDownloadsAsync()
    {
        try
        {
            Directory.CreateDirectory(_mediaDownloader.CacheDirectory);
            var folder = await StorageFolder.GetFolderFromPathAsync(_mediaDownloader.CacheDirectory);
            if (await Launcher.LaunchFolderAsync(folder)) return true;
        }
        catch (Exception exception) { AppLog.Error("Could not open the media download cache.", exception); }
        ProfileStatus = "پوشهٔ دانلودها باز نشد؛ دوباره امتحان کن.";
        return false;
    }

    private void ClearProfile()
    {
        ClearMood();
        _hasAccount = false;
        _taskbarProfileSession = 0;
        _profileMediaPublicId = null;
        _profileRefreshTask = null;
        ProfileImage = null;
        ProfileImagePath = null;
        Username = NickName = null;
        FirstName = LastName = EmailAddress = string.Empty;
        DisplayName = "حساب من";
        EmailVerification = ProfileStatus = string.Empty;
        IsProfileBusy = false;
        MainSearch.Clear();
        OnPropertyChanged(nameof(UsernameLabel));
        OnPropertyChanged(nameof(ProfileButtonLabel));
    }
}
