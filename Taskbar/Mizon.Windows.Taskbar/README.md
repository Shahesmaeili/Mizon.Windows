# Mizon taskbar profile companion

An independent .NET 11 / WinUI 3 process. It initializes a hidden, process-owned native host for `DesktopWindowXamlSource`, then converts that host to `WS_CHILD` and reparents it to `Shell_TrayWnd` before showing it. It does not create a floating overlay, inject into Explorer, or register automatic startup.

The main app launches a copy of the complete build output from an immutable runtime directory. Closing the main app does not close the companion. On logout the snapshot hides and disposes the taskbar island; the process waits for the next authenticated snapshot. Explorer replacement and taskbar DPI/geometry changes recreate or resize only the companion's own child.

## IPC

The main app atomically replaces `%USERPROFILE%\.mizon\TaskbarProfile\profile.json`:

```json
{
  "version": 1,
  "revision": 1,
  "isAuthenticated": true,
  "clientPublicId": null,
  "displayName": "Mizon account",
  "imagePath": "C:\\existing\\cached-profile.jpg",
  "moodPublicId": "01900000-0000-7000-8000-000000000001",
  "moodEmoji": "😊",
  "moodTitle": "Happy",
  "moodAnimationPath": "C:\\existing\\mood.tgs.json",
  "moodEffectPath": "C:\\existing\\effect.tgs.json"
}
```

No token or API credential is transferred. The companion decodes the existing local cached file and uses initials if no photo is available. It observes atomic file replacement using a debounced watcher plus a periodic fallback. Click opens `mizon://client/{id}` when the ID is known, otherwise `mizon://music` opens the main app.

Mood fields are optional and use the same version-1 snapshot. The main app downloads and expands the selected animation through its existing media cache, then publishes local paths. The companion never opens another API session or downloads the mood catalog. Profile updates retain the current mood; changing accounts or logging out clears it. Closing the main app leaves the last authenticated snapshot available to the companion.

The mood button sits beside the profile and loops only while visible, respecting Windows' animation preference. Its click effect is prepared once and played in an unconstrained XAML popup anchored to the emoji's center. Only one effect plays at a time; another click restarts it. The popup scene is released when closed, while the parsed animation is retained for the next click. Changing mood, hiding the host, signing out and disposing the island cancel active playback. Placement includes the entire profile-and-mood width, including at higher DPI.

The decorative popup is also transparent to native mouse input. Its native bridge is identified only within this process/UI thread, by its exact WinUI class and rendered bounds; layered/transparent/no-activate flags preserve the colored composition while allowing clicks underneath. The scope removes its added flags when the popup closes. No other application's window is changed. This small interop boundary must be rechecked when upgrading WinUI, because Popup does not expose its own HWND publicly.

The log is `%USERPROFILE%\.mizon\TaskbarProfile\Taskbar.log`.

## Build and process lifecycle

```powershell
dotnet build .\Mizon.Windows.Taskbar.csproj -c Release -p:Platform=x64
.\bin\x64\Release\net11.0-windows10.0.22621.0\win-x64\Mizon.Windows.Taskbar.exe
```

An ordinary duplicate launch exits. `--replace` signals a per-user named event, waits up to eight seconds for the previous companion to release its mutex after cleanup, then starts the new version. It never kills Explorer or the main app. All watchers, timers, event subscriptions, image streams, cursors, the XAML island, and the owned HWND are released during shutdown.

Isolated lifecycle checks can use an explicit state directory and a bounded lifetime:

```powershell
.\Mizon.Windows.Taskbar.exe --diagnostic-state-directory "D:\\diagnostics\\taskbar" --diagnostic-duration-seconds 15
```

Diagnostic mode uses a separate mutex/event identity and reads only that directory's snapshot. It does not replace the real logged-in snapshot. The attached HWND is still a real taskbar child so this mode is suitable for validating the actual WinUI hosting path.

Windows does not provide an officially supported public layout slot for arbitrary third-party children in the Windows 11 taskbar. This implementation keeps the requested real-child behavior and logs attachment failure instead of falling back to a floating window. Future Windows shell layout changes may require updating its placement logic.

Placement reads cached native UI Automation bounds on a worker thread and includes other applications' visible direct taskbar child HWNDs. It chooses a verified empty interval for the complete host width and height, retains a safe previous position, and hides the child when the layout cannot be verified or no gap fits. Out-of-context shell location events trigger updates, with a two-second fallback. This is collision avoidance, not an internal taskbar reservation; unrelated applications do not receive a reservation contract from Windows.

`--test-layout` runs deterministic geometry checks without changing the shell. `--diagnostic-state-directory "D:\diagnostics\taskbar" --inspect-layout` records the current read-only UI Automation/foreign-HWND layout in the diagnostic log.
