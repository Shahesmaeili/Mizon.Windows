using Mizon.API.Client.Operations.Media;
using System.Diagnostics;

namespace Mizon.Windows.PaletteProvider;

internal sealed class PaletteResultState(PaletteResultData result) : IPaletteResultState, IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _download;
    private string? _image, _message;
    private int? _percentage;
    private bool _closed, _detailsLoading, _imageLoading, _canDownload = result.Kind == PaletteResultKind.Music;
    private Guid? _imageId;
    private long _imageGeneration, _imageNextAttempt, _detailsNextAttempt;
    private PaletteResultData _result = result;
    public PaletteResultData Result { get { lock (_gate) return _result; } }
    public event Action? Changed;
    public PaletteResultSnapshot GetSnapshot()
    {
        lock (_gate) return new(_image, _download is not null, _percentage, _message, _canDownload);
    }
    internal void UpdateSearchSummary(PaletteResultData data, Guid? mediaId)
    {
        bool changed;
        lock (_gate)
        {
            if (_closed) return;
            var updated = _result with
            {
                Title = data.Title, PersianTitle = data.PersianTitle, Username = data.Username,
                FirstName = data.FirstName, LastName = data.LastName, NickName = data.NickName,
                SearchQuery = data.SearchQuery, MatchedTitle = data.MatchedTitle,
                MatchedSubtitle = data.MatchedSubtitle, RelevanceScore = data.RelevanceScore
            };
            changed = _result != updated;
            _result = updated;
        }
        if (changed) Notify();
        EnsureImage(mediaId);
    }
    internal void EnsureImage(Guid? id)
    {
        CancellationToken token;
        long generation;
        bool clear;
        lock (_gate)
        {
            if (_closed) return;
            clear = _imageId != id;
            if (clear)
            {
                _imageId = id; _image = null; _imageLoading = false;
                _imageNextAttempt = 0; ++_imageGeneration;
            }
            if (id is not null && (_imageLoading || _image is not null || Environment.TickCount64 < _imageNextAttempt)) return;
            _imageLoading = id is not null;
            generation = _imageGeneration;
            token = _lifetime.Token;
        }
        if (clear) Notify();
        if (id is { } value) _ = LoadImageAsync(value, generation, token);
    }
    public void RequestDetails()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_closed || _detailsLoading || Environment.TickCount64 < _detailsNextAttempt) return;
            _detailsLoading=true;
            token = _lifetime.Token;
        }
        _ = LoadDetailsAsync(token);
    }
    private async Task LoadDetailsAsync(CancellationToken token)
    {
        try
        {
            using var api = new PaletteApiSession(PaletteApiSession.RequireAuthentication());
            var (data, media, canDownload) = await api.DetailsAsync(Result,token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_closed) return;
                // Details may finish after a newer query reused this item.
                _result = PaletteSearchRelevance.Apply(data, _result.SearchQuery);
                _canDownload=canDownload; _detailsNextAttempt = Environment.TickCount64 + 60_000;
            }
            Notify();
            EnsureImage(media);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log.Write($"Search details unavailable: {Result.Key}; {error.GetType().Name}"); }
        finally
        {
            lock (_gate)
            {
                _detailsLoading = false;
                _detailsNextAttempt = Math.Max(_detailsNextAttempt, Environment.TickCount64 + 5_000);
            }
        }
    }
    private async Task LoadImageAsync(Guid id, long generation, CancellationToken token)
    {
        try
        {
            using var api = new PaletteApiSession(PaletteApiSession.RequireAuthentication());
            var path = await api.ImageAsync(id, token).ConfigureAwait(false);
            lock (_gate) { if (_closed || _imageGeneration != generation) return; _image = path; }
            Notify();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log.Write($"Search image unavailable: {Result.Key}; {error.GetType().Name}"); }
        finally
        {
            lock (_gate)
                if (_imageGeneration == generation)
                {
                    _imageLoading = false;
                    _imageNextAttempt = Environment.TickCount64 + 5_000;
                }
        }
    }
    public void Invoke(string action)
    {
        lock (_gate) if (_closed) return;
        if (action == "cancel-download")
        {
            CancellationTokenSource? download;
            lock (_gate) download = _download;
            try { download?.Cancel(); } catch (ObjectDisposedException) { }
            return;
        }
        if (action == "download" && Result.Kind == PaletteResultKind.Music)
        {
            CancellationTokenSource cancellation;
            lock (_gate)
            {
                if (_closed || !_canDownload || _download is not null) return;
                cancellation = new CancellationTokenSource();
                _download = cancellation; _percentage = 0; _message = "در حال دریافت آهنگ…";
            }
            Program.ChangeServerLock(true); // A user-started download survives closing Search.
            Notify();
            _ = DownloadAsync(cancellation);
            return;
        }
        try
        {
            var uri = Result.Kind == PaletteResultKind.Music ? $"mizon://music/{Result.PublicId:D}" : $"mizon://client/{Result.PublicId:D}";
            switch (action)
            {
                case "play" when Result.Kind == PaletteResultKind.Music:
                    Launch(uri + "?action=play"); SetMessage("درخواست پخش به Mizon ارسال شد."); break;
                case "details" when Result.Kind == PaletteResultKind.Music:
                case "profile" when Result.Kind == PaletteResultKind.Client:
                    Launch(uri); break;
                case "copy-link":
                    PaletteClipboard.Copy(uri); SetMessage("لینک کپی شد."); break;
                case "copy-title":
                    PaletteClipboard.Copy(Result.DisplayTitle); SetMessage("عنوان کپی شد."); break;
                case "copy-username" when Result.Kind == PaletteResultKind.Client && !string.IsNullOrWhiteSpace(Result.Username):
                    PaletteClipboard.Copy("@" + Result.Username.Trim()); SetMessage("نام کاربری کپی شد."); break;
            }
        }
        catch (Exception error) { Log.Write($"Search action failed: {action}; {error.GetType().Name}"); SetMessage("اجرای عملیات ممکن نشد؛ دوباره امتحان کنید."); }
    }
    private async Task DownloadAsync(CancellationTokenSource cancellation)
    {
        try
        {
            using var api = new PaletteApiSession(PaletteApiSession.RequireAuthentication());
            var reporter = new ProgressSink(value =>
            {
                var percentage = (int)value.Percentage;
                lock (_gate)
                {
                    if (_closed || !ReferenceEquals(_download,cancellation) || _percentage == percentage) return;
                    _percentage = percentage;
                }
                Notify();
            });
            await api.DownloadMusicAsync(Result.PublicId, reporter, cancellation.Token).ConfigureAwait(false);
            lock (_gate) { if (_closed) return; _percentage = 100; _message = "دانلود کامل شد؛ آهنگ در کش مشترک Mizon موجود است."; }
        }
        catch (OperationCanceledException) { SetMessage("دانلود لغو شد؛ ادامهٔ دریافت در تلاش بعدی امکان‌پذیر است."); }
        catch (PaletteLoginRequiredException) { SetMessage("برای دانلود، دوباره وارد حساب Mizon شوید."); }
        catch (FileNotFoundException) { lock (_gate) _canDownload = false; SetMessage("فایل این آهنگ در دسترس نیست."); }
        catch (Exception error) { Log.Write($"Music download failed: {Result.Key}; {error.GetType().Name}"); SetMessage("دانلود انجام نشد؛ دوباره امتحان کنید."); }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_download,cancellation)) _download = null; }
            cancellation.Dispose();
            try { Notify(); }
            finally { Program.ChangeServerLock(false); }
        }
    }
    private static void Launch(string uri) { using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
    private void SetMessage(string value) { lock (_gate) { if (_closed) return; _message = value; } Notify(); }
    private void Notify()
    {
        Action? handlers;
        lock (_gate) { if (_closed) return; handlers = Changed; }
        if (handlers is null) return;
        try
        {
            using var apartment = new ComApartment();
            foreach (Action handler in handlers.GetInvocationList())
                try { handler(); } catch (Exception error) { Log.Error("Search item update", error); }
        }
        catch (Exception error) { Log.Error("Search notification apartment", error); }
    }
    public void Dispose()
    {
        lock (_gate) { if (_closed) return; _closed = true; Changed = null; }
        _lifetime.Cancel();
        // In-flight workers own their linked token sources until finally.
        _lifetime.Dispose();
    }
    private sealed class ProgressSink(Action<MediaDownloader.MediaDownloadProgress> report) : IProgress<MediaDownloader.MediaDownloadProgress>
    { public void Report(MediaDownloader.MediaDownloadProgress value) => report(value); }
}
