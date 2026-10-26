using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;

namespace Mizon.Windows.SearchProvider.Installer;

internal static class Program
{
    private const string ClassId = "{CC183E81-8D59-4EA1-8D35-DBF80CB424E7}";
    private const string ProtocolName = "mizon";
    private const string VersionIndependentProgramId = "Mizon.Windows.SearchProvider";
    private const string VersionedProgramId = "Mizon.Windows.SearchProvider.1";

    public static int Main(string[] args)
    {
        try
        {
            return Install(args);
        }
        catch (Exception exception)
        {
            var logPath = Path.Combine(Path.GetTempPath(), "Mizon.Windows.SearchProvider.Installer.log");
            File.WriteAllText(logPath, exception.ToString());
            return Marshal.GetHRForException(exception);
        }
    }

    private static int Install(string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            Console.Error.WriteLine("Usage: Mizon.Windows.SearchProvider.Installer.exe <path-to-native-provider-dll> [--machine]");
            return 1;
        }

        var machineWide = args.Length == 2 && string.Equals(args[1], "--machine", StringComparison.OrdinalIgnoreCase);
        if (args.Length == 2 && !machineWide)
        {
            Console.Error.WriteLine("Unknown option. Only --machine is supported.");
            return 1;
        }

        var sourcePath = Path.GetFullPath(args[0]);
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Provider DLL was not found: {sourcePath}");
            return 2;
        }

        var installDirectory = machineWide
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mizon", "WindowsSearchProvider")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mizon", "WindowsSearchProvider");
        Directory.CreateDirectory(installDirectory);

        // SearchProtocolHost can retain the previous in-proc server for the
        // lifetime of the host. Never overwrite that locked file; place each
        // native payload beside it under a content-addressed file name and
        // repoint COM registration to the new immutable path.
        var payloadHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant()[..16];
        var targetPath = Path.Combine(installDirectory, $"Mizon.Windows.SearchProvider.{payloadHash}.dll");
        if (!File.Exists(targetPath))
            File.Copy(sourcePath, targetPath, overwrite: false);

        RegisterComServer(targetPath, machineWide);
        RegisterWindowsSearchProtocol(machineWide);
        AddSampleMusicCrawlScope();

        Console.WriteLine($"Installed {(machineWide ? "machine-wide" : "per-user")} native provider: {targetPath}");
        Console.WriteLine("Protocol registration completed for the current user's Mizon music root.");
        return 0;
    }

    private static void RegisterComServer(string providerPath, bool machineWide)
    {
        using var classes = GetRegistryRoot(machineWide).CreateSubKey(@"SOFTWARE\Classes", writable: true)!;

        using (var classKey = classes.CreateSubKey($@"CLSID\{ClassId}", writable: true)!)
        {
            classKey.SetValue(null, "Mizon Windows Search NativeAOT Provider");
            using var inProcServer = classKey.CreateSubKey("InprocServer32", writable: true)!;
            inProcServer.SetValue(null, providerPath);
            inProcServer.SetValue("ThreadingModel", "Both");

            classKey.CreateSubKey("ProgID", writable: true)!.SetValue(null, VersionedProgramId);
            classKey.CreateSubKey("VersionIndependentProgID", writable: true)!.SetValue(null, VersionIndependentProgramId);
            using var shellFolder = classKey.CreateSubKey("ShellFolder", writable: true)!;
            shellFolder.SetValue("Attributes", unchecked((int)0xA0180000), RegistryValueKind.DWord);
        }

        using (var versionIndependent = classes.CreateSubKey(VersionIndependentProgramId, writable: true)!)
        {
            versionIndependent.SetValue(null, "Mizon Windows Search Provider");
            versionIndependent.CreateSubKey("CLSID", writable: true)!.SetValue(null, ClassId);
            versionIndependent.CreateSubKey("CurVer", writable: true)!.SetValue(null, VersionedProgramId);
        }

        using var versioned = classes.CreateSubKey(VersionedProgramId, writable: true)!;
        versioned.SetValue(null, "Mizon Windows Search Provider");
        versioned.CreateSubKey("CLSID", writable: true)!.SetValue(null, ClassId);
    }

    private static void RegisterWindowsSearchProtocol(bool machineWide)
    {
        using var windowsSearch = GetRegistryRoot(machineWide).CreateSubKey(@"SOFTWARE\Microsoft\Windows Search", writable: true)!;
        using var handlers = windowsSearch.CreateSubKey("ProtocolHandlers", writable: true)!;
        handlers.SetValue(ProtocolName, VersionedProgramId);

        using var connectors = windowsSearch.CreateSubKey($@"PHSearchConnectors\{ProtocolName}", writable: true)!;
        connectors.SetValue("Version", 1, RegistryValueKind.DWord);
        connectors.SetValue("Name", "Mizon Music");
        connectors.SetValue("Description", "Mizon music catalogue");
    }

    private static RegistryKey GetRegistryRoot(bool machineWide) => machineWide ? Registry.LocalMachine : Registry.CurrentUser;

    private static void AddSampleMusicCrawlScope()
    {
        const string catalogName = "SystemIndex";
        // Windows Search trims the final slash from a protocol root. The
        // first node must be the protocol host; "music" is a path segment
        // beneath it, not the root itself.
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("The current Windows user does not have a SID.");
        var rootUrl = $"mizon://{userSid}/localhost";
        var legacyRootUrl = $"{rootUrl}/";
        var sampleMusicUrl = $"{rootUrl}/music/0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72";
        var previousMusicRootUrl = $"{rootUrl}/music";
        var previousMusicLegacyRootUrl = $"{previousMusicRootUrl}/";
        // The gatherer probes a protocol root as a directory URL and uses
        // this canonical spelling on Windows: <root>/.  It is not treated as
        // an equivalent URL by the crawl-scope matcher, so include it
        // explicitly as well as the normalized and trailing-slash forms.
        var canonicalDirectoryRootUrl = $"{legacyRootUrl}.";
        const string previousRootUrl = "mizon://localhost/music";
        const string previousLegacyRootUrl = "mizon://localhost/music/";
        const uint followComplexUrls = 0x1;

        var searchManagerType = Type.GetTypeFromCLSID(new Guid("7D096C5F-AC08-4F1F-BEB7-5C22C517CE39"), throwOnError: true)!;
        var searchManager = (ISearchManager)Activator.CreateInstance(searchManagerType)!;
        try
        {
            Marshal.ThrowExceptionForHR(searchManager.GetCatalog(catalogName, out var catalog));
            try
            {
                Marshal.ThrowExceptionForHR(catalog.GetCrawlScopeManager(out var crawlScopeManager));
                try
                {
                    var searchRootType = Type.GetTypeFromCLSID(new Guid("30766BD2-EA1C-4F28-BF27-0B44E2F68DB7"), throwOnError: true)!;
                    var searchRoot = (ISearchRoot)Activator.CreateInstance(searchRootType)!;
                    try
                    {
                        // Replace the earlier non-hierarchical prototype root. This
                        // provider is a tree: the root emits individual music URLs.
                        crawlScopeManager.RemoveRoot(previousLegacyRootUrl);
                        crawlScopeManager.RemoveRoot(previousRootUrl);
                        crawlScopeManager.RemoveRoot(previousMusicRootUrl);
                        crawlScopeManager.RemoveRoot(previousMusicLegacyRootUrl);
                        crawlScopeManager.RemoveRoot(sampleMusicUrl);
                        crawlScopeManager.RemoveRoot(canonicalDirectoryRootUrl);
                        crawlScopeManager.RemoveRoot(rootUrl);
                        Marshal.ThrowExceptionForHR(searchRoot.SetRootUrl(sampleMusicUrl));
                        Marshal.ThrowExceptionForHR(searchRoot.SetIsHierarchical(false));
                        Marshal.ThrowExceptionForHR(searchRoot.SetEnumerationDepth(1));
                        Marshal.ThrowExceptionForHR(searchRoot.SetHostDepth(1));
                        Marshal.ThrowExceptionForHR(searchRoot.SetProvidesNotifications(false));
                        Marshal.ThrowExceptionForHR(searchRoot.SetUseNotificationsOnly(false));
                        Marshal.ThrowExceptionForHR(searchRoot.SetFollowDirectories(true));
                        Marshal.ThrowExceptionForHR(crawlScopeManager.AddRoot(searchRoot));
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(searchRoot);
                    }

                    // A custom protocol is excluded by default. The default
                    // include rule makes the root crawlable by the service;
                    // the user rule keeps it visible in the current user's
                    // scope as well.
                    crawlScopeManager.RemoveScopeRule(previousLegacyRootUrl);
                    crawlScopeManager.RemoveScopeRule(previousRootUrl);
                    crawlScopeManager.RemoveScopeRule(previousMusicRootUrl);
                    crawlScopeManager.RemoveScopeRule(previousMusicLegacyRootUrl);
                    crawlScopeManager.RemoveScopeRule(canonicalDirectoryRootUrl);
                    crawlScopeManager.RemoveScopeRule(rootUrl);
                    Marshal.ThrowExceptionForHR(crawlScopeManager.AddDefaultScopeRule(rootUrl, true, followComplexUrls));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.AddDefaultScopeRule(legacyRootUrl, true, followComplexUrls));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.AddDefaultScopeRule(canonicalDirectoryRootUrl, true, followComplexUrls));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.AddUserScopeRule(rootUrl, true, false, followComplexUrls));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.AddUserScopeRule(legacyRootUrl, true, false, followComplexUrls));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.AddUserScopeRule(canonicalDirectoryRootUrl, true, false, followComplexUrls));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.SaveAll());

                    Marshal.ThrowExceptionForHR(crawlScopeManager.IncludedInCrawlScope(legacyRootUrl, out var legacyRootIncluded));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.IncludedInCrawlScope(rootUrl, out var rootIncluded));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.IncludedInCrawlScope(canonicalDirectoryRootUrl, out var canonicalDirectoryRootIncluded));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.IncludedInCrawlScopeEx(legacyRootUrl, out var legacyRootIncludedEx, out var legacyRootReason));
                    Marshal.ThrowExceptionForHR(crawlScopeManager.IncludedInCrawlScopeEx(rootUrl, out var rootIncludedEx, out var rootReason));
                    Console.WriteLine($"Crawl scope included (root): {rootIncluded}; (slash): {legacyRootIncluded}; (dot): {canonicalDirectoryRootIncluded}");
                    Console.WriteLine($"Crawl scope reason (root): included={rootIncludedEx}, reason={rootReason}; (slash): included={legacyRootIncludedEx}, reason={legacyRootReason}");

                    // Queue a crawl for the root itself. ReindexMatchingURLs
                    // alone does not always enqueue a newly registered custom
                    // Search Root after the Windows Search service restarts.
                    var reindexRootResult = catalog.ReindexSearchRoot(sampleMusicUrl);
                    if (reindexRootResult >= 0)
                    {
                        Console.WriteLine($"ReindexSearchRoot queued: {sampleMusicUrl}");
                    }
                    else
                    {
                        var matchingResult = catalog.ReindexMatchingURLs(sampleMusicUrl);
                        Console.WriteLine($"ReindexSearchRoot HRESULT: 0x{reindexRootResult:X8}; ReindexMatchingURLs HRESULT: 0x{matchingResult:X8}");
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(crawlScopeManager);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(catalog);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(searchManager);
        }
    }

    [ComImport]
    [Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF69")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISearchManager
    {
        [PreserveSig] int GetIndexerVersionStr(out IntPtr version);
        [PreserveSig] int GetIndexerVersion(out uint major, out uint minor);
        [PreserveSig] int GetParameter([MarshalAs(UnmanagedType.LPWStr)] string name, out IntPtr value);
        [PreserveSig] int SetParameter([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr value);
        [PreserveSig] int GetProxyName(out IntPtr proxyName);
        [PreserveSig] int GetBypassList(out IntPtr bypassList);
        [PreserveSig] int SetProxy(int proxyAccess, [MarshalAs(UnmanagedType.Bool)] bool localBypass, uint portNumber, [MarshalAs(UnmanagedType.LPWStr)] string proxyName, [MarshalAs(UnmanagedType.LPWStr)] string bypassList);
        [PreserveSig] int GetCatalog([MarshalAs(UnmanagedType.LPWStr)] string catalogName, out ISearchCatalogManager catalog);
    }

    [ComImport]
    [Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF50")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISearchCatalogManager
    {
        [PreserveSig] int GetName(out IntPtr name);
        [PreserveSig] int GetParameter([MarshalAs(UnmanagedType.LPWStr)] string name, out IntPtr value);
        [PreserveSig] int SetParameter([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr value);
        [PreserveSig] int GetCatalogStatus(out int status, out int pausedReason);
        [PreserveSig] int Reset();
        [PreserveSig] int Reindex();
        [PreserveSig] int ReindexMatchingURLs([MarshalAs(UnmanagedType.LPWStr)] string pattern);
        [PreserveSig] int ReindexSearchRoot([MarshalAs(UnmanagedType.LPWStr)] string rootUrl);
        [PreserveSig] int SetConnectTimeout(uint timeout);
        [PreserveSig] int GetConnectTimeout(out uint timeout);
        [PreserveSig] int SetDataTimeout(uint timeout);
        [PreserveSig] int GetDataTimeout(out uint timeout);
        [PreserveSig] int NumberOfItems(out int count);
        [PreserveSig] int NumberOfItemsToIndex(out int incrementalCount, out int notificationQueue, out int highPriorityQueue);
        [PreserveSig] int URLBeingIndexed(out IntPtr url);
        [PreserveSig] int GetURLIndexingState([MarshalAs(UnmanagedType.LPWStr)] string url, out uint state);
        [PreserveSig] int GetPersistentItemsChangedSink(out IntPtr sink);
        [PreserveSig] int RegisterViewForNotification([MarshalAs(UnmanagedType.LPWStr)] string view, IntPtr sink, out uint cookie);
        [PreserveSig] int GetItemsChangedSink(IntPtr site, ref Guid interfaceId, out IntPtr sink, out Guid catalogResetSignature, out Guid checkPointSignature, out uint lastCheckPointNumber);
        [PreserveSig] int UnregisterViewForNotification(uint cookie);
        [PreserveSig] int SetExtensionClusion([MarshalAs(UnmanagedType.LPWStr)] string extension, [MarshalAs(UnmanagedType.Bool)] bool exclude);
        [PreserveSig] int EnumerateExcludedExtensions(out IntPtr extensions);
        [PreserveSig] int GetQueryHelper(out IntPtr helper);
        [PreserveSig] int SetDiacriticSensitivity([MarshalAs(UnmanagedType.Bool)] bool sensitive);
        [PreserveSig] int GetDiacriticSensitivity([MarshalAs(UnmanagedType.Bool)] out bool sensitive);
        [PreserveSig] int GetCrawlScopeManager(out ISearchCrawlScopeManager crawlScopeManager);
    }

    [ComImport]
    [Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF55")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISearchCrawlScopeManager
    {
        [PreserveSig] int AddDefaultScopeRule([MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.Bool)] bool include, uint followFlags);
        [PreserveSig] int AddRoot(ISearchRoot searchRoot);
        [PreserveSig] int RemoveRoot([MarshalAs(UnmanagedType.LPWStr)] string url);
        [PreserveSig] int EnumerateRoots(out IntPtr roots);
        [PreserveSig] int AddHierarchicalScope([MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.Bool)] bool include, [MarshalAs(UnmanagedType.Bool)] bool isDefault, [MarshalAs(UnmanagedType.Bool)] bool overrideChildren);
        [PreserveSig] int AddUserScopeRule([MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.Bool)] bool include, [MarshalAs(UnmanagedType.Bool)] bool overrideChildren, uint followFlags);
        [PreserveSig] int RemoveScopeRule([MarshalAs(UnmanagedType.LPWStr)] string rule);
        [PreserveSig] int EnumerateScopeRules(out IntPtr rules);
        [PreserveSig] int HasParentScopeRule([MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.Bool)] out bool hasParentRule);
        [PreserveSig] int HasChildScopeRule([MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.Bool)] out bool hasChildRule);
        [PreserveSig] int IncludedInCrawlScope([MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.Bool)] out bool included);
        [PreserveSig] int IncludedInCrawlScopeEx([MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.Bool)] out bool included, out int reason);
        [PreserveSig] int RevertToDefaultScopes();
        [PreserveSig] int SaveAll();
    }

    [ComImport]
    [Guid("04C18CCF-1F57-4CBD-88CC-3900F5195CE3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISearchRoot
    {
        [PreserveSig] int SetSchedule([MarshalAs(UnmanagedType.LPWStr)] string schedule);
        [PreserveSig] int GetSchedule(out IntPtr schedule);
        [PreserveSig] int SetRootUrl([MarshalAs(UnmanagedType.LPWStr)] string rootUrl);
        [PreserveSig] int GetRootUrl(out IntPtr rootUrl);
        [PreserveSig] int SetIsHierarchical([MarshalAs(UnmanagedType.Bool)] bool isHierarchical);
        [PreserveSig] int GetIsHierarchical([MarshalAs(UnmanagedType.Bool)] out bool isHierarchical);
        [PreserveSig] int SetProvidesNotifications([MarshalAs(UnmanagedType.Bool)] bool providesNotifications);
        [PreserveSig] int GetProvidesNotifications([MarshalAs(UnmanagedType.Bool)] out bool providesNotifications);
        [PreserveSig] int SetUseNotificationsOnly([MarshalAs(UnmanagedType.Bool)] bool useNotificationsOnly);
        [PreserveSig] int GetUseNotificationsOnly([MarshalAs(UnmanagedType.Bool)] out bool useNotificationsOnly);
        [PreserveSig] int SetEnumerationDepth(uint enumerationDepth);
        [PreserveSig] int GetEnumerationDepth(out uint enumerationDepth);
        [PreserveSig] int SetHostDepth(uint hostDepth);
        [PreserveSig] int GetHostDepth(out uint hostDepth);
        [PreserveSig] int SetFollowDirectories([MarshalAs(UnmanagedType.Bool)] bool followDirectories);
        [PreserveSig] int GetFollowDirectories([MarshalAs(UnmanagedType.Bool)] out bool followDirectories);
    }
}
