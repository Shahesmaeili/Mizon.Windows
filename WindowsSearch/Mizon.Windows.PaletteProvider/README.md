# Mizon native Windows Search provider

This .NET 10 out-of-process COM server feeds music and public client profiles from the existing Mizon streaming search API into the new Windows 11 taskbar Search. It implements the `Windows.UI.Palettes.IPaletteItemSourceExtension` contract observed in the installed System Composer runtime. It does not create per-song index documents or desktop shortcuts, and it does not replace search inside the Mizon application.

The Palettes contract is private and build-specific. The tested system is Windows 11 Insider **26340.9843** with the new taskbar Search enabled. Initial previews still use the older search experience from Start; use the taskbar Search when checking this integration. See Microsoft's [new Search announcement](https://blogs.windows.com/windows-insider/2026/10/07/from-searching-to-doing-building-a-faster-more-streamlined-windows-search/) for that rollout distinction.

## Current status

The local capability playground is available by searching **`mizon demo`**, **`mizondemo`**, or **`دمو میزون`** in the new taskbar Search. It returns one distinct `demo/capabilities` result before authentication or API search. Use the card's previous/next buttons, its primary **بخش بعدی** action, or **بخش‌های نمونه** in the context menu to visit five compact sections:

1. Original image, sample music metadata, play/pause and like buttons.
2. Native `Input.Slider` and `Input.ToggleSwitch` with returned values.
3. Native `Chart.Line` with two explicitly fabricated weekly series.
4. Native progress bar/ring and bounded simulated progress with cancellation.
5. Text, choice, numeric and checkbox inputs with local validation/submit.

All demo actions are local in-memory operations; they do not call Mizon APIs, play audio, download files or write account data. Progress stops on cancellation, leaving its section, reset or disposal. Slider samples do not rebuild the card until the drag commits. Normal music/profile streaming, permissions and actions retain their existing paths. Custom elements include fallbacks for a host lacking a renderer. Native parser verification and standalone fallback layout checks do not replace the actual Search-host visual check.

**2026-11-06: 1.0.0.21 installed**, payload SHA256 `f44de576e2a57ef3fe73ce0137697db204e150386438afb339e6d78e42c0a9c6`. The default regression suite passed **315 assertions**, including **103 local demo checks**; the installed native parser passed **49**, and the standalone renderer passed **584**, including **110 demo fallback-layout checks**. The installed package passed **37 remote demo assertions**, covering its real COM query, preview, input callbacks, menu commands, progress and cleanup. Final custom-control visibility in taskbar Search remains a separate manual check.

The current implementation includes padded native circular previews, customizable action subtitles, history exclusion and a branded empty/error-search action. `Deploy.ps1 -Inspect` reports the authoritative installed revision and hashes; the deployment log records each immutable package.

**2026-11-06: 1.0.0.19 installed**, payload SHA256 `8be9d21292e7384f15dc94bb6e130f0f02a4551815eda48cc727eea7a92ef9f8`. It removes the opaque emphasis surface and adds layout-only 12px spacing on all four sides. Native layout verification now explicitly uses zero host padding and passed **426 assertions**, avoiding reliance on the stock renderer's default padding. Original-image decoding/reselection and cleanup also passed. The installed package passed **55 remote live-result checks**: `st` returned **13 records and 13 cumulative progress events**. Version 1.0.0.18 previously passed **7 remote unmatched-query fallback checks**, and the user confirmed that the fallback appears in Search and opens the correct query in Mizon.

**1.0.0.20** adds independent **8px above and below the action row**, retaining the 12px outer insets and host-owned material. Its production cards passed **474 native layout assertions** and **19 native parser assertions**, including four-button alignment, spacing around the action row, small viewports and the active download state. No image conversion or surface styling was added.

Native contract tests cover streamed result snapshots, asynchronous completion/cancellation, music and profile previews, action dispatch, state changes and disposal. The integrated suite passed **171 assertions**, including **65 rendering/menu checks** and **23 relevance/row-presentation checks**. The installed native Adaptive Card parser passed a separate **19 assertions**. The retained WinUI renderer test passed **338 assertions**, including visible title/image/action bounds at widths **360/500** and heights **250/300**, the normal image element and theme-matching buttons. The earlier 44-check rendering suite also passed in an isolated NativeAOT executable linked to the production renderers.

