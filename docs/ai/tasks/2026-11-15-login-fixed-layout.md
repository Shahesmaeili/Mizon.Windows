# Fixed language picker and form-only scrolling

- Owner: Codex. Repository: Mizon.Windows. Branch: `codex/auth-layout`.
- Base: `70049c6cde3b31e810fa1b055e62f930c31ec00e`.
- Scope: LoginPage, its existing keyboard/controller focus entry points, the shared language picker style hook, and the navigator checks.

Login and registration share a fixed language picker in the upper-right corner.
The page reserves the current native caption height in XAML units and updates it
when the presenter or XAML root changes. The language picker keeps its native
ComboBox, localized options and automation label. Welcome and Settings use the
same default appearance as before.

The form has the only scroll viewport on this page. The showcase remains centered
in its column on wide layouts. Compact layouts show the centered wordmark outside
the viewport. The form centers when it fits and can scroll to reveal every field
otherwise. Mode changes still reset the form scroll position.

The existing authentication MCP and Xbox controller navigation boundaries still
apply. No schema or permission changes are required. The navigator check includes
the form and the language picker in different branches and exercises focus in
both directions, selection and dismissal without a form action.

Validation: Visual Studio MSBuild, Debug x64 (.NET 11), passed. The headless
suites passed 36 production-linked navigator assertions and 46 authentication
MCP adapter assertions. Live resize, pixel placement, Narrator and physical
controller behavior were not checked.
