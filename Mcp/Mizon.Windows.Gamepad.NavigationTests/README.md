# Authentication navigator checks

```powershell
dotnet run --project Mcp/Mizon.Windows.Gamepad.NavigationTests -c Release
```

This suite source-links the actual `Common/AuthGamepadNavigation.cs` and shared
controller action contract. The minimal XAML, keyboard and input-session doubles
hold observable visual-tree/focus state and record calls; navigation, scope,
availability, modal ownership and lifecycle decisions all come from production
code. It tests hidden/disabled ancestors, template-part exclusion, field wrapping,
keyboard and help focus boundaries, repeated explicit help states, outside-page
and modal input refusal, no MCP activation/submission, explicit feedback preferences,
cancellation and cleanup.

For isolated worktree integration, `-p:AuthGamepadSourceRoot=<repository-root>`
selects a sibling worktree's production sources without copying or editing them.
The normal default is this repository. No Windows UI, native popup, real keyboard,
controller hardware, app consent or account is accessed. These tests cannot
establish visual layout, native focus event ordering, animations or UI performance.
