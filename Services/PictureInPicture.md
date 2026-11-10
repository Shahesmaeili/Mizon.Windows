# Picture in picture

`PictureInPictureService` owns one native WinUI `CompactOverlay` window. Resolve it from the app service container on the main UI dispatcher. The music detail page's existing PiP button calls `ShowMusicAsync()`.

## Shared playback

- Music borrows `IAudioPlayerService`, including the existing AudioGraph, stem mixer, output device, SMTC and playlist queue. Opening PiP does not automatically play a song.
- Metadata follows the playing song, rather than changing when another detail page is merely browsed. The play button can explicitly start the song originally opened in PiP.
- Seeking commits after a drag, or after a short keyboard debounce. A pending seek is discarded when its media/session changes.
- Closing the floating window leaves playback running. Closing the main window or leaving the authenticated home closes PiP and releases its subscriptions, timers and video surface.

## Music videos, films and episodes

The same window supports an existing `MediaPlayerElement`. No second player, download or playback clock is created. Keep ownership of the `MediaPlayer` in the page/service; its original element is temporarily detached while PiP renders it.

```csharp
await pictureInPicture.ShowVideoAsync(
    VideoPlayerElement,
    title: episodeTitle,
    subtitle: seriesTitle,
    artworkPath: cachedCoverPath,
    returnToApp: OpenCurrentEpisode,
    next: PlayNextEpisodeAsync,
    previous: PlayPreviousEpisodeAsync);
```

For next/previous, change the source on the **same retained MediaPlayer**, then update labels without activating the floating window:

```csharp
pictureInPicture.UpdateVideoMetadata(
    VideoPlayerElement, episodeTitle, seriesTitle, cachedCoverPath);
```

Close PiP **before** disposing the owning video page/player. Ordinary PiP closure restores that player to its original element without stopping playback. If the page assigned a replacement player in the meantime, the old player is not restored over it. Explicitly switching PiP to another media session pauses the outgoing video; opening video PiP also pauses active music to avoid duplicate audio.

The film/episode/music-video detail pages are not implemented in the current app. This shared video entry point is ready for those pages; no placeholder video backend was added.

## Interaction and lifetime

- Native Small/Medium/Large sizes, content extended into the title bar and the Windows-owned close button. Caption insets account for DPI; no duplicate close button is drawn.
- A compact transport dock and framed artwork use the existing Mizon visual language. The cover's backend `PrimaryAccentColor` supplies tint and glow, with a readable fallback; no additional image analysis or API is needed.
- The window uses the same `AcrylicSystemBackdrop(DesktopAcrylicKind.Default)` as `MainWindow`. Its XAML root stays transparent, and the video contrast scrim is only visible for video. Closing PiP disconnects the backdrop.
- Space toggles playback, Ctrl+Left/Right seeks ten seconds, Escape closes PiP.
- Video controls fade after inactivity; mouse/keyboard input reveals them. Keyboard focus and open flyouts keep controls visible.
- Persian accessibility names and existing IRANSans/Fluent icons; motion follows Windows animation settings.
- Compiled XAML and typed C# APIs, with no runtime XAML parsing, reflection-based binding or new NuGet dependency.
