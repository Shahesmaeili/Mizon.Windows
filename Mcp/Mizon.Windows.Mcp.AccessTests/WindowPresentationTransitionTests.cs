using Mizon.Windows.Services;

internal static partial class Program
{
    private static void WindowPresentationTransitions()
    {
        using (var host = new TransitionHost())
        {
            host.Transition.Request();
            Assert(!host.FullScreen && host.Applied.Count == 0 && host.Queue.Count == 1,
                "UI fullscreen requests leave the input callback before changing the native presenter");
            host.Transition.Request();
            host.Drain();
            Assert(!host.FullScreen && host.Applied.Count == 0, "Two rapid toggles cancel against the pending target");
            host.Transition.Request(true);
            host.Transition.Request(false);
            host.Transition.Request(true);
            Assert(host.Queue.Count == 1, "Multiple pending targets retain one dispatcher callback");
            host.Drain();
            Assert(host.FullScreen && host.Applied.SequenceEqual([true]), "The last explicit fullscreen target wins");
            host.Transition.Request(true);
            host.Drain();
            Assert(host.Applied.Count == 1, "An already satisfied request avoids another native presenter mutation");
            host.Transition.Request(false);
            host.Transition.Set(true);
            Assert(host.FullScreen, "Direct setting provides synchronous state readback");
            host.Drain();
            Assert(host.FullScreen && host.Applied.Count == 1, "Direct setting invalidates an older queued exit");
            host.Transition.Request(false);
            host.Transition.Set(false);
            host.Transition.Request(true);
            Assert(!host.FullScreen && host.Queue.Count == 1, "A new UI request after a direct set reuses the queued slot");
            host.Drain();
            Assert(host.FullScreen && host.Applied.SequenceEqual([true, false, true]),
                "Only intentional requests newer than the direct set may run afterward");
        }
        using (var host = new TransitionHost())
        {
            host.AcceptQueue = false;
            host.Transition.Request(true);
            Assert(host.Errors.Count == 1 && host.Applied.Count == 0 && host.Queue.Count == 0,
                "A rejected dispatcher enqueue reports once without applying inline");
            host.AcceptQueue = true;
            host.Transition.Request();
            host.Drain();
            Assert(host.FullScreen, "A failed enqueue discards its target and cannot poison a later toggle");
            host.EnqueueError = new IOException("synthetic dispatcher failure");
            host.Transition.Request(false);
            Assert(host.Errors.Count == 2 && host.FullScreen, "A thrown enqueue error is reported without changing the presenter");
            host.EnqueueError = null;
            host.Transition.Request(false);
            host.Drain();
            Assert(!host.FullScreen, "Requests recover after an enqueue exception");
            host.ApplyError = new IOException("synthetic native failure");
            host.Transition.Request(true);
            host.Drain();
            Assert(!host.FullScreen && host.Errors.Count == 3 && host.Queue.Count == 0,
                "A failed UI apply reports once and is never automatically retried");
            var failure = Capture(() => host.Transition.Set(true));
            Assert(ReferenceEquals(failure, host.ApplyError) && host.Errors.Count == 3,
                "Direct apply failures propagate to the MCP boundary rather than the UI reporter");
            host.ApplyError = null;
            host.Transition.Set(true);
            Assert(host.FullScreen, "Direct state setting recovers after a failed native operation");
        }
        using (var host = new TransitionHost())
        {
            host.BeforeApply = enabled =>
            {
                host.BeforeApply = null;
                host.Transition.Request(!enabled);
                Assert(host.Queue.Count == 0, "Requests raised inside native apply do not queue or apply recursively");
                Assert(Capture(() => host.Transition.Set(!enabled)) is InvalidOperationException,
                    "A reentrant direct set is explicitly rejected");
            };
            host.Transition.Request(true);
            host.Drain();
            Assert(host.FullScreen && host.Queue.Count == 1 && host.Applied.Count == 1,
                "A request received during apply is deferred to the next dispatcher turn");
            host.Drain();
            Assert(!host.FullScreen && host.Applied.SequenceEqual([true, false]), "Deferred requests run after the first native transition completes");
            host.BeforeApply = enabled =>
            {
                host.BeforeApply = null;
                host.Transition.Request(!enabled);
            };
            host.Transition.Set(true);
            Assert(host.FullScreen && host.Queue.Count == 1, "Requests during a direct MCP set also wait for a later dispatcher turn");
            host.Drain();
            Assert(!host.FullScreen, "UI request issued during direct setting retains its intended target");
        }
        using (var host = new TransitionHost())
        {
            host.Transition.Request(true);
            host.Transition.Dispose();
            host.Drain();
            host.Transition.Request(true);
            Assert(host.Applied.Count == 0 && host.Queue.Count == 0 && host.Errors.Count == 0,
                "Closing the owner makes both queued and later UI requests inert");
            Assert(Capture(() => host.Transition.Set(true)) is ObjectDisposedException,
                "A direct MCP mutation cannot target a disposed owner");
        }
        using (var host = new TransitionHost())
        {
            host.BeforeRead = host.Transition.Dispose;
            host.Transition.Request(true);
            host.Drain();
            Assert(host.Applied.Count == 0 && host.Errors.Count == 0,
                "Disposal during the state lookup prevents a late native mutation");
        }
        using (var host = new TransitionHost())
        {
            host.ReadError = new IOException("synthetic state failure");
            host.Transition.Request();
            Assert(host.Errors.Count == 1 && host.Queue.Count == 0, "Failed toggle state lookup reports safely without queuing work");
            host.Transition.Request(true);
            host.Drain();
            Assert(host.Errors.Count == 2 && host.Applied.Count == 0, "Failed queued state lookup cannot mutate the window");
            host.ReadError = null;
            host.Transition.Request(true);
            host.Transition.Set(false);
            host.Drain();
            Assert(!host.FullScreen && host.Applied.Count == 0, "Even an idempotent direct set supersedes pending UI work");
        }
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private sealed class TransitionHost : IDisposable
    {
        internal readonly Queue<Action> Queue = new();
        internal readonly List<bool> Applied = [];
        internal readonly List<Exception> Errors = [];
        internal readonly WindowPresentationTransition Transition;
        internal bool FullScreen, AcceptQueue = true;
        internal Exception? EnqueueError, ReadError, ApplyError;
        internal Action? BeforeRead;
        internal Action<bool>? BeforeApply;

        internal TransitionHost() => Transition = new(
            action =>
            {
                if (EnqueueError is not null) throw EnqueueError;
                if (!AcceptQueue) return false;
                Queue.Enqueue(action);
                return true;
            },
            () =>
            {
                BeforeRead?.Invoke();
                if (ReadError is not null) throw ReadError;
                return FullScreen;
            },
            enabled =>
            {
                BeforeApply?.Invoke(enabled);
                if (ApplyError is not null) throw ApplyError;
                Applied.Add(enabled);
                FullScreen = enabled;
            }, Errors.Add);

        internal void Drain() => Queue.Dequeue()();
        public void Dispose() => Transition.Dispose();
    }
}