The real API integration run passed **35 assertions** using the current protected login handoff: `st` returned three music records and ten client records; profile/music details, image downloads, a real music download, unchanged-byte cache reuse and cancellation passed. The registered **1.0.0.12** package also passed **55 remote assertions**, including real lazy preview details, images, native copy menus and cleanup. A fresh transfer was canceled around 30%, resumed, and compared with the source using SHA256.

The user confirmed the full compact live card in **1.0.0.10**, then the circular music/profile images and corrected themed buttons in **1.0.0.12**. Version **1.0.0.13** adds a little more space above the image/title row. Version **1.0.0.14** places music play, details, download/cancel and copy actions in one row, with short labels and descriptive tooltips. Its native renderer suite passes **338 assertions**, including same-row alignment, bounds and non-overlap at widths 360/500, with idle download, active cancellation and no-file states.

Version **1.0.0.17** used a normal image after removing the temporary circular-PNG workaround that altered colors. The current revision follows the user's new request for round images using native `Image.style=Person`, keeping the original shared media-cache file unchanged. The revised padded surface passed **354 native layout assertions**, plus actual original-image decode/reselection/opacity recovery and cleanup checks. The integrated provider suite passed **212 assertions**, including **29 empty/error/timeout/cancellation fallback checks**; native parsing passed **19**. These separate-host tests do not establish the cause of the older Search-only image disappearance.

When a query has no result or the API is unavailable, **جستجو در میزون** opens `mizon://search?query=...` in the app's existing search field. No fake entity or catalog is created. Cancellation while typing produces no stale fallback. The app keeps the pending query through login and handles repeated activation without a second instance. The native subtitle action label/glyph are supplied separately; Windows selects their accent color. Live entities are marked `ExcludeFromHistory`; previously stored Windows recent entries are not deleted. See [CAPABILITIES.md](CAPABILITIES.md) for verified property types and limitations.

The legacy protocol/filter, crawl scope, four indexed sample rows, 17 installed native DLLs, generated connector and sample catalogue have been retired. Main app **1.0.0.2** is registered without the old `.mizonmusic` association; its `mizon://` protocol remains present. The old source projects and excluded demo fixtures are absent from the active tree and solution, with a recoverable copy under ignored `obj/LegacySearchArchive/retired-20261105`. This cleanup does not replace the app's own search.

Earlier milestones were verified separately:

- **1.0.0.2:** the actual Windows taskbar host delivered successive typed queries, displayed the sample result and opened the correct music page when clicked.
- **1.0.0.3:** the user confirmed visible preview buttons, a nested menu and progress, including working interactions.
- **1.0.0.4:** native and cross-process tests verified the replacement preview ownership implementation and its event cleanup.

Those demo playback/like/playlist/progress simulations are not production search behavior. Their obsolete source and fixtures have been removed from the active project; the retained tests exercise the real result state boundary.

## Live data flow

1. Windows discovers the packaged `com.microsoft.palettes` extension and supplies its native item factory through `Initialize`.
2. `ExecuteQueryAsync` returns a genuinely pending WinRT operation. A new query cancels the previous query; a short typing delay avoids starting an API request on every keystroke.
3. `PaletteApiSession` calls the existing `GlobalSearchOperation` through the Mizon SDK. Music and client records are consumed as they arrive; the provider does not impose a music-first or client-first order.
4. The provider sends cumulative native progress snapshots because RunUX replaces this provider's result set on each event. Each query returns at most 20 distinct results.

Streaming was traced end-to-end: separate EF contexts with `AsAsyncEnumerable`, per-record NDJSON plus HTTP flush, SDK `ResponseHeadersRead`/`ReadLineAsync`/`yield return`, then the provider's `await foreach` and native progress. There is no wait for a complete HTTP result list. A backend merge correction on 2026-11-06 removes forced source alternation when the opposite source is slow; only simultaneous readiness uses alternating tie preference. Consumer disposal now cancels and awaits both database producers. The six production-method merge regressions pass, but that backend source change requires a server rebuild/restart separately from provider deployment.
5. Summary fields and cover/profile media IDs come from global search. The image follows `MediaOperation` and the existing `MediaDownloader.DownloadToCacheAsync` path; downloaded images update the existing native item icon and preview.
6. Selecting a result requests its detail API once needed. Music uses `MusicOperation` and `MusicFileOperation`; public profiles use `ClientProfileOperation`. Counts, genres, album, release date and duration appear only when supplied by those responses.

