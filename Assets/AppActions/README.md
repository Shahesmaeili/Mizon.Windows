# Mizon Windows App Actions

`Microsoft.AI.Actions` 0.1.0 supplies the COM class factory and runtime helper.
`MizonActionProvider` implements the documented manual-provider path because these
page actions have no entity inputs; this package's annotated-method generator
requires at least one input. Registration is explicit v3 JSON to preserve IDs,
empty input combinations, icons and `allowedAppInvokers`.

The provider CLSID is shared by the package manifest and `AppActionCatalog`.
It is registered in the primary process before XAML starts. Requests wait for
initialization, execute on the UI dispatcher and reuse the current window.
No action submits credentials, signs in, signs out, starts playback or connects VPN.
The actions open the corresponding pages only.

## Availability and language

| Account | Available destinations |
| --- | --- |
| Signed out | Login, Register |
| Signed in | Music, Movies, Apps, VPN |

Welcome is not an App Action. Its internal/deep-link route remains available for
normal session initialization.

Descriptions in the Windows catalog are read-only. Therefore each destination has
a Persian registration and an English `.English` variant. The installed defaults
enable only Persian Login/Register. `AppActionsService` synchronizes availability
at startup, after token changes and after `LocalizationService.LanguageTag` changes.
It disables the previous set before enabling the new set. A bounded queue coalesces
rapid changes; broker calls run off the UI thread. Invocation rechecks account
state because callers can retain old catalog entries.

Windows management tools that list **all registrations** can show both languages,
including unavailable entries. Invocation surfaces should honor availability;
already-open surfaces may need to query again. There is no supported instant
rename, atomic batch, or forced-refresh API for another app's open action menu.
This implementation does not rewrite files in an installed MSIX or change Windows'
display language. Persian is the default when Mizon has no saved language choice.

The service unsubscribes and cancels its worker when the main window closes.
The SDK COM factory has process lifetime and no public unregister API; Windows
releases it on process exit. The runtime proxy is not `IClosable`.

## Validation

Run `WindowsSearch/Mizon.Windows.Activation.Tests` for routes, registration metadata,
icon payloads and all account/language availability combinations. Build the app as
Debug x64, then deploy through Visual Studio (the SDK needs target Windows 26100).
Use a packaged launch, not a bare EXE launch without package identity.

In the App Actions Testing Playground, query available actions and run a page
action; change Mizon's language or sign in/out, then refresh that query. The existing
`AppLog` records COM registration, synchronization and invocation without tokens.
Runtime failure is logged once per requested update and does not block the UI.

Sources:
- https://learn.microsoft.com/windows/ai/app-actions/actions-iactionprovider-manual
- https://learn.microsoft.com/windows/ai/app-actions/actions-availability
- https://learn.microsoft.com/windows/ai/app-actions/actions-consume
