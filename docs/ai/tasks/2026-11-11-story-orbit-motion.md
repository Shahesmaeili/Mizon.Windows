# 2026-11-11 Story collage compositor motion

- Status: complete (helper implementation; integration validation pending)
- Owner/agent: Codex orbit_motion_review
- Date: 2026-11-11
- Repository and branch: Mizon.Windows, `codex/story-orbit-motion`
- Base commit: `ff928c9`
- Related repositories/tasks: parent `codex/music-story-orbits` collage integration
- Owned files/areas: `Common/StoryOrbitMotion.cs` and this task note only

## Request and acceptance criteria

Provide reusable, independent slow floating motion for the three decorative
portrait collages above music-home stories. Motion must have smooth repeated
paths, pause offscreen/hidden/minimized, honor reduced motion, and release all
owned resources and observers on departure without per-frame C# work.

## Implementation and decisions

`StoryOrbitMotion(owner)` accepts `Add(element, excursion, durationSeconds,
rotationDegrees)` calls followed by `Start()` from Loaded. Dispose on true
Unloaded; the helper also observes unload and window close defensively.
Construct a fresh helper for the next load. Elements retain their original
translation, rotation and center when motion is stopped.

Independent compositor phase clocks drive sinusoidal translation and optional
tilt. Horizontal, vertical and tilt periods differ; all are closed smooth loops.
Animation controllers pause clocks and preserve their phases offscreen or when
the host is hidden/minimized. Reduced motion stops clocks and restores the rest
pose. Initial viewport checks cover the root and scrolling ancestors before the
first effective-viewport callback. Ancestor visibility, root/window events and
UISettings subscriptions are scoped to the helper lifetime; queued settings work
checks disposal. XAML-owned visuals and active controllers are never disposed.
Late compositor failures disable and release the helper instead of escaping a
visibility/settings callback; cleanup proceeds across the other registered targets.

The feature remains decorative. Existing music-home MCP contracts and permission,
failure/cancellation and lifetime suites are reused; no visual-state tool is added.
The expression math follows Microsoft's compositor property-set relation-animation
pattern: https://learn.microsoft.com/en-us/windows/apps/develop/composition/relation-animations

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| Source/diff review, independent read-only review, `git diff --cached --check` | Passed | No runtime claim |
| Build, existing isolated MCP suites | Not run by this agent | Parent owns combined build/verification |
| Windows UI / animation / minimize / reduced motion | Not run | No Windows UI automation requested |

## Handoff

Parent integrates the helper with the collage controls, builds the combined
change and runs relevant existing MCP suites. Visual smoothness, responsiveness,
offscreen/minimize resume and reduced-motion behavior remain separately unverified.

## Publication

Helper commit is prepared on the isolated branch for parent cherry-pick.
No push by this agent, as explicitly requested by the integration owner.