`GetItems` returns an empty static list. Search results are produced by live queries, rather than a pre-published catalogue. `GetItem` can resolve a previously selected `music/{publicId}` or `client/{publicId}` identity.

## Result actions and previews

Music results expose **play**, **music details**, **download/cancel download** when applicable, and **copy link**. Profile results expose **open profile** and **copy link**. These commands are available in the native context menu and in the expanded card. The context menu also groups copying the link, display title and available client username in a native **Copy** submenu. There are no simulated likes, playback state, playlists or counts.

- Music playback activates `mizon://music/{publicId}?action=play`; details use the same URI without the action. Existing single-instance app activation routes to the selected music.
- Profiles activate `mizon://client/{publicId}`.
- Download uses `MusicFileOperation` → `MediaOperation` → the shared SDK downloader. Native row status and card progress reflect the actual transfer. A user-started download holds the COM server alive while Search is closed, and cancellation is explicit.
- Completed media is stored in the application's existing desktop `MizonCache` directory. That directory contains media cache files, not Windows Search catalogue records or desktop shortcuts.
- Missing authentication produces a sign-in result; unavailable backend services produce a connection message rather than a fabricated song.

The expanded card is a native-host-rendered Adaptive Card. `PaletteCardTemplate` expands fields into a complete, correctly typed `TemplateJson` before handing it to Windows; `DataJson` remains the current data snapshot. This avoids the installed renderer's partial-card failure on string-valued boolean bindings such as `isVisible`. Changed cards notify `TemplateJson` before `DataJson`, and unchanged snapshots do not rebuild the card.

Both expanded previews display the original downloaded photo through a normal 80-pixel `Image`, without PNG conversion or circular image processing. Primary action buttons use the host's normal theme instead of its positive/accent style; the actual renderer verifies matching primary/secondary foreground and background in both light and dark themes. Loaded row images have a native music/profile type badge; the fallback glyph has no redundant overlay. Persian text is right-aligned, while English titles and usernames retain their direction. The row title follows the matching language/username, while the expanded preview retains the display title. The provider also supplies a native numeric relevance hint; Windows controls final global ordering. The host owns actual typography, colors and rendering. [CAPABILITIES.md](CAPABILITIES.md) records the verified fields, primary command customization and rendering limits.

The host's `AdaptiveCardContentHeight` defaults to **300**, clamped to **64..512**; the tested system has no override. This is a host-wide setting, not a verified per-provider property. Its `WholeItemsPanel` hides an overflowing whole child and all subsequent children. Consequently, the card uses an 80-pixel image beside short text, early action rows and compact optional metadata instead of a tall atomic hero. No Windows setting is changed to make the card fit.

`Mizon.Windows.PaletteProvider.RendererTests` checks the actual production cards with the installed native WinUI renderer, including the download-progress state. Its stock renderer does not include RunUX's custom `ProgressBar` renderer, so that specific visual remains an actual-host check; it verifies the surrounding controls and layout without claiming otherwise.

## Authentication

The main application publishes its current API address and access token through `Services/PaletteAuthenticationStore.cs`. The token is protected with Windows DPAPI CurrentUser and stored outside packaged AppData virtualization:

```text
%USERPROFILE%\.mizon\WindowsPaletteProvider\Auth\session.dat
```

Start the updated main application and sign in normally. The provider reads this handoff; there is no token in package assets, command-line arguments or source configuration. API authorization remains server-side. Logging out clears the handoff, and an authentication revision prevents cached results from being shared between login sessions.

## Build and registration

From this project directory, use the same x64 configuration for publish and deployment:

```powershell
dotnet publish -c Release -p:Platform=x64 -r win-x64
.\Deploy.ps1 -PublishDirectory .\bin\x64\Release\net10.0-windows\win-x64\publish
.\Deploy.ps1 -Inspect
```

The current packaged server is a self-contained regular .NET executable. NativeAOT is optional for an out-of-process COM server; the isolated COM/ABI diagnostics have also been tested with NativeAOT.

`Deploy.ps1` stages a versioned package under `%USERPROFILE%\.mizon\WindowsPaletteProvider\Packages` and registers it for the current user. `-StageOnly` stages without registration. Deployment neither enables Developer Mode nor changes publisher policy, installs the legacy indexer, or restarts Explorer/WSearch. Publisher policy is a separate explicit operation below.

