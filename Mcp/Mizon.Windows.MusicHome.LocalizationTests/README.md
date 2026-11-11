# Music home localization checks

This headless executable links production weekly-rhythm, news, playlist and
following-activity models, the shared following response DTO and both widget
resource files. It tests translation/format parity, Persian-English round trips,
meaningful labels and Narrator text, preserved IDs/content/selection identities,
and no thumbnail recreation on language changes. WinUI image primitives and MRT
resource lookup are replaced at the platform boundary; this does not prove final
layout, actual MRT packaging, animation or screen-reader output.

From the Windows repository root with the conventional sibling SDK checkout:

```powershell
dotnet run --project Mcp/Mizon.Windows.MusicHome.LocalizationTests -c Release
```

For an isolated worktree, specify the existing SDK source root using
`-p:MizonApiSourceRoot=<absolute Mizon.API directory>`. NativeAOT can be checked
with `dotnet publish ... -c Release -r win-x64 -p:PublishAot=true` and running the
resulting executable. This verifies the linked presentation logic, not the whole
WinUI app. No real application/connection is launched, no consent is granted and
no database or API request is performed.
