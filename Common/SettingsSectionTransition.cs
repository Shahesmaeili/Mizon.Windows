using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Mizon.Windows.Services;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Common;

/// <summary>Animates the persistent settings surface while its lazy section changes.</summary>
internal sealed class SettingsSectionTransition : IDisposable
{
    private readonly UISettings _settings = new();
    private FrameworkElement? _element;
    private Visual? _visual;
    private CancellationTokenSource? _request;
    private TaskCompletionSource? _phaseCompletion;
    private bool? _originalHitTesting;
    private bool _disposed;

    public SettingsSectionTransition(FrameworkElement element)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        _settings.AnimationsEnabledChanged += AnimationsChanged;
    }

    // Called on the UI thread. A newer selection supersedes an unfinished one;
    // navigation remains interactive and only the current callback can be applied.
    public async Task RunAsync(Action applySelection, bool forward, bool rightToLeft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applySelection);
        if (_disposed || cancellationToken.IsCancellationRequested || _element is not { } element)
            return;

        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = _request;
        _request = request;
        previous?.Cancel();

        try
        {
            if (!CanAnimate(element))
            {
                ResetVisual();
                applySelection();
                return;
            }

            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            _visual ??= ElementCompositionPreview.GetElementVisual(element);
            if (_visual.Properties.TryGetVector3("Translation", out _) != CompositionGetValueStatus.Succeeded)
                _visual.Properties.InsertVector3("Translation", Vector3.Zero);

            _originalHitTesting ??= element.IsHitTestVisible;
            element.IsHitTestVisible = false;
            await AnimateAsync(_visual, opening: false, Vector3.Zero, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_request, request) || _disposed) return;

            // Swap while invisible, then let x:Load and its compiled bindings
            // process the new content before revealing the same stable surface.
            applySelection();
            await NextDispatcherPassAsync(element.DispatcherQueue, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_request, request) || _disposed) return;
            RestoreHitTesting();
            if (!CanAnimate(element)) return;

            var direction = (forward ? 1 : -1) * (rightToLeft ? -1 : 1);
            await AnimateAsync(_visual, opening: true, new Vector3(direction * 12, 0, 0), request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Changing section or leaving the page cancels the previous reveal.
        }
        finally
        {
            // An old continuation must not stop the newest selection's motion.
            if (ReferenceEquals(_request, request))
            {
                _request = null;
                ResetVisual();
            }
        }
    }

    private bool CanAnimate(FrameworkElement element) =>
        element.IsLoaded && element.XamlRoot?.IsHostVisible == true && _settings.AnimationsEnabled;

    private async Task AnimateAsync(Visual visual, bool opening, Vector3 start, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _phaseCompletion = completion;
        using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), Vector2.One);
        using var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        using var translation = opening ? visual.Compositor.CreateVector3KeyFrameAnimation() : null;
        using var batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        void Completed(object sender, CompositionBatchCompletedEventArgs args) => completion.TrySetResult();
        batch.Completed += Completed;

        try
        {
            opacity.Duration = TimeSpan.FromMilliseconds(opening ? 230 : 80);
            opacity.InsertExpressionKeyFrame(0, "this.StartingValue");
            opacity.InsertKeyFrame(1, opening ? 1f : 0f, easing);
            if (translation is not null)
            {
                visual.Properties.InsertVector3("Translation", start);
                translation.Duration = opacity.Duration;
                translation.InsertKeyFrame(0, start);
                translation.InsertKeyFrame(1, Vector3.Zero, easing);
            }
            try
            {
                visual.StartAnimation("Opacity", opacity);
                if (translation is not null)
                    visual.Properties.StartAnimation("Translation", translation);
            }
            finally { batch.End(); }

            // A hidden or closing host can omit the compositor's completion event.
            // Bound the wait so it cannot leave the settings surface invisible.
            await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(600), token);
        }
        catch (TimeoutException) { }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            AppLog.Error("Settings section motion was skipped.", error);
        }
        finally
        {
            batch.Completed -= Completed;
            if (ReferenceEquals(_phaseCompletion, completion)) _phaseCompletion = null;
            if (!token.IsCancellationRequested && !_disposed)
            {
                visual.Opacity = opening ? 1 : 0;
                visual.StopAnimation("Opacity");
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
                visual.Properties.StopAnimation("Translation");
            }
        }
    }

    private static async Task NextDispatcherPassAsync(DispatcherQueue dispatcher, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => completion.TrySetResult())) return;
        await completion.Task.WaitAsync(token);
    }

    private void AnimationsChanged(UISettings sender, object args)
    {
        _element?.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed && !_settings.AnimationsEnabled) _phaseCompletion?.TrySetResult();
        });
    }

    private void ResetVisual()
    {
        RestoreHitTesting();
        if (_visual is not { } visual) return;
        visual.Opacity = 1;
        visual.StopAnimation("Opacity");
        if (visual.Properties.TryGetVector3("Translation", out _) == CompositionGetValueStatus.Succeeded)
        {
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            visual.Properties.StopAnimation("Translation");
        }
    }

    private void RestoreHitTesting()
    {
        if (_originalHitTesting is not { } original) return;
        if (_element is { } element) element.IsHitTestVisible = original;
        _originalHitTesting = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.AnimationsEnabledChanged -= AnimationsChanged;
        var request = _request;
        _request = null;
        request?.Cancel();
        _phaseCompletion?.TrySetResult();
        _phaseCompletion = null;
        ResetVisual();
        _visual = null;
        _element = null;
    }
}
