# Mizon: shared AI development workflow

Policy revision: **2026-11-10.2**. Applies to Codex, Claude, Copilot and human contributors.
The user's current request takes precedence. Read the repository's AGENTS.md first.

## Read before editing

1. AGENTS.md, this guide, docs/ai/PROJECT.md and docs/ai/HANDOFF.md.
2. Existing task notes in docs/ai/tasks/ and the current branch's recent commits.
3. The actual source and relevant project-specific guides. Conversation history is not source of truth.
4. The Windows repository's Mcp/DEVELOPMENT.md for any user-facing behavior change.

Do not infer implementation or test success from a plan, a screenshot, an old handoff,
or another assistant's claim. Verify the current branch. Report unavailable checks.

## Repository boundaries

| GitHub repository | Scope | Conventional checkout |
| --- | --- | --- |
| Shahesmaeili/Mizon.Database | EF entities, mappings, migrations, model checks | Mizon.Database/ |
| Shahesmaeili/Mizon.Server.API | API host, Logic operations, Server abstractions, analysis/import tools | Mizon.Server.API/ |
| Shahesmaeili/Mizon.API.DotNet | .NET transport and typed SDK | Mizon.API/ |
| Shahesmaeili/Mizon.Windows | WinUI app, ExplorerCommands, Palettes, MCP, taskbar companions | Mizon.Windows/Mizon.Windows/ |

The .NET SDK repository was renamed remotely; its local folder remains Mizon.API.
Do not create a second source copy based on a different remote name.
Apple/Swift repositories exist separately; this checkout does not contain or validate their source.

Keep sibling folder names when using existing relative ProjectReferences. The server
solution currently also contains machine-specific absolute Windows/SDK paths.
Inspect solution paths on another machine rather than creating duplicate projects
or silently rewriting a collaborator's Visual Studio setup.

## Commit and push is part of completion

Standing user instruction, recorded 2026-11-10: **after each completed development
task, commit its changes with a precise message and push them to GitHub**. Do not ask
again for routine commit/push authorization. A later explicit user instruction overrides this.

- Review the diff and run checks appropriate to the actual behavior changed.
- Update the relevant documentation and the task's handoff note.
- Stage explicit task-owned paths/hunks. Inspect the staged diff before committing.
- Commit and push every affected repository, in dependency order when applicable:
  Database, Server/SDK, then Windows. Record cross-repository dependencies.
- Verify the pushed branch's remote SHA matches the local commit. Report repository,
  branch, commit, test results and any unfinished or blocked work.
- If no files changed, do not manufacture an empty commit.
- If authentication, branch protection, conflicts or a failed check blocks publishing,
  preserve the work, diagnose the concrete failure and report it. Never claim a push succeeded.
- Do not force-push, rewrite shared history, bypass protections or delete another
  contributor's changes to make a push work.
- A push is not authorization to deploy production, change Windows registration,
  run database migrations against live data, create real accounts or approve MCP access.

The 2026-11-10 initial synchronization explicitly includes all reviewed accumulated
project source. That one-time broad snapshot is NOT permission to include another
agent's unfinished changes in future task commits.

## Concurrent work: isolation and ownership

Markdown does not synchronize running AI sessions or provide a lock.
Git branches/worktrees and explicit ownership prevent simultaneous edits.

- Give each concurrent task a separate worktree and branch. Codex branches normally
  use codex/<short-task>; other agents use their own identifiable prefix.
- Before editing, fetch the remote and inspect status, branch, HEAD and divergence.
  Never switch/reset/pull/rebase a shared dirty checkout while another agent uses it.
- In a new worktree, start from the agreed integration base, usually origin/main.
  Do not copy the .git directory or share one staging index between agents.
- Record owner, task ID, base SHA, repositories and owned files in a task note.
  Each agent owns a separate note; avoid a shared append-only "current task" file.
- Announce/coordinate ownership through the available team channel. If the same
  file/contract must change, assign one editor or integrate the changes sequentially.
- Do not edit another task's files, unstage its changes, discard its work, or stash
  everything as a shortcut. Read shared changes before depending on them.
- Use a single integration owner for cross-repository contract/schema changes.
  Review conflicting API/DTO/JSON/MCP edits together before merging.
- Keep task notes and branches pushed when ready for handoff. An agent on another
  machine must fetch to see them; there is no automatic memory sharing.

Example for a clean, separate Windows checkout (adapt paths, do not paste blindly):

~~~powershell
git fetch origin
git worktree add -b codex/taskbar-improvement ../Mizon.Windows-taskbar origin/main
~~~

Build configurations and relative sibling dependencies must also be resolved in
that worktree. Never run simultaneous builds that write the same bin/obj directory.

## Commit messages

Use a concrete English subject, normally:

~~~text
fix(windows): refresh taskbar pin visibility after external unpin

Read the current Windows pin state whenever the profile menu opens.
Keep the native confirmation and release menu subscriptions on close.