## Validation

The retained test project links the production implementation and has no external test framework dependency:

```powershell
dotnet run --project ..\Mizon.Windows.PaletteProvider.Tests\Mizon.Windows.PaletteProvider.Tests.csproj -c Release -p:Platform=x64
```

The default suite checks COM identity/ownership, async operation behavior, cumulative native progress, typed music/profile rendering, submenu/action dispatch, type overlays, lazy details, native progress/status and event cleanup. Fixture actions do not launch the app or download media. It prints assertion counts rather than treating compilation as success.

After registration, these modes inspect actual catalog discovery or test the packaged COM server:

```powershell
dotnet run --project ..\Mizon.Windows.PaletteProvider.Tests\Mizon.Windows.PaletteProvider.Tests.csproj -c Release -p:Platform=x64 -- --catalog
dotnet run --project ..\Mizon.Windows.PaletteProvider.Tests\Mizon.Windows.PaletteProvider.Tests.csproj -c Release -p:Platform=x64 -- --remote
dotnet run --project ..\Mizon.Windows.PaletteProvider.Tests\Mizon.Windows.PaletteProvider.Tests.csproj -c Release -p:Platform=x64 -- --native-parser
dotnet run --project ..\Mizon.Windows.PaletteProvider.Tests\Mizon.Windows.PaletteProvider.Tests.csproj -c Release -p:Platform=x64 -- --demo
dotnet run --project ..\Mizon.Windows.PaletteProvider.Tests\Mizon.Windows.PaletteProvider.Tests.csproj -c Release -p:Platform=x64 -- --remote-demo
```

`--catalog` is read-only: it distinguishes the default extension catalog from the real System Composer publisher policy. It does not prove query routing or UI visibility.

`--demo` verifies the local capability state, API/authentication bypass, the native card/action/menu interfaces, slider commits, form submission, progress cancellation and COM cleanup. `--remote-demo` repeats the native result/preview/action checks against the installed packaged COM server; all actions remain inside the in-memory sample.

`--native-parser` loads the Adaptive Cards DLL installed with this Windows Search build and checks the actual production card JSON. It also retains the invalid boolean-binding regression case. It opens no window and verifies parsing, not the constrained-height visual layout.

The separate opt-in renderer project checks the actual WinUI visual tree and layout, using the existing WindowsAppSDK dependency:

```powershell
dotnet run --project ..\Mizon.Windows.PaletteProvider.RendererTests\Mizon.Windows.PaletteProvider.RendererTests.csproj -c Release -- --native-renderer
```

This headless test opens no visible window and changes no shell registration. It links the production preview code, loads Windows Search's installed native Adaptive Cards renderer, and measures/arranges cards at widths 360/500 and heights 250/300. It reproduces the old header-only bug: `WholeItemsPanel` moves an oversized child and every subsequent child outside the visible area. It checks that the compact production title, image footprint and primary actions remain in bounds, including during download state, with zero native render errors. Because an unattached visual tree may defer bitmap decoding, the declared 80-pixel image footprint is explicitly applied for layout checks. This does not test image decoding or RunUX's custom `ProgressBar` renderer and does not replace the final Windows Search visual check.

To diagnose repeated image selection with an existing original local photo, add `--image-reselection "C:\path\image.jpg"` after `--native-renderer`. This explicitly opens a non-activating offscreen test window, hidden from task switching, to provide a real `XamlRoot`. It checks bitmap decoding, repeated music/profile preview selection, collapsed-height recovery, renderer lifetime and actual `RenderTargetBitmap` photo pixels after a 1.7-second unload followed by an opacity fade. It reads the photo directly without rewriting it. The window and native references are released in `finally`. It does not automate Windows Search or interact with the running Mizon app. Its renderer result is separate from actual Search-host behavior.

`--remote` runs a real backend search for `st`, requiring a valid main-app login, a running backend and matching test data containing both music and client records. It checks mixed results, distinct IDs and cumulative progress across the process boundary, then requests each result type's actual lazy preview. Preview titles/details are compared with an independent real API read, image bytes and row icons/type overlays are checked, and the native copy submenu is queried without invoking it. It also verifies template visibility types, property notifications and callback cleanup after releasing the extension. It does not click the result, change the clipboard or download audio. Diagnostic processes log their own PID; these entries must not be confused with Windows host requests.

