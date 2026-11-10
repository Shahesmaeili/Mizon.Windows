# Activation and deep-link checks

This executable links the production `AppActivationRoute`, `AppDeepLinkCatalog`
and `AppActionCatalog` sources. It verifies existing protocol compatibility,
canonical generation/parsing for every supported destination, authentication
metadata, all settings sections, malformed URI rejection, query encoding,
comment/story/playlist IDs and packaged App Actions registration.

It does not launch Mizon, invoke a real account operation, alter protocol
registration, change Windows settings or access the network. MCP transport,
permission and live navigation checks belong to their corresponding MCP suites;
these tests do not substitute for them or for visual verification.

From the Windows app repository:

```powershell
dotnet run --project WindowsSearch/Mizon.Windows.Activation.Tests -c Release
dotnet publish WindowsSearch/Mizon.Windows.Activation.Tests -c Release -r win-x64 -p:PublishAot=true -p:EnableAotAnalyzer=true
& WindowsSearch/Mizon.Windows.Activation.Tests/bin/Release/net11.0/win-x64/publish/Mizon.Windows.Activation.Tests.exe
```

JSON reflection is disabled in both managed and native runs. Package fixtures are
copied from the actual app sources, so registration checks also work when the
executable starts outside the repository working directory.
