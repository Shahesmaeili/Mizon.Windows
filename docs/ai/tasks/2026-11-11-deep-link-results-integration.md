# Deep-link responses and independent taskbar loading

- Owner: Codex root; agents: music_home_contracts, music_home_widgets, deep_link_lifecycle.
- Repository: Mizon.Windows; integration branch: `codex/deep-link-results`.
- Base: `3b75ffcb0bcf508069b9b5d615031791ff94ca0e`.
- Scope: native receiver/manifest, shared typed receipt, existing navigation and
  readiness, taskbar caller/loading lifetime, existing scoped MCP and docs.
- Separate agent branches owned contracts/MCP, taskbar, and pure lifecycle;
  root owns Program/App/manifest/page integration. No backend/DB changes.

## Implementation

`LaunchUriForResultsAsync` uses the same allowlisted URI routes, target PFN and
an owned caller HWND. The protocol remains optional so ordinary links still
work. Request metadata is copied from activation synchronously; the agile
`ProtocolForResultsOperation` is kept before the redirected process exits.
The response is a strict, source-generated, bounded JSON string in ValueSet.
It includes version, correlation UUID, success, stable code, sanitized message,
and optional typed destination metadata. No arbitrary data/credentials are sent.

The response dispatcher shares AppDeepLinkNavigation with MCP. Latest activation
wins, and stale results cannot overwrite new work. Default receiver deadline is
50 seconds; explicit downloads get five minutes. Sender waits at most 60 seconds
for profile/navigation. Receiver cannot observe all caller-side native teardown;
its own deadline and app/page cancellation bound remaining work.

Profile receipts wait for the existing profile/media request and verify the
active page/session. Comment navigation now returns its real found/presented
outcome. Story results wait on ImageOpened/MediaOpened or fail on error/closure.
Other content pages honestly report `opened`, not server data readiness. Explicit
play/download await the existing playback/download operation. No business flow,
authentication submission or permission is duplicated.

The taskbar ring is a sibling of the animated photo with the presence cutout;
it does not expand with hover and pauses while the host is hidden. Once-only
request ownership covers click repetition, native empty results, callback
exceptions, deadline, account change, island teardown and late replies.
Accessible busy/error text follows English/Persian snapshot language.

MCP `open_destination` returns the same typed receipt only with navigation read
permission. `get_navigation.pendingUri` also reflects native in-flight requests;
timeout/cancel/completion clears it before old work finishes unwinding. Neither
authentication grants nor navigation control alone expose additional metadata.
Playback/download remain outside MCP navigation authorization.

## Verification

- Full WinUI Debug x64 build with Visual Studio MSBuild: passed.
- Activation route/App Actions/optional protocol registration: 601 assertions.
- Pure result lifecycle/pending state: 51 assertions, managed and NativeAOT.
- Companion activation/callback/deadline/localization: 82 assertions, managed
  and NativeAOT; companion Debug x64 build passed.
- MCP production access adapter/synthetic consent: 227 assertions.
- MCP SDK/protocol/generated receipt: 44/44 groups, managed and NativeAOT.
- Existing publisher/presence helpers: 40 and 50 assertions.
- Diff whitespace and scoped source review: passed.

Agent native runs used isolated test executables; they do not establish full
WinUI application NativeAOT publishing. Logs stay in local diagnostics folders,
not Git. There were no real account submissions, self-approvals or API writes.

Not runtime-verified: deployed ProtocolForResults dispatch through AppInstance,
taskbar child-HWND launch/focus behavior, rendered ring/hover/DPI, Narrator speech,
and actual API-backed profile/story/comment readiness. Windows UI automation is
not authorized for this task; no running VS/app/Explorer was stopped or operated.
Normal Visual Studio MSIX Deploy is required to register ReturnResults and stage
the new companion payload. A successful build alone does not do that.

## API evidence

- [Official WinUI 3 receiver example](https://learn.microsoft.com/en-us/windows/ai/app-actions/actions-uri-launch).
- [ReturnResults and ValueSet contract](https://learn.microsoft.com/en-us/uwp/launch-resume/how-to-launch-an-app-for-results).
- [MAUI desktop caller tested packaged/unpackaged](https://github.com/dotnet/maui/pull/13220).
- [AppInstance redirect callback lifetime](https://github.com/microsoft/WindowsAppSDK/blob/main/dev/AppLifecycle/AppInstance.cpp).
- [Known older OS empty-result behavior](https://github.com/microsoft/WindowsAppSDK/issues/1938); empty results are rejected, never reported as success.

Root integrates reviewed commits into clean main and verifies the GitHub SHA.
No registry changes or deployment are implied by the publication step.