For the explicit API/media integration test:

```powershell
dotnet run --project ..\Mizon.Windows.PaletteProvider.Tests\Mizon.Windows.PaletteProvider.Tests.csproj -c Release -p:Platform=x64 -- --live-api
```

This mode reads the protected login handoff, queries real music/profiles and details, downloads their media into the shared cache, checks cache reuse without rewriting the music bytes, and verifies cancellation. It can populate the production cache and requires at least one existing playable music file. It does not alter the authentication handoff or click anything in Windows Search.

`--remote-search-fallback` instead sends the unmatched query `aflaskjflajsvlakjsv` to the installed COM extension and verifies the branded fallback title, query identity, icon, invoker and cleanup. It does not launch the app or assert that Windows' global ranking displays the row.

The final manual check is in the actual **new taskbar Search**: enter a query, observe music and profiles, select each type to inspect its card, and use its actions. Check cancellation while typing and confirm that copied/opened links target the selected record. The provider's log and backend log should correspond to the same action.

Logs are kept outside AppData virtualization:

```text
%USERPROFILE%\.mizon\WindowsPaletteProvider\Logs\WindowsPaletteProvider.log
%USERPROFILE%\.mizon\WindowsPaletteProvider\Logs\WindowsPaletteProvider.Deploy.log
```

For a shell-only preview issue, create an empty `PreviewDiagnostics.enabled` file in that log directory before restarting the provider. The opt-in trace records repeated selections, whether a local image still exists, template/data reads and host event subscription changes. It does not log image URLs, authentication data or card text. Remove the marker and restart the provider to disable this trace. Successful standalone rendering does not prove that a particular Windows Search host renders the same card correctly.

## Ownership and cancellation

`PaletteSearchSession` owns a bounded result cache and the active query. `PaletteResultItem` owns its state, native view and presentation/preview attachments. `PendingQueryOperation` holds an independent worker reference until the asynchronous work finishes, and releases results, progress/completion callbacks and HSTRINGs through explicit ownership paths.

The preview uses separate managed COM content and view identities. This avoids a reference cycle observed in the installed native `PaletteTemplateContent`/view implementation. Disposal unsubscribes state changes and host handlers; late preview actions return `RO_E_CLOSED`. API/image tasks are canceled when their result state is disposed. User-started downloads have a separate lifetime so closing Search does not truncate an explicitly requested transfer.

## Explicit local publisher registration

This Windows build filters System Composer extensions through `HKLM\SOFTWARE\Microsoft\Windows\System Composer\AllowedPublishers`. Initially, the default catalog included Mizon while the System Composer catalog excluded it. The key is owned by TrustedInstaller and is read-only even to an ordinary elevated administrator.

The explicitly approved local setup added only publisher **yvtn54431hrs8** (`CN=alish`). This entry is publisher-wide, not limited to this one package. `PublisherPolicy.ps1` uses `REG_OPTION_BACKUP_RESTORE` on the already-open exact key with temporarily enabled process privileges. It never changes the key owner or DACL, restores previous privilege attributes, and verifies that the security descriptor is unchanged.

To reproduce that explicit setup, review the script and run from **64-bit Windows PowerShell 5.1 as Administrator**:

```powershell
.\PublisherPolicy.ps1 -Action Enable -UseRestorePrivilege
```

The original state and descriptor are saved in `C:\ProgramData\Mizon.PaletteProvider.Policy\OriginalPolicy.json`, protected for Administrators and SYSTEM. The policy log is `%USERPROFILE%\.mizon\WindowsPaletteProvider\PublisherPolicy.log`.

To remove only Mizon's added entry, preserving other entries and permissions:

```powershell
.\PublisherPolicy.ps1 -Action Rollback -UseRestorePrivilege
```

If interrupted, `-Action Recovery -UseRestorePrivilege` restores the recorded prior absence of the entry. A matching `Prepared` record can also be reconciled by rerunning Enable; it validates the installed package, recorded state and unchanged security before updating the record. Normal build/deployment never invokes these policy actions.

The tested System Composer catalog changed from 17 sources without Mizon to 18 with Mizon. This result establishes catalog acceptance on this machine, not a public Windows extensibility guarantee.
