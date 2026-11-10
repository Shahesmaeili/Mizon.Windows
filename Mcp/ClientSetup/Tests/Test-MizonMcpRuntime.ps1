<#
Isolated deployment regression checks. Never touches the installed package,
registered launchers, real MCP identity store or in-app permissions.
#>
[CmdletBinding()]
param(
    [string]$TemporaryDirectory = [IO.Path]::GetTempPath(),
    [string]$CompanionDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'Stage-MizonMcpRuntime.ps1')
$testRoot = Join-Path ([IO.Path]::GetFullPath($TemporaryDirectory)) ('MizonMcpRuntimeTests-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$testRoot = [IO.Path]::GetFullPath($testRoot)
$utf8 = New-Object System.Text.UTF8Encoding($false)
$passed = 0
$liveHost = $null

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Confirm-TestPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing an operation outside the isolated test root.'
    }
    return $full
}
function Write-TestText([string]$Path, [string]$Text) {
    $full = Confirm-TestPath $Path
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full))
    [IO.File]::WriteAllText($full, $Text, $utf8)
}
function New-Payload([string]$Name) {
    $path = Join-Path $testRoot $Name
    Write-TestText (Join-Path $path 'Mizon.Windows.Mcp.exe') 'synthetic-apphost'
    Write-TestText (Join-Path $path 'Mizon.Windows.Mcp.dll') 'synthetic-managed-entry'
    Write-TestText (Join-Path $path 'Example.Dependency.dll') 'dependency-v1'
    Write-TestText (Join-Path $path 'Mizon.Windows.Mcp.runtimeconfig.json') '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}'
    Write-TestText (Join-Path $path 'Mizon.Windows.Mcp.deps.json') '{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0/win-x64"},"targets":{".NETCoreApp,Version=v10.0/win-x64":{"Mizon.Windows.Mcp/1.0.0":{"runtime":{"Mizon.Windows.Mcp.dll":{}}},"Example.Dependency/1.0.0":{"runtime":{"lib/net10.0/Example.Dependency.dll":{}}}}},"libraries":{}}'
    Write-TestText (Join-Path $path 'nested\data.txt') 'nested-payload-data'
    return $path
}
function Assert-Fails([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert-Check $failed $Message
}
function Pass([string]$Name) {
    $script:passed++
    Write-Output ('PASS ' + $Name)
}
function Read-McpReply([Diagnostics.Process]$HostProcess, [int]$Id) {
    for ($i = 0; $i -lt 12; $i++) {
        $read = $HostProcess.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(15000)) { throw 'The isolated MCP host did not respond in time.' }
        if ($null -eq $read.Result) { throw 'The isolated MCP host closed its transport unexpectedly.' }
        $message = $read.Result | ConvertFrom-Json
        if ($message.id -eq $Id) { return $message }
    }
    throw 'The isolated MCP host did not return the expected response.'
}