Validation: Debug x64 build; isolated pin lifecycle and MCP permission checks.
Limits: native confirmation UI was not automated.
Task: 2026-11-10-taskbar-pin
~~~

Use feat/fix/perf/refactor/docs/test/build as appropriate. Mention the actual final
behavior, important compatibility decisions and meaningful validation. Avoid
"update", "changes", "fix stuff", invented performance claims or conversational history.
For a broad initial snapshot, explicitly describe its multiple areas rather than
pretending it is one small isolated feature.

Use a temporary UTF-8 message file and git commit -F for multiline text. Preserve
literal text/newlines. Do not interpolate secrets or arbitrary text into shell code.

## Shared implementation conventions

- Preserve existing naming and layering. View models end in ViewModel.
- Business behavior and runtime writes belong in Mizon.Server.Logic/Operations;
  routes belong in Mizon.Server.API; EF structures belong in Mizon.Database.
- Database type catalogs are lookup tables, following MusicGenreType conventions.
  Do not replace them with persisted enums as a shortcut.
- Do not invent SQL business logic, historical data/backfills or migrations that
  mutate user data without task authorization. Use code-first migrations for schema.
- Project only needed columns. The user prefers readable, bounded split queries;
  use separate Where calls for AND predicates. Never parallelize one DbContext.
- Preserve IAsyncEnumerable streaming, cancellation, bounded work/backpressure and
  generated JSON metadata through database, API, SDK and consumers.
- Target .NET 11 for current development and check NativeAOT compatibility explicitly. Avoid reflection
  fallback and broad warning suppression. IsAotCompatible is not proof of a full
  native application publish; EF query precompilation remains separately constrained.
  .NET 10 is retained only as a measured comparison baseline, not a production target.
- Reuse the existing download/cache and playback ownership services. Do not create
  duplicate audio players, transport sessions or uncontrolled background loops.
- Preserve accepted designs/animations and functionality unless the task changes them.
  Support localization, RTL/Persian fonts, Narrator, responsive layout and cleanup.
- Dispose event subscriptions, timers, media, animations, buffers and native handles.
  Cancel superseded requests and prevent late callbacks from updating a closed page.

## MCP is a scoped product surface

Follow the Windows Mcp/DEVELOPMENT.md rather than recreating a parallel API.
Actual coverage currently includes authentication pages, appearance, declared
navigation and taskbar pinning; it does NOT expose every page's business data/actions.

New user-facing behavior needs meaningful typed MCP state/actions as appropriate.
New domains require separate user-visible permission groups, default denied.
Existing authentication grants must not authorize music/settings/taskbar domains.
Reuse the same business/service flow as UI. Keep source-generated schemas,
cancellation, sanitized errors, revocation and resource lifetime tests.

Never approve access from a tool/test, bypass in-app consent, expose arbitrary SQL,
shell/reflection/URL invocation, or submit real account forms merely for a green test.
Synthetic protocol/adapter checks do not prove visual behavior.

## Verification and environment

- Use the commands in each PROJECT.md and existing test README.
- Start with the smallest meaningful checks. Broaden only for changed behavior,
  failures or an unresolved concern; do not repeatedly rebuild unchanged code.
- Record exact configuration/platform, command, result and skipped cases.
- Do not run Windows UI automation unless the user explicitly requests it.
- Do not stop/restart a user's Visual Studio session, Explorer, Search or SQL service
  merely to run documentation/publication checks.
- Use synthetic data for writes unless real writes are explicitly part of the task.
- Keep diagnostics outside source or in ignored artifacts. Never commit generated
  obj/bin/AppPackages, logs, database dumps, downloaded caches or credentials.
- Do not silently replace existing application secrets/configuration. Prior explicit
  consent to publish one known development JWT value is not permission to publish
  new credentials, MCP pairing proofs, certificates/private keys or access tokens.
- Review dependency/security warnings honestly; do not claim they were fixed by
  a successful build or by enabling NativeAOT analysis.

## Handoff and cross-repository changes

Create docs/ai/tasks/<date>-<short-task>.md using TASK_TEMPLATE.md.
Keep it factual and concise: requirement, ownership, files, decisions, checks,
remaining risks and the next action. No secrets, raw account payloads or chat dumps.

For coordinated schema/API/SDK/UI changes:
1. Write compatible contracts and schema decisions.
2. Update every affected layer and source-generated serialization metadata.
3. Run relevant component and integration checks.
4. Push dependency commits and record exact hashes in the dependent task note.
5. Integrate branches with normal merges/fast-forwards, never overwrite history.
6. Revalidate the combined result before calling the feature complete.

## Keeping these instructions consistent

The authoritative policy copy is docs/ai/COLLABORATION.md in Mizon.Server.API.
An identical portable copy lives in each of the four repositories for offline agents.
When this shared policy changes, update its revision and synchronize all copies
in the same coordinated task. Repository-specific facts belong in PROJECT.md;
current implementation/verification state belongs in HANDOFF.md and task notes.
CLAUDE.md and Copilot instructions are entrypoints, not competing rule sets.
