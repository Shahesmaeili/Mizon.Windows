using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
namespace Mizon.Windows.Taskbar;
    // Application supplies normal WinUI resources and its message loop, without creating a Window.
    public sealed partial class TaskbarApplication : Application, IDisposable
    {
        private readonly EventWaitHandle _exitRequested;
        private readonly int _duration;
        private TaskbarProfileHost? _host;
        private RegisteredWaitHandle? _exitWait;
        private Timer? _diagnosticExit;
        private bool _disposed;

        internal TaskbarApplication(EventWaitHandle exitRequested, int duration)
        {
            _exitRequested = exitRequested;
            _duration = duration;
            InitializeComponent();
            UnhandledException += OnUnhandledException;
            TaskbarPaths.Log("WinUI application constructed.");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            _host = new TaskbarProfileHost(queue);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitRequested,
                (_, _) => queue.TryEnqueue(RequestExit), null, Timeout.Infinite, true);
            if (_duration != 0)
                _diagnosticExit = new Timer(_ => queue.TryEnqueue(RequestExit), null, TimeSpan.FromSeconds(_duration), Timeout.InfiniteTimeSpan);
            TaskbarPaths.Log("Companion started. Native taskbar child only; independent of the main application.");
        }

        private void RequestExit()
        {
            Dispose();
            Exit();
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
            => TaskbarPaths.Log($"XAML exception: {args.Message}; {args.Exception}");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _diagnosticExit?.Dispose();
            _exitWait?.Unregister(null);
            _host?.Dispose();
            _host = null;
            UnhandledException -= OnUnhandledException;
        }
    }