try {
    $source = New-Payload 'source'
    $cache = Join-Path $testRoot 'runtime'
    $first = Get-MizonMcpRuntime -SourceDirectory $source -RuntimeRoot $cache
    Assert-Check ([IO.File]::Exists($first)) 'The staged executable was not published.'
    Assert-Check (-not $first.StartsWith($source, [StringComparison]::OrdinalIgnoreCase)) 'The launcher returned the source executable.'
    $published = [IO.Path]::GetDirectoryName($first)
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
        $relative = $file.FullName.Substring($source.Length + 1)
        $copy = Join-Path $published $relative
        Assert-Check ([IO.File]::Exists($copy)) ('Missing staged payload file: ' + $relative)
        Assert-Check ((Get-FileHash -LiteralPath $file.FullName).Hash -eq (Get-FileHash -LiteralPath $copy).Hash) ('Changed payload file: ' + $relative)
    }
    Pass 'The complete payload is copied outside its package directory'

    $cachedDependency = Join-Path $published 'Example.Dependency.dll'
    $lastWrite = [IO.File]::GetLastWriteTimeUtc($cachedDependency)
    $locked = [IO.File]::Open($cachedDependency, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $same = Get-MizonMcpRuntime -SourceDirectory $source -RuntimeRoot $cache
        Assert-Check ($same -eq $first) 'An unchanged payload did not reuse its snapshot.'
        Assert-Check ([IO.File]::GetLastWriteTimeUtc($cachedDependency) -eq $lastWrite) 'The launcher overwrote a snapshot that can be in use.'
    }
    finally { $locked.Dispose() }
    Pass 'An unchanged version is reused while its dependency is locked'

    Write-TestText (Join-Path $source 'Example.Dependency.dll') 'dependency-v2'
    $second = Get-MizonMcpRuntime -SourceDirectory $source -RuntimeRoot $cache
    Assert-Check ($second -ne $first) 'Changing only a dependency did not create a new version.'
    Assert-Check ([IO.File]::ReadAllText($cachedDependency) -eq 'dependency-v1') 'The previous runtime was modified.'
    Pass 'A dependency change creates a separate immutable version'

    $concurrentSource = New-Payload 'concurrent-source'
    $concurrentCache = Join-Path $testRoot 'concurrent-runtime'
    $helper = Join-Path (Split-Path $PSScriptRoot -Parent) 'Stage-MizonMcpRuntime.ps1'
    $childCode = ". '$($helper.Replace("'", "''"))'; Get-MizonMcpRuntime -SourceDirectory '$($concurrentSource.Replace("'", "''"))' -RuntimeRoot '$($concurrentCache.Replace("'", "''"))'"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCode))
    $workers = @()
    try {
        for ($i = 0; $i -lt 4; $i++) {
            $output = Join-Path $testRoot ('worker-' + $i + '.out')
            $errorLog = Join-Path $testRoot ('worker-' + $i + '.err')
            $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded) -WindowStyle Hidden -PassThru -RedirectStandardOutput $output -RedirectStandardError $errorLog
            $workers += [pscustomobject]@{ Process = $process; Output = $output; ErrorLog = $errorLog }
        }
        $paths = @()
        foreach ($worker in $workers) {
            Assert-Check ($worker.Process.WaitForExit(30000)) 'A staging worker did not finish.'
            $worker.Process.Refresh()
            Assert-Check ($worker.Process.ExitCode -eq 0) ('A concurrent staging worker failed: ' + [IO.File]::ReadAllText($worker.ErrorLog))
            $paths += [IO.File]::ReadAllText($worker.Output).Trim()
        }
        Assert-Check (@($paths | Select-Object -Unique).Count -eq 1) 'Concurrent workers published different snapshots.'
        Assert-Check ([IO.File]::Exists($paths[0])) 'Concurrent publication did not produce a complete executable.'
    }
    finally {
        foreach ($worker in $workers) {
            if (-not $worker.Process.HasExited) { $worker.Process.Kill(); [void]$worker.Process.WaitForExit(5000) }
            $worker.Process.Dispose()
        }
    }
    Pass 'Concurrent workers resolve the same atomically published snapshot'

    $broken = New-Payload 'broken-source'
    Remove-Item -LiteralPath (Confirm-TestPath (Join-Path $broken 'Example.Dependency.dll'))
    $brokenCache = Join-Path $testRoot 'broken-runtime'
    Assert-Fails { Get-MizonMcpRuntime -SourceDirectory $broken -RuntimeRoot $brokenCache } 'An incomplete dependency set was published.'
    Assert-Check (@(Get-ChildItem -LiteralPath $brokenCache -Filter 'Mizon.Windows.Mcp.exe' -File -Recurse -ErrorAction SilentlyContinue).Count -eq 0) 'Failed staging left an executable snapshot.'
    Pass 'A partially deployed payload cannot become a launchable snapshot'

    $changingSource = New-Payload 'changing-source'
    $changingCache = Join-Path $testRoot 'changing-runtime'
    [void][IO.Directory]::CreateDirectory($changingCache)
    $large = [IO.File]::Create((Confirm-TestPath (Join-Path $changingSource 'z-large.bin')))
    try { $large.SetLength(128MB) } finally { $large.Dispose() }
    $watcherReady = Join-Path $testRoot 'watcher-ready'
    $watcherChanged = Join-Path $testRoot 'watcher-changed'
    $watchCode = @"
