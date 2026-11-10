using Mizon.API.Client.Operations.Clients;
using Mizon.API.Client.Responses.Clients;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class HomePageViewModel
{
    private MoodMediaCache? _moodMediaCache;
    private IReadOnlyList<ClientMoodItemResponse>? _moodCatalog;
    private DateTimeOffset _moodCatalogLoaded;
    private Task<IReadOnlyList<ClientMoodItemResponse>>? _moodCatalogTask;
    private bool _moodExplicitlyCleared;
    private long _moodMutationRevision;
    private CancellationTokenSource? _taskbarMoodRequest;
    private long _taskbarMoodRevision, _taskbarMoodSession;
    private Guid? _taskbarMoodId;
    private Guid _taskbarMoodAnimationId, _taskbarMoodEffectId;
    private string? _taskbarMoodAnimationPath, _taskbarMoodEffectPath;

    public ClientMoodItemResponse? CurrentMood { get => field; private set => SetField(ref field, value); }
    public bool IsMoodSaving { get => field; private set => SetField(ref field, value); }
    public string MoodStatus { get => field; private set => SetField(ref field, value); } = string.Empty;

    public async Task<IReadOnlyList<ClientMoodItemResponse>> GetMoodTypesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_moodCatalog is not null && DateTimeOffset.UtcNow - _moodCatalogLoaded < TimeSpan.FromMinutes(10))
            return _moodCatalog;
        if (_moodCatalogTask is null || _moodCatalogTask.IsCompleted) _moodCatalogTask = FetchMoodTypesAsync();
        return await _moodCatalogTask.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ClientMoodItemResponse>> FetchMoodTypesAsync()
    {
        var session = _profileSession;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shellCancellation?.Token ?? CancellationToken.None);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var response = await MizonApi.SendRequestAsync(new ClientMoodTypesOperation(new()), timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        if (session != _profileSession) throw new OperationCanceledException(timeout.Token);
        if (!response.IsSuccess || response.Content is null) throw new IOException("Mood catalog is unavailable.");
        _moodCatalog = response.Content.Items;
        _moodCatalogLoaded = DateTimeOffset.UtcNow;
        return _moodCatalog;
    }

    public async Task EnsureMoodAsync(CancellationToken cancellationToken)
    {
        if (CurrentMood is not null || _moodExplicitlyCleared) return;
        var session = _profileSession;
        var catalog = await GetMoodTypesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (session != _profileSession) return;
        // Preserve the existing header's default without saving a choice for the user.
        if (CurrentMood is null && !_moodExplicitlyCleared)
        {
            CurrentMood = catalog.FirstOrDefault(item => item.Emoji == "🤢") ?? catalog.FirstOrDefault();
            SynchronizeTaskbarMood();
        }
    }

    public ValueTask<string> GetMoodMediaPathAsync(Guid mediaPublicId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (_moodMediaCache ??= new MoodMediaCache(MizonApi, _mediaDownloader)).GetAsync(mediaPublicId, cancellationToken);
    }

    public async Task<bool> SaveMoodAsync(ClientMoodItemResponse? mood, CancellationToken cancellationToken)
    {
        if (IsMoodSaving || IsSigningOut) return false;
        var session = _profileSession;
        _moodMutationRevision++;
        IsMoodSaving = true;
        MoodStatus = string.Empty;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shellCancellation?.Token ?? CancellationToken.None);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var response = await MizonApi.SendRequestAsync(new ClientMoodOperation(new() { ClientMoodTypePublicId = mood?.PublicId }), timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (session != _profileSession) return false;
            if (!response.IsSuccess || response.Content is null)
            {
                MoodStatus = "حال‌وهوایت ذخیره نشد؛ دوباره امتحان کن.";
                return false;
            }
            _moodExplicitlyCleared = response.Content.CurrentMood is null;
            CurrentMood = response.Content.CurrentMood;
            SynchronizeTaskbarMood();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || session != _profileSession) { return false; }
        catch (Exception exception)
        {
            AppLog.Error("Mood selection failed.", exception);
            if (session == _profileSession) MoodStatus = "ارتباط برقرار نشد؛ دوباره امتحان کن.";
            return false;
        }
        finally { if (session == _profileSession) IsMoodSaving = false; }
    }

    private void ApplyMood(ClientMoodItemResponse? mood)
    {
        if (IsMoodSaving) return;
        if (mood is null && !_moodExplicitlyCleared && _moodCatalog is not null)
            mood = _moodCatalog.FirstOrDefault(item => item.Emoji == "🤢") ?? _moodCatalog.FirstOrDefault();
        if (CurrentMood?.PublicId != mood?.PublicId || mood is not null) CurrentMood = mood;
        SynchronizeTaskbarMood();
    }

    private void SynchronizeTaskbarMood()
    {
        if (!_hasAccount || _taskbarProfileSession == 0 || IsSigningOut) return;
        var mood = CurrentMood;
        var effectId = mood?.EffectMediaPublicIds.FirstOrDefault() ?? Guid.Empty;
        if (effectId == Guid.Empty) effectId = mood?.AnimationMediaPublicId ?? Guid.Empty;
        var sameSelection = mood is not null && _taskbarMoodSession == _taskbarProfileSession && _taskbarMoodId == mood.PublicId &&
            _taskbarMoodAnimationId == mood.AnimationMediaPublicId && _taskbarMoodEffectId == effectId;
        if (sameSelection)
        {
            PublishTaskbarMood();
            if (_taskbarMoodRequest is { IsCancellationRequested: false } ||
                (File.Exists(_taskbarMoodAnimationPath) && File.Exists(_taskbarMoodEffectPath))) return;
        }

        // The companion receives completed local files only. Rapid mood changes
        // cancel the old downloads and cannot publish an old effect over a new mood.
        var revision = ++_taskbarMoodRevision;
        _taskbarMoodRequest?.Cancel();
        _taskbarMoodRequest = null;
        _taskbarMoodSession = _taskbarProfileSession;
        _taskbarMoodId = mood?.PublicId;
        _taskbarMoodAnimationId = mood?.AnimationMediaPublicId ?? Guid.Empty;
        _taskbarMoodEffectId = effectId;
        _taskbarMoodAnimationPath = _taskbarMoodEffectPath = null;
        PublishTaskbarMood();
        if (mood is null) return;

        var request = CancellationTokenSource.CreateLinkedTokenSource(_shellCancellation?.Token ?? CancellationToken.None);
        request.CancelAfter(TimeSpan.FromSeconds(25));
        _taskbarMoodRequest = request;
        _ = PrepareTaskbarMoodAsync(mood.AnimationMediaPublicId, effectId, _profileSession, _taskbarProfileSession, revision, request);
    }

    private void PublishTaskbarMood()
    {
        var mood = CurrentMood;
        var title = mood is null ? null : string.IsNullOrWhiteSpace(mood.PersianTitle) ? mood.Title : mood.PersianTitle;
        TaskbarProfileBridge.PublishMood(_taskbarProfileSession, mood?.PublicId, mood?.Emoji, title,
            _taskbarMoodAnimationPath, _taskbarMoodEffectPath);
    }

    private async Task PrepareTaskbarMoodAsync(Guid animationId, Guid effectId, int profileSession, long taskbarSession,
        long revision, CancellationTokenSource request)
    {
        bool IsCurrent() => !request.IsCancellationRequested && _hasAccount && !IsSigningOut &&
            revision == _taskbarMoodRevision && profileSession == _profileSession && taskbarSession == _taskbarProfileSession;
        try
        {
            var animationPath = await GetMoodMediaPathAsync(animationId, request.Token);
            if (!IsCurrent()) return;
            _taskbarMoodAnimationPath = animationPath;
            // Show the looping mood as soon as it is ready, without waiting for
            // its larger click effect. No catalog/previews are sent to the companion.
            PublishTaskbarMood();
            var effectPath = effectId == animationId ? animationPath : await GetMoodMediaPathAsync(effectId, request.Token);
            if (!IsCurrent()) return;
            _taskbarMoodEffectPath = effectPath;
            PublishTaskbarMood();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrent()) AppLog.Error("Taskbar mood media could not be prepared.", exception);
        }
        finally
        {
            if (ReferenceEquals(_taskbarMoodRequest, request)) _taskbarMoodRequest = null;
            request.Dispose();
        }
    }

    private void ClearMood()
    {
        _moodMutationRevision++;
        _taskbarMoodRevision++;
        _taskbarMoodRequest?.Cancel();
        _taskbarMoodRequest = null;
        _taskbarMoodSession = 0;
        _taskbarMoodId = null;
        _taskbarMoodAnimationId = _taskbarMoodEffectId = Guid.Empty;
        _taskbarMoodAnimationPath = _taskbarMoodEffectPath = null;
        // Releasing the shell must not clear the independent companion's saved
        // presentation. Authentication invalidation publishes logout separately.
        _moodMediaCache?.Dispose();
        _moodMediaCache = null;
        _moodCatalog = null;
        _moodCatalogTask = null;
        _moodExplicitlyCleared = false;
        CurrentMood = null;
        MoodStatus = string.Empty;
        IsMoodSaving = false;
    }
}
