# Three floating editorial portraits above music-home stories

- Status: complete (live visual verification remains unperformed)
- Owner: Codex root; isolated motion helper supplied by orbit_motion_review
- Date: 2026-11-11
- Repository/branch: Mizon.Windows, codex/music-story-orbits; integrate into main
- Base: ff928c94669c215aa141cf13e94c7bccddce969d
- Owned: UserControls/StoryOrbitCard*, StoryOrbitShowcase*, Pages/MusicPage.xaml,
  Strings/{fa-IR,en-US}/MusicHome.resw, the related asset/task/handoff documentation.
- Related helper: [motion ownership note](2026-11-11-story-orbit-motion.md).

## Request and implementation

The supplied Dribbble image shows three organic portrait collages, rather than
the earlier NFT sculptures. Add a similar animated composition immediately above
MusicStories in the existing music home feed. Preserve the surrounding page.

The three native XAML cards have different Bezier silhouettes, independent fine
contours, accent shadows, edition numbers, icon badges and localized typography.
They reuse the app's existing original fictional performer cutouts. The reference
image is not shipped. No new packages, media service, web renderer or API are used.

Composition moves eight layers per card on separate slow periodic paths. Clipping
and outlines share the same control points. Images decode to 560 pixels wide,
not the source's full size. Resizing scales a fixed design space; narrow windows
use a hidden-scrollbar horizontal strip with touch and wheel scrolling. Content
and event subscriptions are not rebuilt for each resize.

The helper pauses out of view, on minimization/hidden ancestors and obeys the
Windows animation preference. True unload stops animation clocks, detaches
callbacks, releases owned clip/shadow objects and drops decoded image references.
XAML-owned visuals, alpha masks and active animation controllers are not closed.
Native composition failures leave static bounded artwork rather than crashing
navigation. Colors behind titles follow the current app theme. Persian titles
use IranSans and RTL; English and language switching reuse LocalizationService.

This strip is decorative, not a set of unimplemented music/profile buttons.
Its art is excluded from the Narrator control tree. Existing stories, player,
artists and music-home actions remain the same. Per Mcp/DEVELOPMENT.md, reuse the
music-home MCP contract and integration checks; do not invent decorative tools.

## Validation

| Check | Result | Limits |
| --- | --- | --- |
| Visual Studio MSBuild, Debug x64, .NET 11 | Passed | Existing unrelated warnings remain; build is not deployment or full NativeAOT publication |
| Existing MCP SDK/protocol suite, Release --isolated | 47/47 groups passed | Synthetic; no app launch or real consent/account writes |
| Existing MCP production-adapter/access suite, Release | 276 assertions passed | Includes scopes, cancellation, revocation and lifecycle; does not validate rendering |
| Source review + git diff --check | Passed | Does not prove native rendering or smoothness |

No Windows UI automation was requested or run. Live portrait clipping, light/dark
appearance, horizontal scrolling, DPI changes, animation smoothness and
minimize/reduced-motion transitions still need human visual verification. No
claim of whole-app NativeAOT publication or measured frame rate/memory is made.

## Publication

Task-owned changes are integrated and pushed to main. Identify the delivery
commit with git log for this file; no unrelated user or agent files are staged.
