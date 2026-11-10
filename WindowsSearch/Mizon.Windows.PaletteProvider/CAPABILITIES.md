# Native Search presentation capabilities

This records the presentation contracts verified for Mizon on Windows 11 Insider **26340.9843**, inspected on **2026-11-05**. It separates properties that the provider can supply from layout choices made by the Windows host. Successful COM readback proves data transmission; the final taskbar rendering must also be observed.

## Evidence and scope

The implementation was derived from the installed Microsoft Bing Search extension manifest, the installed `OSClient.API.dll` contracts/proxy descriptors, Microsoft public PDB symbols, and controlled native calls in a separate diagnostic process. Research did not establish a public third-party Palettes SDK or a published Microsoft sample implementing this contract. It therefore remains an experimental Windows integration, not a documented Windows App SDK extension point.

The inspected modules are:

| Module | File version | Location |
| --- | --- | --- |
| `OSClient.API.dll` | `2610.19000.0.6000` | `C:\Windows\System32` |
| `OSClient.NET.dll` | `10.0.26100.9843` | `C:\Windows\SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy` |
| `RunUX.AOT.dll` | `10.0.26100.9750` | The same SystemApps directory |

The RVAs below refer only to that `OSClient.NET.dll` version. They are analysis references, not addresses called by production code. Production uses verified COM/WinRT interfaces and vtable contracts isolated in this project.

Microsoft's [October 2026 Search announcement](https://blogs.windows.com/windows-insider/2026/10/07/from-searching-to-doing-building-a-faster-more-streamlined-windows-search/) confirms the new WinUI 3 taskbar experience, inline actions and planned richer previews. It does not document a third-party Palettes registration API. In the initial preview, Start and taskbar Search are distinct hosts.

## Collapsed result and main command

| Capability | Verified contract | Current Mizon use |
| --- | --- | --- |
| Title | `PaletteItem.Title` | Query-matching English/Persian music title or public profile field; the preview retains the normal display title. |
| Subtitle | `PaletteItem.Subtitle`, an `IInspectable[]` | Boxed strings with alternate title or username and result type. It is not an array of HSTRING pointers. |
| Row image/icon | `PaletteIconData.FromUri` or `FromGlyph` | Actual cached cover/profile image, updated in place; music/person glyph fallback. |
| Icon overlay | `PaletteItem.IconOverlay` | Music/profile type badge on a loaded cover/avatar; removed when the row uses a fallback glyph. It does not claim playback or online state. |
| Tags | `PaletteTag(icon, text, tooltip)` | Type, and actual duration/like/view counts when details arrive. |
| Relevance hint | `Properties["Score"]`, boxed Double in **0..1** | Exact/prefix/partial matching against real search fields, including Persian normalization. It is a hint, not control over Windows' global result order. |
| Status | `PaletteStatus` | Real operation messages and download outcomes. Text-only status does not create a fake progress bar. |
| Progress | `PaletteProgressState` | Real download percentage. Native progress is **0..1**; RunUX converts it to percent. |
| Main action | `PaletteItem.Invoker` | Play the selected music or open the selected client profile through Mizon's single-instance deep-link handling. |
| Main action glyph | `Properties["CommandGlyph"]` | Play or profile glyph. |
| Main action text | `Properties["CommandTooltip"]`, and navigation override `Properties["ExecuteCommandTooltip"]` | Persian play/profile text. Despite their names, these values can populate the visible primary command text as well as a tooltip in this build. |
| Context menu | Item `ExecuteQueryDelegate`, query mode `2` | Music play/details/download/cancel/copy; profile open/copy. |
| Nested menus | Native `Container` items with their own query delegate | A real Copy submenu for link, display title and available client username. No fabricated playlist choices. |
| Separators and command icons | Native `Separator` kind and item icon | Used between the primary actions and copy link. |

The provider cannot add arbitrary permanent controls to the global Search textbox or rearrange other providers' results. It supplies the selected item's action and presentation metadata; Windows decides where to render them.

### How the Open label is selected

The inspected host establishes the following path:

| Host method | RVA | Observed behavior |
| --- | --- | --- |
| `PaletteItemViewModel.get_PrimaryCommandGlyph` | `0x010C5CA0` | Reads `Properties["CommandGlyph"]` before native fallback choices. |
| `PaletteItemViewModel.get_PrimaryCommandTooltip` | `0x010C5EC0` | Reads `Properties["CommandTooltip"]`; the default resource includes `ActionButtonTooltip_Open`. |
| `PaletteItemViewModel.GetExecuteCommandTooltip` | `0x010C5FF0` | Reads a nonempty string from `Properties["ExecuteCommandTooltip"]`. |
| `PaletteComposer.GetNavigationCommandText` | `0x00F52300` | Uses that execute-command override in the applicable navigation state. |
| `PaletteComposer.UpdatePrimaryCommand` | `0x00F6E090` | Chooses navigation text or the selected item's primary command tooltip, then assigns it to the `PrimaryCommandText` dependency property. |
| `PaletteComposer.get_PrimaryCommandText` | `0x00F5A0F0` | Reads that dependency property for the Search UI. |

This is why Mizon sets both tooltip keys instead of inventing a `CommandText` or `ExecuteCommandLabel` key. The native URI invoker independently controls what clicking the command does. Typography, button dimensions, alignment and hover styling remain host-owned.

### Accent Action subtitle and relative dates (2026-11-06)

The installed host's action treatment is available through a **separate first element of the rich subtitle array**, `Properties["ProminentFirstSubtitleElement"]` as a boxed Boolean, and `Properties["SubtitleGlyph"]` as a string. Mizon uses a Persian play/profile/search action label and the corresponding glyph; it keeps the normal `Command` kind. The presence of the built-in `WindowsAction` kind does not require an app provider to impersonate a Windows action.

Direct analysis of the installed `OSClient.NET.dll` confirms the Boolean reader at RVA **0x010C3A90**, the glyph string reader at **0x010C3B30**, and a built-in action producer setting both at **0x01AD70D0**. `UseAccentColorForFirstSubtitleElement` is a host view-model property controlled by Windows, not a verified provider dictionary key; Mizon does not set an invented color property. Native round-trip tests check the actual boxed types, glyphs and two-element subtitle array. Exact accent presentation remains host-owned.

The “minutes ago” text is the host's recent-action subtitle, not a database creation/release date supplied by Mizon. `ExcludeFromHistory` is a real known property; `ComposerRanker.RankableItem.Create` reads its boxed Boolean at RVA **0x002F4569**. Mizon now sets it for live entity and fallback actions. It does not erase existing history. `MostRecentlyUsedItemProxy.OnSourceContentChanged` restores its own `SubtitlePrefix` after copying source properties (write at **0x0112087C**), so sending an empty prefix cannot reliably remove dates from older saved entries. Existing recent entries can remain until removed through Windows; no global history, registry or other providers' data is modified.

## Expanded preview

`PalettePreviewData` implements `IPaletteContentDelegate` and a template content/view pair. Windows requests content asynchronously, reads `TemplateJson` and `DataJson`, invokes the action callback, and subscribes to property changes. The provider emits `TemplateJson` and then `DataJson` when the expanded card changes.

The useful interfaces verified here include:

| Interface | IID | Relevant behavior |
| --- | --- | --- |
| `IPaletteContentDelegate` | `AA523F7D-E64D-514F-8FD7-35D65E866EE7` | Returns `IAsyncOperation<PaletteContent>` when the host requests a preview. |
| `IPaletteContent` | `42CD7936-3FDD-59CF-AF35-55844289125F` | Supplies kind `1` (template) and a content view. |
| `IPaletteContentView` | `01760C19-8AD1-545A-881B-E8FA57A4EE2B` | Exposes the verified content kind. |
| `IPaletteTemplateContentView` | `50740111-193A-590D-B3F5-FDB4AB0B71FC` | Template/data JSON, action dispatch and property-change subscription. |

The current cards use:

- Header/type label, Persian right alignment and separate English title/username direction.
- Original music/profile images with a text/glyph fallback before an image loads. The current cards use `Image.style = Person`: Windows renders an ellipse filled with the original bitmap. No circular PNG, recoloring or rewritten photo is produced.
- A compact column hero, separate music metrics and conditional fields with layout-only 12px insets on all four sides. Container/background styles are omitted so Windows keeps its own material. Unknown data is omitted rather than fabricated; actual release dates are explicitly labeled as release dates.
- Music metrics and details only after the real detail APIs respond; profile fields only when present.
- `Action.Submit` buttons that pass a validated action identifier to the provider's state/action boundary.
- Default themed action-button colors, verified against the secondary buttons in light and dark themes instead of forcing a white brush.
- Actual download progress, cancellation and completion/error text, updated without replacing the whole result.