`$ErrorActionPreference = 'Stop'
[IO.File]::WriteAllText('$($watcherReady.Replace("'", "''"))', 'ready')
`$timer = [Diagnostics.Stopwatch]::StartNew()
while (`$timer.Elapsed.TotalSeconds -lt 20) {
    foreach (`$stage in [IO.Directory]::EnumerateDirectories('$($changingCache.Replace("'", "''"))', 'staging-*')) {
        if ([IO.File]::Exists([IO.Path]::Combine(`$stage, 'Example.Dependency.dll'))) {
            [IO.File]::WriteAllText('$((Join-Path $changingSource 'Example.Dependency.dll').Replace("'", "''"))', 'changed-during-staging')
            [IO.File]::WriteAllText('$($watcherChanged.Replace("'", "''"))', 'changed')
            exit 0
        }
    }
    [Threading.Thread]::Sleep(1)
}
exit 2
"@
    $watchEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($watchCode))
    $watcher = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', $watchEncoded) -WindowStyle Hidden -PassThru
    try {
        $waitReady = [Diagnostics.Stopwatch]::StartNew()
        while (-not [IO.File]::Exists($watcherReady) -and $waitReady.Elapsed.TotalSeconds -lt 10) { [Threading.Thread]::Sleep(10) }
        Assert-Check ([IO.File]::Exists($watcherReady)) 'The source-change watcher did not start.'
        Assert-Fails { Get-MizonMcpRuntime -SourceDirectory $changingSource -RuntimeRoot $changingCache } 'A source changed during staging was published.'
        Assert-Check ($watcher.WaitForExit(10000)) 'The source-change watcher did not finish.'
        Assert-Check ([IO.File]::Exists($watcherChanged)) 'The source-change fixture did not alter the source.'
        Assert-Check (@(Get-ChildItem -LiteralPath $changingCache -Filter 'Mizon.Windows.Mcp.exe' -File -Recurse -ErrorAction SilentlyContinue).Count -eq 0) 'Source-change failure left a partial executable snapshot.'
    }
    finally {
        if (-not $watcher.HasExited) { $watcher.Kill(); [void]$watcher.WaitForExit(5000) }
        $watcher.Dispose()
    }
    Pass 'A source change during copying cannot publish a mixed-version snapshot'

    Write-TestText (Join-Path ([IO.Path]::GetDirectoryName($second)) 'Example.Dependency.dll') 'corrupt-cache-data'
    Assert-Fails { Get-MizonMcpRuntime -SourceDirectory $source -RuntimeRoot $cache } 'A corrupted cached snapshot was used or overwritten.'
    Pass 'Corrupted cached content fails closed'

    if ($CompanionDirectory) {
        $realSource = Join-Path $testRoot 'real-source'
        [void][IO.Directory]::CreateDirectory($realSource)
        foreach ($entry in Get-ChildItem -LiteralPath ([IO.Path]::GetFullPath($CompanionDirectory)) -Force) {
            Copy-Item -LiteralPath $entry.FullName -Destination $realSource -Recurse
        }
        $realRuntime = Get-MizonMcpRuntime -SourceDirectory $realSource -RuntimeRoot (Join-Path $testRoot 'real-runtime')
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $realRuntime
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $liveHost = [Diagnostics.Process]::Start($start)
        $liveHost.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"Mizon.RuntimeIsolationTest","version":"1.0"}}}')
        $initialized = Read-McpReply $liveHost 1
        Assert-Check ($null -ne $initialized.result.serverInfo) 'The copied MCP host did not initialize.'
        $liveHost.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
        $liveHost.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
        $catalog = Read-McpReply $liveHost 2
        Assert-Check ($catalog.result.tools.Count -eq 8) 'The copied MCP host returned an unexpected catalog.'
        $removalTarget = Confirm-TestPath $realSource
        Remove-Item -LiteralPath $removalTarget -Recurse -Force
        Assert-Check (-not [IO.Directory]::Exists($realSource)) 'The running companion still locked its original payload.'
        $liveHost.StandardInput.WriteLine('{"jsonrpc":"2.0","id":3,"method":"tools/list","params":{}}')
        $catalogAfterDelete = Read-McpReply $liveHost 3
        Assert-Check ($catalogAfterDelete.result.tools.Count -eq 8) 'The running companion depended on the removed source files.'
        Pass 'A real MCP host stays functional while its original payload is deleted'
    }
    Write-Output ("{0} staging checks passed. No real app connection or permission was created." -f $passed)
}
finally {
    if ($null -ne $liveHost) {
        try {
            $liveHost.StandardInput.Close()
            if (-not $liveHost.WaitForExit(5000)) { $liveHost.Kill(); [void]$liveHost.WaitForExit(5000) }
        }
        finally { $liveHost.Dispose() }
    }
    $cleanupPath = [IO.Path]::GetFullPath($testRoot)
    $expectedParent = [IO.Path]::GetFullPath($TemporaryDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($cleanupPath.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($cleanupPath).StartsWith('MizonMcpRuntimeTests-')) {
        if (Test-Path -LiteralPath $cleanupPath) { Remove-Item -LiteralPath $cleanupPath -Recurse -Force }
    }
}
