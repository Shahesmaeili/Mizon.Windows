# Desktop music shortcut checks

Run on Windows with the .NET 10 SDK:

```powershell
dotnet run --project Mizon.Windows.Shortcut.Tests.csproj -c Release
dotnet publish Mizon.Windows.Shortcut.Tests.csproj -c Release -r win-x64 -p:PublishAot=true
.\bin\Release\net10.0-windows10.0.22621.0\win-x64\publish\Mizon.Windows.Shortcut.Tests.exe
```

The harness links the production shortcut service, native Shell ABI and music URI
parser. It creates real `.lnk` and `.ico` files under a unique `%TEMP%\MizonShortcutTests`
directory, checks collision handling, invalid filenames, repeated/concurrent requests,
fallback artwork, changed covers, cleanup and icon decoding by Windows. It does not
alter desktop items or launch Mizon automatically.

To generate a temporary shortcut for an existing music ID, pass
`--sample <music-public-id>` to the executable. Opening that file manually exercises
the registered protocol and existing-instance activation. Test output is retained
in the printed temporary directory for inspection.
