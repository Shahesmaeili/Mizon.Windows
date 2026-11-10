# Mizon live Windows Search integration

The active integration is the .NET 11 **Palettes provider** for the new Windows taskbar Search. It streams real music and public profile results from the existing Mizon API, supplies native previews and context menus, and opens the selected record through the application's existing single-instance deep links.

This folder contains these active projects:

| Project | Purpose |
| --- | --- |
| [Mizon.Windows.PaletteProvider](Mizon.Windows.PaletteProvider/README.md) | Packaged out-of-process native search extension, live API session, authentication handoff and real actions. |
| [Mizon.Windows.PaletteProvider.Tests](Mizon.Windows.PaletteProvider.Tests/README.md) | Native ownership, API, streaming, parser and registered-server tests. |
| Mizon.Windows.PaletteProvider.RendererTests | Installed WinUI renderer tests for compact card layout under the Search height limit. |
| Mizon.Windows.Activation.Tests | Current music/profile deep-link routing tests. |
| Mizon.MediaDownloader.Tests | Shared media downloader and cache behavior tests. |

See the provider [README](Mizon.Windows.PaletteProvider/README.md) for build/deployment commands, protected login handoff, installed version and verification status. [CAPABILITIES.md](Mizon.Windows.PaletteProvider/CAPABILITIES.md) records the verified native fields and the limits imposed by the Windows host.

The superseded protocol-indexer, native filter, catalogue-document installer and hardcoded-demo projects have been removed from the active source tree and solution. The current integration does not create per-song Windows Search catalogue files or desktop shortcuts. Search inside the Mizon application and its `mizon://` activation protocol remain independent and are retained.

Retirement was reversible: the old directories and excluded demo fixtures were moved to the ignored `obj/LegacySearchArchive/retired-20261105` directory after their paths were verified. They are not compiled, published or included in the solution. The separate system cleanup has also removed the old filter/protocol registrations, crawl scope, sample index rows, installed DLLs and generated catalogue/connector. Main app version1.0.0.2 no longer registers the old file association, while its mizon:// protocol remains.