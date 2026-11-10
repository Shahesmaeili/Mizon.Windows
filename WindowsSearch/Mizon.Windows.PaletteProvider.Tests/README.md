# Palette provider ABI tests

This .NET 11 console project links the actual provider C# files. It requires no test framework or additional NuGet packages. It tests the native COM/WinRT boundary, not Windows Search UI visibility.

Run the in-process suite on Windows with the tested Palettes runtime:

```powershell
dotnet run -c Release -p:Platform=x64
```

Run against the already registered **Mizon** provider process:

```powershell
dotnet run -c Release -p:Platform=x64 -- --remote
```

The current in-process and registered-server suites pass **171** and **55** assertions respectively. They cover interface identity, native ownership, event retention, pending/canceled async queries, cumulative progress, close/error handling, actual native factories, typed music/profile cards, query-aware row titles/relevance, nested copy menus and cleanup. Remote mode requires the updated app's protected login handoff and a running backend; it searches `st` for both record types and compares actual preview data/media with the real public detail APIs.

Inspect extension discovery using the default native catalog and the same explicit configuration and kind used by System Composer:

```powershell
dotnet run -c Release -p:Platform=x64 -- --catalog
```

This read-only diagnostic prints bounded discovery snapshots, extension IDs, package family names, publishers, and whether each catalog accepts the installed Mizon extension. It uses verified WinRT interfaces, not build-specific memory addresses. The System Composer catalog uses `QueryResult.SystemComposer` and `SOFTWARE\Microsoft\Windows\System Composer`. The default catalog can also be affected by an existing `PALETTECATALOG_ITEM_SOURCE_CONFIG_KEY` environment variable, whose value is printed without changing it. Exit code 0 means both catalogs accepted Mizon, 2 means at least one did not, and 1 means a native call or diagnostic failed. Catalog acceptance alone does **not** verify API calls, live query routing, or result visibility in the taskbar Search UI.

The tests do not install or register anything, change Windows policy, create music catalogue files, activate Bing or another provider, or launch the music URI. `--remote` activates only the Mizon provider CLSID. A diagnostic host stub is passed only to our own extension, which retains it without invoking host methods.

For the ABI suites, exit code 0 means the selected suite passed; nonzero means a test or native call failed. Successful ABI tests do not establish that System Composer discovers third-party packages or displays their results. The console prints the test process ID so linked-code log entries can be distinguished from calls originating in the Windows host.

Validate the production preview JSON with the **installed Windows Search native Adaptive Cards parser**:

```powershell
dotnet run -c Release -p:Platform=x64 -- --native-parser
```

This opt-in Windows compatibility test loads the existing `RunUX/AdaptiveCards.ObjectModel.WinUI3.dll` from the Windows SystemApps directory in its own process. It does not install a dependency, access account data, open the Search UI, or change Windows settings. It requires the tested native runtime and reports an explicit unsupported-platform error when that runtime is absent.

One rich-preview defect was reproduced by this parser: a raw `"isVisible":"${visible}"` value is rejected because `isVisible` requires a JSON boolean. Production now resolves fields to typed JSON and omits inactive controls before handing the document to Windows. The **19-assertion** test reads actual production `TemplateJson` through its COM interface, checks zero native parsing errors before and after image/progress changes, and verifies the compact column hero and action buttons. The separate RendererTests project checks the additional constrained-height clipping regression; final appearance remains an actual taskbar-host check.

For an explicit real media transfer/cache test, run `dotnet run -c Release -p:Platform=x64 -- --live-api`. Unlike the default fixtures, this mode can download media to the app's shared cache. Its fresh-transfer cancellation/resume check uses an isolated temporary cache and verifies SHA256.

Renderer diagnostics can export exact current production cards without API access:

```powershell
dotnet run -c Release -p:Platform=x64 -- --export-card-fixtures <output-directory> <existing-test-image>
```

Only these exported fixtures use synthetic values; production results and remote/live API checks use real records. The old excluded demo-state/menu/preview tests have been retired in favor of the current result rendering tests.