The public [Adaptive Cards project](https://github.com/microsoft/AdaptiveCards) and [template language documentation](https://learn.microsoft.com/en-us/adaptive-cards/templating/language) describe the general JSON/template format. They do not guarantee that every schema feature is enabled in this particular Windows Search renderer. The provider uses the host's native `ProgressBar` extension with a text fallback; a schema version alone does not prove support for every extension.

### Typed-card parsing regression

The actual taskbar host initially displayed only the static header of the live profile card. Getter/subscription logs established that the managed COM projection was working. A separate process then called the installed `AdaptiveCards.ObjectModel.WinUI3.AdaptiveCard.FromJsonString` with the exact card: an unexpanded `"isVisible": "${hasImage}"` produced **“Value for property isVisible was invalid. Expected type bool.”** The same client and music documents with correctly typed expansion returned cards with zero parser errors. Containers and ActionSets were not the cause.

`PaletteCardTemplate.Expand` now substitutes exact field bindings through `Utf8JsonWriter`, preserving booleans and numbers as their JSON types and escaping strings correctly. This is a provider-side serialization fix, not a claim that Windows supports arbitrary XAML or every Adaptive Card element. The retained parser diagnostic and the actual Search UI are distinct checks.

### Constrained preview height

The typed-JSON fix did not by itself resolve the visible header-only card. A separate probe rendered the exact card through the installed WinUI renderer and performed `Measure`/`Arrange` at constrained heights. With the large profile hero, **100, 200 and 300 pixels** caused `WholeItemsPanel` to position the overflowing block and subsequent actions outside its clip; at **400 pixels** they returned to their normal positions. Moving real titles/actions ahead of that block also made them visible in the user's actual Search UI. `Container` and `ActionSet` were supported; the problem was the height of an atomic child.

The host reads `AdaptiveCardContentHeight` with default **300** (the inspected constructor at RVA `0x0101A454` passes `0x12C`) and clamps it to **64..512**. `RenderContent` uses the scaled height for the card's maximum height, and `ExpandingContentPresenter` uses the same limit. No override was present in the tested machine/user System Composer settings. No per-provider height or arbitrary scrolling property was verified.

The revised cards keep the hero image at **80×80**, put short text beside it, place primary action rows before optional single-line metrics/details, and omit inactive branches before rendering. The retained `Mizon.Windows.PaletteProvider.RendererTests` project passed **171 assertions** against actual production content: at widths **360/500** and heights **250/300**, the title, image footprint and primary/copy/details/download buttons remained inside the viewport with zero renderer errors. It also verifies actual 80-pixel ellipse shapes and matching primary/secondary button brushes in light and dark themes. It covers the 42% download state, but the standalone stock renderer does not register RunUX's custom `ProgressBar`; that particular visual is still checked in the actual host. This respects the existing size instead of changing Windows-wide settings.

### Visual limits and charts

A rich preview is not limited to a title and two buttons. The installed host includes custom renderer symbols such as `PaletteAdaptiveCardSliderRenderer` and `PaletteAdaptiveCardLineChartRenderer`, including chart-series parsing, plotting, legends and accessibility naming. That is concrete evidence that the host contains a real chart renderer. It does not establish the complete accepted chart JSON contract for Mizon, nor prove which implementation produced a particular promotional screenshot.

A stock-chart screenshot therefore does not imply that it is a static image, and it also does not demonstrate support for arbitrary application XAML. A chart can be supplied as supported card data once its renderer schema is verified, or as a generated image when an interactive chart is unnecessary. The current Mizon API does not supply a suitable historical series for this preview, so no invented chart is displayed.

There is no verified route in this provider to inject an arbitrary XAML file, `UserControl`, DevWinUI control, WebView, arbitrary animation tree or application event handlers into Search. Being built with WinUI 3 does not itself make the host's visual tree an application extension surface. The verified integration supplies content and callbacks through the native template contract; the host constructs its own controls.

Similarly, native accent/theme rendering is not a promise of pixel-identical Mizon gradients, custom shadows, Iranian fonts or an exact eight-pixel cover radius. These depend on properties explicitly accepted by the installed renderer. Both previews reuse the original downloaded file with native `Person` clipping; final taskbar visual verification remains separate.

### Padded circular previews (2026-11-06)

Version 1.0.0.18 used an `emphasis` container to request the renderer's standard padding. The real Search screenshot showed why that was insufficient: its HostConfig removes this padding and the emphasis style paints an unwanted opaque panel. The stock renderer's 20px padding had hidden that difference from the earlier tests.

Version 1.0.0.19 removes container/card surface styles completely. Separate 12px column gutters and 12px top/bottom min-height spacers provide real layout insets without setting any background, opacity, material or color. Each logical row stays separate so optional content cannot turn the whole card into one oversized atomic child. The template wrappers are prepared once, not rebuilt through reflection on every result.

The revised native renderer test explicitly sets **HostConfig.Spacing.Padding=0** through the verified WinUI3 renderer contract. It passed **426 assertions**, including all-four-side bounds, no provider surface styles, circular photos and the four music buttons sharing a row at widths 360/500 and heights 250/300. Primary actions retain normal host theme styling.

The 1.0.0.20 follow-up also adds independent 8px vertical spacers around action rows. These share the action row's visibility, so an inactive download/no-download branch does not leave a blank gap. The updated **474-assertion** native test verifies both action gaps, unchanged single-row layout and all-four-side insets with host padding still zero.

An additional offscreen test decoded the original 640×640 JPEG through the native ellipse/ImageBrush renderer, repeated music/profile selection, cached unload/reload, collapsed-to-expanded height transitions, and a 1.7-second unload followed by an opacity fade. Original file URIs and decoded photo pixels were retained, and native objects/event subscriptions were released. This verifies the installed renderer in a separate host; it cannot guarantee that RunUX's own selection lifecycle has no additional bug. The standalone renderer's stock palette also differs from Search's host styling.

### Empty or unavailable search

A nonempty query with no Mizon results produces a branded **جستجو در میزون** action. The same fallback is returned after an authentication failure, server failure, or the eight-second response deadline. Partial successful results are preserved; cancelling an older query never produces a stale fallback. The action uses `mizon://search?query=...` with URI escaping and opens the app's existing search field, retaining the query through login. It is a command, not a synthetic music/client entity or indexed file.

## Validation boundaries

### Local interactive playground (2026-11-06)

The explicit `mizon demo` result exercises the native result title/icon/overlay, accent subtitle, tags, status/progress, primary delegate action, context-menu commands and a five-item nested section menu. Its five preview sections keep the test controls within the existing height limit rather than requesting a global host setting change.

Inspection of the installed RunUX renderer registration and native code established these additional card contracts:

| Registered element | Confirmed fields used by the demo | Input callback |
| --- | --- | --- |
| `Input.Slider` | `actionId`, `min`, `max`, `step`, `value`, `title`, `isEnabled` | `{ "value": 65, "committed": true }` |
| `Input.ToggleSwitch` | `actionId`, `title`, `value`, `isEnabled` | `{ "isOn": true }` |
| `Input.Text` | Standard text fields plus `actionId` | `{ "value": "text" }` on submit |
| `Input.ChoiceSet` | Standard choices/style/value plus `actionId` | `{ "value": "selected" }` |
| `ProgressRing` | `value`, `max`, numeric `size` | Display only |
| `Chart.Line` | `data`, `plotHeight`, `showTitle`, `showScale`, `showLegend`, `yMin`, `yMax` | Display/hover |
| `FontIcon` | `glyph`, `size`, `label` | Display only |

Chart data is an array of series with `legend` and `values`; each point supplies `x` and `y`. Colors are left to the host. These are inspected private contracts, not a public SDK promise. The installed native parser accepts all five demo cards and retains their original custom element types. The standalone renderer tests only custom-element fallbacks because the standard renderer does not register RunUX's extensions.

Custom inputs call `IPaletteTemplateContentView.InvokeAction(actionId, payloadJson, "{}")`; the first argument is not `Action.Submit`. The demo has a separate allowlisted handler for these callbacks. Ordinary entity cards retain their existing command dispatch. A native collapsed-row toggle (`Toggle` kind plus `PaletteToggleInvoker`), undo resolution, parameterized rich-title slots and attachments were found in host symbols but are **not implemented or claimed as usable** by this playground.

The user verified the earlier demo's visible native card buttons/progress and interaction in the real taskbar host, and the full compact live card in version 1.0.0.10. The rendering fixtures verify music/client schema, typed visibility, real-data mapping, native menu dispatch, lazy details, image updates, type overlays, download progress, text-only status, subscription removal, rejection of late actions and zero retained COM callback objects. The integrated suite passed 171 assertions including 65 rendering/menu and 23 query-relevance checks; the separate installed-parser suite passed 19. The earlier 44-check rendering suite also passed in an isolated NativeAOT build.

These checks do not automatically prove final live backend authorization, image decoding in the taskbar process, every advertised Adaptive Card feature, final typography or visible placement of the main command. Those are checked against the installed package in the actual new Search UI. [README.md](README.md) tracks the current installed revision and end-to-end verification status.
