# Artist model angle with mixed API build versions

- Owner: Codex
- Branch: `codex/fix-person-model-live`, base `84900bd`
- Owns: Windows music-to-person navigation and profile model angle resolution.

The active Debug API host was loading an older copy of its Logic assembly.
The Logic project's own newer output defined `PersonProfileResponse.ModelYawInDegrees`;
the assembly in the running API host's directory did not. The music catalog
already exposed the value in both builds.

The profile uses the explicit angle from its own API response when available.
Music Home passes the selected model's ID and angle alongside its existing
navigation, including the scoped MCP open action. When the profile response
omits the angle, a model-ID match reuses that context without another request.
For direct profile routes with no angle, the existing music catalog supplies
a fallback only for a matching person and model. If the catalog cannot supply
a match, the control uses its normal default. Leaving the page cancels the
lookup, and the native model load diagnostic records the effective angle.

Debug x64, 311 isolated MCP assertions, and 301 controller input assertions
passed. The isolated regression probe used the production profile view model,
SDK, WinUI composition scene, and Moein GLB with an HTTP response shaped like
the old server. It verified catalog failure, mismatch and cancellation, both
navigation sources, and the native angle after reset. The active Visual Studio
session was left running and the new packaged app was not opened in it.
