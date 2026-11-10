# Windows headless runtime benchmarks

This console runner measures two production paths without opening Mizon or using
the user's account, credential store, named pipes, network or Windows shell:

- Deep-link parsing and canonical URI generation: four representative declared
  routes, using the actual `AppActivationRoute` and `AppDeepLinkCatalog` source.
- MCP JSON/frame round-trip: the actual source-generated response metadata and
  `McpPipeProtocol` over a reusable in-memory stream with synthetic state.

Each scenario warms up five batches, then records fifteen samples. Results include
all raw samples, median nanoseconds and allocated bytes per operation, runtime,
architecture, GC mode and a consumed-result checksum. Allocation is measured with
`GC.GetTotalAllocatedBytes`; it is not process working set or native/GPU memory.
The stream is intentionally in memory: this does not measure pipe IPC latency.

```powershell
dotnet build Mcp/Mizon.Windows.PerformanceBenchmarks -c Release -o <candidate-output>
& <candidate-output>/Mizon.Windows.PerformanceBenchmarks.exe --output <candidate.json>
```

For a framework comparison, preserve the baseline executable and its complete
output directory before retargeting. Run baseline and candidate executables in
separate processes, serially, without simultaneous builds or benchmarks. Alternate
their order over multiple process runs. Compare the same code, fixtures,
configuration, architecture and measurement method. Do not infer faster UI,
audio/rendering performance, application startup or reduced application RAM from
these headless results.

The .NET 10 baseline for the 2026-11-10 migration was saved outside the repository
and rebuilt with installed SDK 10.0.400-preview.0.26322.102. Its isolated project
targets .NET 10 while linking the same unchanged production files and preserving
the contracts assembly boundary. The candidate uses SDK 11.0.100. Baseline
execution selects the installed .NET 10 runtime; candidate execution selects
.NET 11. Exact runtime versions are recorded in every result rather than inferred
from the SDK version. This compares the installed SDK/runtime combinations, not
an isolated compiler or runtime component in isolation.
