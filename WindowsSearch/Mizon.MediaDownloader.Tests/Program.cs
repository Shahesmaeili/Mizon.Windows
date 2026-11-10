using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Responses.Media;

if (args is ["--child", var workerCache, var childSource, var ready, var workerGate])
{
    using var client = new HttpClient();
    var downloader = new MediaDownloader(client, workerCache);
    var info = Info(childSource);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await foreach (var chunk in downloader.DownloadAsync(info, new Uri(childSource), cancellationToken: timeout.Token))
    {
        if (!File.Exists(ready)) await File.WriteAllTextAsync(ready, Environment.ProcessId.ToString(), timeout.Token);
        while (!File.Exists(workerGate)) await Task.Delay(20, timeout.Token);
    }
    return;
}

var root = Path.Combine(Path.GetTempPath(), "MizonDownloadTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passes = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    passes++; Console.WriteLine("PASS " + name);
}
try
{
    var source = Path.Combine(root, "source.bin");
    var data = new byte[1024 * 1024];
    new Random(42).NextBytes(data);
    await File.WriteAllBytesAsync(source, data);
    var info = Info(source);
    using var handler = new RangeHandler(data);
    using var http = new HttpClient(handler);
    var downloader = new MediaDownloader(http, Path.Combine(root, "http-cache"));
    var second = new MediaDownloader(http, downloader.CacheDirectory);
    var uri = new Uri("https://mizon.test/audio.bin"); // Intercepted by RangeHandler; no network.
    var pair = await Task.WhenAll(downloader.DownloadToCacheAsync(info, uri).AsTask(), second.DownloadToCacheAsync(info, uri).AsTask());
    Check(pair[0] == pair[1], "two independent callers share the same final cache path");
    Check(File.ReadAllBytes(pair[0]).SequenceEqual(data), "concurrent download bytes match the source");
    Check(handler.Requests == info.TotalChunks, "only one caller requests each HTTP chunk");
    var before = handler.Requests;
    await second.DownloadToCacheAsync(info, uri);
    Check(before == handler.Requests, "completed cache bypasses HTTP");
    Check(!File.Exists(pair[0] + ".download-lock"), "cache lease is disposed after success");

    var waiting = new MediaDownloader(http, Path.Combine(root, "wait-cache"));
    var lockPath = waiting.GetCachedFilePath(info) + ".download-lock";
    using (var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    {
        using var cancellation = new CancellationTokenSource(150);
        var cancelled = false;
        try { await waiting.DownloadToCacheAsync(info, uri, cancellationToken: cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "external cancellation interrupts waiting for another cache writer");
        Check(!File.Exists(waiting.GetCachedFilePath(info)), "cancelled waiting caller does not create a final file");

        var session = new MediaDownloader.DownloadSession();
        using var fallback = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sessionTask = waiting.DownloadToCacheAsync(info, uri, session, cancellationToken: fallback.Token).AsTask();
        await Task.Delay(150);
        session.Cancel();
        var cancelledBySession = false;
        try { await sessionTask.WaitAsync(TimeSpan.FromSeconds(1)); }
        catch (OperationCanceledException) { cancelledBySession = !fallback.IsCancellationRequested; }
        catch (TimeoutException)
        {
            fallback.Cancel();
            try { await sessionTask; } catch (OperationCanceledException) { }
        }
        Check(cancelledBySession, "DownloadSession.Cancel interrupts waiting for another cache writer");
    }

    var resumed = new MediaDownloader(http, Path.Combine(root, "resume-cache"));
    await using (var iterator = resumed.DownloadAsync(info, new Uri(source)).GetAsyncEnumerator())
        Check(await iterator.MoveNextAsync(), "interrupted iterator downloads its first chunk");
    Check(!File.Exists(resumed.GetCachedFilePath(info) + ".download-lock"), "disposing an interrupted iterator releases its lease");
    var resumedPath = await resumed.DownloadToCacheAsync(info, new Uri(source));
    Check(File.ReadAllBytes(resumedPath).SequenceEqual(data), "interrupted partial file resumes with exact bytes");

    var childCache = Path.Combine(root, "process-cache");
    var firstReady = Path.Combine(root, "first.ready");
    var secondReady = Path.Combine(root, "second.ready");
    var gate = Path.Combine(root, "continue.gate");
    using var firstChild = StartChild(childCache, source, firstReady, gate);
    using var secondChild = new Process();
    try
    {
        await WaitForFile(firstReady, TimeSpan.FromSeconds(10));
        secondChild.StartInfo = ChildInfo(childCache, source, secondReady, gate);
        Check(secondChild.Start(), "second independent process starts");
        await Task.Delay(350);
        Check(!File.Exists(secondReady), "second process waits while first process owns the partial file");
        await File.WriteAllTextAsync(gate, "continue");
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Task.WhenAll(firstChild.WaitForExitAsync(limit.Token), secondChild.WaitForExitAsync(limit.Token));
        Check(firstChild.ExitCode == 0 && secondChild.ExitCode == 0, "both download processes complete successfully");
        var crossProcess = new MediaDownloader(http, childCache);
        Check(File.ReadAllBytes(crossProcess.GetCachedFilePath(info)).SequenceEqual(data), "cross-process shared cache has exact source bytes");
        Check(!File.Exists(crossProcess.GetCachedFilePath(info) + ".download-lock"), "cross-process lease is removed after both readers finish");
    }
    finally
    {
        StopOwnedChild(firstChild); StopOwnedChild(secondChild);
    }
    Console.WriteLine($"PASS {passes} shared-download assertions. No external network, installed-app mutation or real media download.");
}
finally
{
    var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MizonDownloadTests")) + Path.DirectorySeparatorChar;
    var resolved = Path.GetFullPath(root);
    if (resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) Directory.Delete(resolved, recursive: true);
}

static MediaResponse Info(string source)
{
    var length = new FileInfo(source).Length;
    return new MediaResponse
    {
        MediaPublicId = Guid.Parse("633d38e7-578b-46b4-bc2b-6596d1ce0110"), FileName = "audio.bin", Category = "test",
        FileHash = "fixture", ChunkSize = 32768, FileSize = length, TotalChunks = (int)Math.Ceiling(length / 32768d), StoragePath = source
    };
}
static ProcessStartInfo ChildInfo(params string[] arguments)
{
    var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        info.ArgumentList.Add(typeof(RangeHandler).Assembly.Location);
    info.ArgumentList.Add("--child");
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    return info;
}
static Process StartChild(params string[] arguments) => Process.Start(ChildInfo(arguments)) ?? throw new InvalidOperationException("Could not start test child.");
static void StopOwnedChild(Process process)
{
    try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
    catch (InvalidOperationException) { }
}
static async Task WaitForFile(string file, TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    while (!File.Exists(file)) await Task.Delay(20, cancellation.Token);
}
sealed class RangeHandler(byte[] bytes) : HttpMessageHandler
{
    private int _requests;
    internal int Requests => Volatile.Read(ref _requests);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        await Task.Delay(10, cancellationToken);
        var range = request.Headers.Range!.Ranges.Single();
        var start = (int)range.From!.Value;
        var end = (int)range.To!.Value;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[start..(end + 1)]) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, bytes.Length);
        return response;
    }
}
