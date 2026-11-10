namespace Mizon.Windows.PaletteProvider;

internal static class Program
{
    private static int _serverLocks;
    private static long _lastActivity = Environment.TickCount64;
    internal static void Activity() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
    internal static void ChangeServerLock(bool locked) { if (locked) Interlocked.Increment(ref _serverLocks); else Interlocked.Decrement(ref _serverLocks); Activity(); }

    [MTAThread]
    private static int Main(string[] args)
    {
        Log.Write($"Start revision=palettes-live-search-3 path={Environment.ProcessPath}");
        var initialized = NativeMethods.RoInitialize(1);
        if (initialized < 0) { Log.Write($"RoInitialize 0x{initialized:X8}"); return 1; }
        uint cookie = 0;
        ProviderClassFactory? factory = null;
        try
        {
            if (!args.Contains("-RegisterProcessAsComServer", StringComparer.OrdinalIgnoreCase))
            {
                Log.Write("Launch through the packaged COM registration with -RegisterProcessAsComServer.");
                return 0;
            }
            factory = new ProviderClassFactory();
            NativeMethods.Check(NativeMethods.CoRegisterClassObject(ProviderClassFactory.ClassId, factory.Identity, 4, 1, out cookie));
            Log.Write("COM class registered; waiting for System Composer.");
            using var exit = new ManualResetEventSlim();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; exit.Set(); };
            Console.CancelKeyPress += cancel;
            try
            {
                while (!exit.Wait(TimeSpan.FromSeconds(5)))
                {
                    if (Volatile.Read(ref _serverLocks) <= 0 && NativeObject.LiveCount == 1 && Environment.TickCount64 - Interlocked.Read(ref _lastActivity) > 60_000)
                    {
                        NativeMethods.Check(NativeMethods.CoSuspendClassObjects());
                        // Include outstanding async operations, not only extension instances.
                        if (Volatile.Read(ref _serverLocks) <= 0 && NativeObject.LiveCount == 1) break;
                        NativeMethods.Check(NativeMethods.CoResumeClassObjects());
                    }
                }
            }
            finally { Console.CancelKeyPress -= cancel; }
            return 0;
        }
        catch (Exception error) { Log.Error("Server", error); return 1; }
        finally
        {
            if (cookie != 0) NativeMethods.CoRevokeClassObject(cookie);
            if (factory is not null) NativeMethods.Release(factory.Identity);
            PaletteApiSession.DisposeTransports();
            NativeMethods.RoUninitialize();
            Log.Write("Server stopped.");
        }
    }
}
