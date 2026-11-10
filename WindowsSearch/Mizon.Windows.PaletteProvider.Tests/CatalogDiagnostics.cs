using System.Runtime.InteropServices;
using Mizon.Windows.PaletteProvider;

internal static unsafe class CatalogDiagnostics
{
    private const string ComposerPolicy = @"SOFTWARE\Microsoft\Windows\System Composer";
    private const string ProviderPackagePrefix = "Mizon.Windows.PaletteProvider_";

    // Contracts verified against the installed Windows Palettes runtime and its matching Microsoft symbols.
    private static readonly Guid CatalogStaticsId = new("084d2066-3184-50f3-a0ff-9804ff4c0002");
    private static readonly Guid CatalogId = new("75d4e7e7-9b3e-577d-9c34-867643fd711a");
    private static readonly Guid DefinitionId = new("ca5cc86a-8c7e-5db6-b46e-d588deee0ae7");
    private static readonly Guid KindsFactoryId = new("be554067-0c61-5273-b66e-dc142f6e01a9");

    internal static int Run()
    {
        // Main owns the paired RoInitialize/RoUninitialize calls.
        Console.WriteLine("Read-only Palettes discovery diagnostic. No query, item invocation, policy change, or UI visibility test is performed.");
        Console.WriteLine($"Windows: {Environment.OSVersion.Version}");
        Console.WriteLine($"Default-catalog environment override: {Environment.GetEnvironmentVariable("PALETTECATALOG_ITEM_SOURCE_CONFIG_KEY") ?? "<not set>"}");
        var defaultAccepted = Probe("Default catalog", null);
        var composerAccepted = Probe("System Composer catalog", ComposerPolicy);
        Console.WriteLine($"Mizon accepted by default catalog: {defaultAccepted}");
        Console.WriteLine($"Mizon accepted by System Composer policy: {composerAccepted}");
        Console.WriteLine("Windows taskbar Search query routing and result visibility: NOT TESTED.");
        return defaultAccepted && composerAccepted ? 0 : 2;
    }

    private static bool Probe(string label, string? configuration)
    {
        nint factory = 0, instance = 0, catalog = 0, kindsFactory = 0, kinds = 0;
        try
        {
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteCatalog", CatalogStaticsId);
            if (configuration is null)
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(factory)[10])(factory, &instance));
            }
            else
            {
                kindsFactory = NativeMethods.Factory("Windows.UI.Palettes.PaletteKinds", KindsFactoryId);
                var kind = NativeMethods.String("QueryResult.SystemComposer");
                try
                {
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint*, int>)NativeMethods.Table(kindsFactory)[6])(kindsFactory, 1, &kind, &kinds));
                }
                finally { NativeMethods.WindowsDeleteString(kind); }
                var config = NativeMethods.String(configuration);
                try
                {
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)NativeMethods.Table(factory)[12])(factory, kinds, config, &instance));
                }
                finally { NativeMethods.WindowsDeleteString(config); }
            }

            catalog = NativeMethods.As(instance, CatalogId);
            Console.WriteLine($"{label}; configuration={configuration ?? "<default>"}");
            ExtensionInfo[] snapshot = [];
            // Discovery completes asynchronously. Bound observation to two seconds per catalog.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (attempt != 0) Thread.Sleep(500);
                snapshot = ReadExtensions(catalog);
                Console.WriteLine($" Snapshot {attempt + 1}: count={snapshot.Length}, Mizon accepted={Array.Exists(snapshot, IsMizon)}");
            }
            foreach (var extension in snapshot.OrderBy(value => value.Id, StringComparer.Ordinal))
                Console.WriteLine($" {extension.Id} | package={extension.PackageFamilyName} | publisher={extension.Publisher}");
            return Array.Exists(snapshot, IsMizon);
        }
        finally
        {
            NativeMethods.Release(catalog);
            NativeMethods.Release(instance);
            NativeMethods.Release(kinds);
            NativeMethods.Release(kindsFactory);
            NativeMethods.Release(factory);
        }
    }

    private static ExtensionInfo[] ReadExtensions(nint catalog)
    {
        uint count = 0;
        nint* values = null;
        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)NativeMethods.Table(catalog)[7])(catalog, &count, &values));
        var validArray = count <= 1000 && (count == 0 || values != null);
        try
        {
            if (!validArray) throw new InvalidDataException("The native catalog returned an unexpected extension array.");
            var result = new ExtensionInfo[count];
            for (var i = 0; i < count; i++)
            {
                if (values[i] == 0) throw new InvalidDataException("The native catalog returned a null extension.");
                var definition = NativeMethods.As(values[i], DefinitionId);
                try { result[i] = new(GetString(definition, 8), GetString(definition, 9), GetString(definition, 15)); }
                finally { NativeMethods.Release(definition); }
            }
            return result;
        }
        finally
        {
            // Release every returned reference even when reading one definition fails.
            if (validArray)
                for (var i = 0; i < count; i++) NativeMethods.Release(values[i]);
            Marshal.FreeCoTaskMem((nint)values);
        }
    }

    private static string GetString(nint source, int slot)
    {
        nint value = 0;
        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(source)[slot])(source, &value));
        try { return NativeMethods.ReadString(value); }
        finally { NativeMethods.WindowsDeleteString(value); }
    }

    private static bool IsMizon(ExtensionInfo value) => value.PackageFamilyName.StartsWith(ProviderPackagePrefix, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ExtensionInfo(string Id, string PackageFamilyName, string Publisher);
}
