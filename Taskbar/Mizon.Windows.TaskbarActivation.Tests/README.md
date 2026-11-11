# Taskbar profile activation checks

Production-linked .NET 11 / NativeAOT checks for the companion's single-request
coordinator, shared result validation, deadline, sign-out/account/island/disposal
cancellation, late replies, localized statuses and optional IPC package identity.
All transport responses are synthetic; no native windows or applications launch.

```powershell
dotnet run --project Taskbar/Mizon.Windows.TaskbarActivation.Tests -c Release
dotnet publish Taskbar/Mizon.Windows.TaskbarActivation.Tests -c Release -r win-x64 -p:PublishAot=true
Taskbar/Mizon.Windows.TaskbarActivation.Tests/bin/Release/net11.0/win-x64/publish/Mizon.Windows.TaskbarActivation.Tests.exe
```

This suite depends on the shared `Services/AppDeepLinkResult.cs` contract. A passing
headless run does not establish actual Windows launch-for-results, taskbar ring
layout/animation, focus handoff, DPI behavior or Narrator speech.
