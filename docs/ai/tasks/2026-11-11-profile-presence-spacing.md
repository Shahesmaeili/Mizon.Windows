# Profile badge clearance and stationary hover

- Owner: Codex root; independent read-only review by `music_home_widgets`.
- Date: 2026-11-11.
- Branch: `codex/profile-presence-spacing`; base `b72886ef73a96fb19465b14b09279b903af12c2c`.
- Owned files: `AuthActionButton`, `PresenceAvatar`/metrics, header XAML and taskbar
  profile host. Windows-only presentation change; no backend, SDK or consent changes.

The user confirmed presence updates work after rebuilding the server, then asked
for more clearance, a complete taskbar badge and a fixed badge while hovering.

## Implementation

- Transparent photo exclusion is now 2.5 logical pixels beyond the 9-pixel badge
  instead of 1.5 at the normal 32-pixel avatar size; larger avatars scale proportionally.
- The existing hover/press animation targets only PersonPicture. The unrounded
  photo host owns the stationary exclusion clip; the status badge is a sibling.
  PersonPicture still owns its circular crop, so hover can enlarge the photo.
- A reusable optional `AuthActionButton.AnimationTarget` keeps all other buttons
  on their existing content target. Target replacement resets the old scale;
  size subscriptions are detached on unload and restored once on load.
- The taskbar HWND's circular region was independently clipping the badge.
  Its native region now includes a fixed badge ellipse at the actual avatar
  position, including the mood button offset and DPI/antialias allowance. The
  inter-button gap remains excluded. Temporary HRGNs and failed composition
  allocations are released; successful SetWindowRgn ownership stays with Windows.

## Verification and limits

- Visual Studio MSBuild Debug x64 build of main app and taskbar payload passed.
- Existing avatar/lease suite: 50 assertions passed.
- Existing MCP access suite: 159 assertions passed using synthetic consent/state.
- Independent source review confirmed badge bounds, fixed animation ownership and
  native handle ownership. No new MCP tool or artificial visual state was added.
- No Windows UI automation, running app interruption or deployment performed.
  Final visible hover/crop and Narrator speech were not exercised. The build is
  not a whole-app NativeAOT publish; this patch adds no packages or reflection.

Publication is to `origin/main` after normal fast-forward integration. The commit
containing this note identifies the final source. Rebuild/restart the Windows app
to load its updated taskbar payload; no server rebuild is needed for this follow-up.
