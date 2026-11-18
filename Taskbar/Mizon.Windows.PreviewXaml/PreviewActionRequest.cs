using Mizon.Windows.Services;

namespace Mizon.Windows.PreviewXaml;

// The request belongs to the action, so thumbnail recreation does not restart it
// or keep an obsolete XAML element alive until a reply arrives.
internal sealed class PreviewActionRequest(Func<string, Task<int>> launch)
{
    private int _pending;
    internal bool IsPending => Volatile.Read(ref _pending) != 0;

    internal Task? Start(string destination, Action? refresh)
    {
        if (!TaskbarPreviewDeepLink.IsSupported(destination) || Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
            return null;
        return Task.Run(async () =>
        {
            try
            {
                var result = await launch(destination).ConfigureAwait(false);
                PreviewTap.Log(result switch
                {
                    0 => "Preview action completed " + destination,
                    3 => "Preview action returned a failure " + destination,
                    _ => "Preview action could not get a response " + destination
                });
            }
            catch (Exception error) { PreviewTap.Log("Preview action request", error); }
            finally
            {
                Volatile.Write(ref _pending, 0);
                refresh?.Invoke();
            }
        });
    }

    internal static async Task<int> LaunchAsync(string destination)
    {
        // Leave the native input callback before Windows processes the launch.
        await Task.Delay(100).ConfigureAwait(false);
        if (PreviewConfiguration.Read() is null) return 2;
        return await PreviewHelperProcess.RunAsync(Exports.GetHostPath(), "--open-preview-action " + destination)
            .ConfigureAwait(false);
    }
}
