# Taskbar profile activation waits for a verified app response

- Status: complete for the companion; main-app integration owned by the parent task.
- Owner: Codex `/root/music_home_widgets`.
- Date: 2026-11-11.
- Repository/branch: Mizon.Windows / `codex/taskbar-deep-link-results`.
- Base: `3b75ffcb0bcf508069b9b5d615031791ff94ca0e`.
- Owned: taskbar profile companion, its focused checks, `Services/TaskbarProfileBridge.cs`.
- Dependency: parent-owned shared `Services/AppDeepLinkResult.cs`, main activation,
  protocol manifest, result completion and MCP integration.

## Behavior

The profile button uses Windows `LaunchUriForResultsAsync` with `LauncherOptions`
initialized to its own process-verified HWND and the main app's package family.
Version/correlation input and strict source-generated result validation use the
shared result contract. An OS transport success without a valid result is failure.
No fire-and-forget fallback presents an unconfirmed activation as success.

One in-flight request controls a stationary ProgressRing around the photo, with
the same transparent exclusion gap around the existing presence badge. Photo
hover/click motion, native host width and the mood button remain unchanged.
Loading pauses while the native host is hidden and is released on success,
failure, a 60-second deadline, account/package change, sign-out, invalid snapshot,
island destruction or disposal. Late callbacks cannot update newer requests.
Localized tooltip/Narrator state does not expose native errors or returned text.

The bridge adds optional `packageFamilyName` to the existing version-one snapshot.
The main app must call `TaskbarProfileBridge.SetPackageFamilyName` at startup with
its actual `Package.Current.Id.FamilyName`. Legacy snapshots remain readable but
cannot invoke a result request without that target identity.

## Validation

- Companion `dotnet build ... -c Debug -p:Platform=x64`: passed; eight existing
  `CsWinRT1032` warnings remain in the unrelated layout checks.
- New `Mizon.Windows.TaskbarActivation.Tests`: 82 assertions passed, managed and
  published NativeAOT win-x64 execution. Uses fake native transport and manual time.
- Existing `Mizon.Windows.ProfilePresence.Tests`: 40 assertions passed, including
  package identity publication/refresh without losing account/presence.
- Existing `Mizon.Windows.Presence.Tests`: 50 assertions passed.
- No UI automation, native activation, installation, taskbar manipulation or live
  account/API calls. Ring rendering, real launch result delivery, foreground
  handoff, high-DPI appearance and Narrator speech require separate live checks.

Review follow-up: request ownership now clears in guaranteed finally cleanup;
throwing presentation observers and native cancellation callbacks cannot retain a
disposed CTS or escape the deadline timer. The async-void click boundary contains
remaining errors. Synthetic checks include notification, explicit cancellation,
deadline, successful-finally and disposal failures. Button padding is explicitly
zero; the custom template's full 36-DIP content area keeps the ring inside its
existing native hit region without moving the centered 32-DIP photo.

The result caller pattern follows Microsoft MAUI/Windows App SDK camera code:
`InitializeWithWindow.Initialize(options, ownedHwnd)` before launching. The
unusual taskbar-child HWND and actual Windows launch-for-results behavior have
not been runtime-tested here. Upstream WindowsAppSDK issue #1938 documents older
OS behavior returning success with no result, which is explicitly rejected.

## Publication

Scoped commit for parent cherry-pick. The shared contract source was temporarily
copied solely for local compilation and is deliberately excluded from this commit.
Root integration owns combined verification and the requested GitHub push.
