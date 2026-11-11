# Particle checks

`dotnet run --project Mcp/Mizon.Windows.Particle.Tests -c Release`

Links the actual local particle physics/storage code. Exercises multi-axis
bounces, range semantics, bounded invalid/extreme layouts, 600 resize cycles,
scene release/reload and allocation-free steady simulation. Uses no WinUI
runtime or new packages and can also publish as NativeAOT.

This is a control regression fixture stored beside the app's existing headless
checks, not a new MCP endpoint or authorization test. The owning VPN feature
adds and verifies its own scoped MCP surface separately.
