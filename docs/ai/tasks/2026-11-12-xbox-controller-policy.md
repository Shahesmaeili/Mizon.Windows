# 2026-11-12-xbox-controller-policy

- Status: complete; main-branch integration is coordinated separately.
- Owner/agent: Codex gamepad_standing_docs; root owns feature integration.
- Date: 2026-11-12
- Repository and branch: Mizon.Windows, codex/xbox-controller-policy
- Base commit: a6bce9a3e9320e5c5a6ddbe301e7fcaf423b1c04
- Related repositories/tasks: coordinated Server, Database, .NET SDK and Windows policy synchronization; separate Windows authentication controller implementation.
- Owned files: AGENTS.md, docs/ai/COLLABORATION.md and this task note. No shared HANDOFF.md or product files.

## Request and acceptance criteria

Record the user's standing requirement that future new or changed interactive
Mizon features/pages also support Xbox controllers. Synchronize revision
2026-11-12.1 of the authoritative Server collaboration guide identically into all
four repositories and make the requirement discoverable from each AGENTS.md.

## Implementation and decisions

The policy requires meaningful context mappings, directional/visible/restored
focus, scrolling, default/back/menu/dialog behavior, usable controller text
entry, localized accessible hints, RTL/Narrator support and reduced motion.
Input ownership follows the active foreground surface, with held/repeat state
reset, disconnect handling and disposal. Native UI and shared business operations
remain the implementation path; existing MCP consent and feature scopes remain.

Only meaningful controls are required in a context; Xbox/Share actions remain
reserved for the system. Controller coverage grows with the feature being worked
on. This policy is not evidence that all existing pages support controllers.
Automated input/state/lifecycle checks and actual hardware/visual verification
must be reported separately. Windows UI automation still requires an explicit
user request.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| git diff --check in each isolated policy worktree | Passed | Documentation formatting only |
| SHA-256 comparison of all four collaboration guides | Identical: 79007DA37A12CDF67141323B440CBAE202A422D1179F5F565E800928753ED0F8 | Verifies synchronized source, not runtime controller behavior |
| Diff review against the recorded bases | Only policy revision/section, AGENTS.md entry and this task note | Existing implementation, accessibility, MCP and publication requirements preserved |

No product build, database operation, Windows UI automation, physical-controller
test, consent interaction or application restart was performed for this
documentation task. The separate feature task must record its own verification.

## Handoff

The root integration owner should review and normally merge this repository's
policy branch with the coordinated feature work, preserving later changes.
Do not infer authentication implementation or existing-page controller coverage
from this documentation commit. No runtime API/SDK/schema dependency is added.

## Publication

Branch: codex/xbox-controller-policy in all four repositories. Commit/push and
remote-SHA verification are reported to the integration owner; no main-branch
merge is performed by this subtask. Use git log for this file to identify its
containing commit without a self-referential hash.
