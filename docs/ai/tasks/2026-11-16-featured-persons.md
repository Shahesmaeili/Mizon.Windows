# Music home featured artists

- Owner: Codex, branch `codex/featured-persons`, base `75e4aec`.
- Database `d1cfd5f`, server `27d03eb`, SDK `7c19b39`.
- Owned paths: the music home spotlight and artist rail, typed view models and music home MCP contracts.

Both artist presentations share a single `FeaturedPersons` result. The server
determines identity, order, follow state, music counts, latest tracks and listening
aggregates. Cards ask for media on realization and the spotlight loads one
model at a time from the shared cache. Selection and page unload cancel obsolete
loads. People without a model display a portrait or initials. Artist profile and
track buttons navigate to the existing pages. Follow sends the desired state
through the existing API and both cards use the returned canonical count.

Persian and English present the same data. The weekly metric shows recorded
plays over seven UTC days. Screenshot-only demo data in other discovery areas
is separate. Music MCP reports the stable public Person and track IDs and
retains the existing independent read, control and submit scopes. Native
buttons preserve the keyboard and controller focus path, and the model keeps
its existing controller rotation and reset operation.

Verified SQL/HTTP/SDK and named-pipe MCP access checks with synthetic consent.
Visual rendering, Narrator speech and physical controller input require a live
desktop and hardware check.
