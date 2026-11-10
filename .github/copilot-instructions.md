# Mizon Windows development

Follow [AGENTS.md](../AGENTS.md), the [shared collaboration workflow](../docs/ai/COLLABORATION.md), the [project map](../docs/ai/PROJECT.md), and the [current handoff](../docs/ai/HANDOFF.md).

Preserve WinUI design, localization, accessibility, disposal and existing behavior. Changed behavior needs scoped MCP support and meaningful checks under [Mcp/DEVELOPMENT.md](../Mcp/DEVELOPMENT.md); visual-only changes reuse existing contracts.

Use an isolated worktree when other agents are working. Do not stage unrelated edits or overwrite another agent's files. Completed tasks require an accurate commit and push, plus a handoff with verification and limitations. Never automate in-app permission approval or Windows consent dialogs.
