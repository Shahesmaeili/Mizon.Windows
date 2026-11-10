# PowerShell 5.1-compatible staging. Dot-sourcing only defines functions; it never
# starts a process or reads/writes client identities and permission stores.
function Get-MizonMcpPayloadSnapshot {
    param([Parameter(Mandatory = $true)][string]$Directory, [switch]$Runtime)

    $root = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not [IO.Directory]::Exists($root)) { throw 'The Mizon MCP payload directory is unavailable.' }
    if (([IO.File]::GetAttributes($root) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Mizon MCP payload directories cannot be links.'
    }
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($root)
    $files = New-Object 'System.Collections.Generic.List[string]'
    while ($pending.Count -gt 0) {
        foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())) {
            $attributes = [IO.File]::GetAttributes($path)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Mizon MCP payload entries cannot be links.' }
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) { $pending.Push($path); continue }
            $relative = $path.Substring($prefix.Length).Replace('\', '/')
            if ($Runtime -and $relative -eq '.complete') { continue }
            if ($relative -eq '.complete') { throw 'The Mizon MCP payload contains a reserved staging marker.' }
            $files.Add($relative)
            if ($files.Count -gt 4096) { throw 'The Mizon MCP payload has too many files.' }
        }
    }
    $files.Sort([StringComparer]::Ordinal)
    $fileNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $files) { [void]$fileNames.Add($relative) }
    foreach ($required in @('Mizon.Windows.Mcp.exe', 'Mizon.Windows.Mcp.dll', 'Mizon.Windows.Mcp.deps.json', 'Mizon.Windows.Mcp.runtimeconfig.json')) {
        if (-not $fileNames.Contains($required)) { throw 'The installed Mizon MCP payload is incomplete. Finish deploying Mizon before reconnecting.' }
    }

    # A failed AppX cleanup can leave the apphost while removing a dependency.
    # The packaged companion is framework-dependent; validate its runtime assets
    # before accepting any content fingerprint as a complete runnable version.
    $depsPath = [IO.Path]::Combine($root, 'Mizon.Windows.Mcp.deps.json')
    if ((New-Object IO.FileInfo($depsPath)).Length -gt 1048576) { throw 'The Mizon MCP dependency manifest is invalid.' }
    $deps = [IO.File]::ReadAllText($depsPath) | ConvertFrom-Json -ErrorAction Stop
    $targetName = [string]$deps.runtimeTarget.name
    $target = $deps.targets.PSObject.Properties[$targetName]
    if (-not $target -or -not $target.Value) { throw 'The Mizon MCP dependency manifest has no runtime target.' }
    foreach ($library in $target.Value.PSObject.Properties) {
        foreach ($kind in @('runtime', 'native', 'resources')) {
            $assets = $library.Value.PSObject.Properties[$kind]
            if (-not $assets) { continue }
            foreach ($asset in $assets.Value.PSObject.Properties) {
                $assetPath = $asset.Name.Replace('\', '/')
                if ([IO.Path]::GetFileName($assetPath) -eq '_._') { continue }
                $leaf = [IO.Path]::GetFileName($assetPath)
                if ($kind -eq 'resources' -and $asset.Value.locale) { $leaf = [string]$asset.Value.locale + '/' + $leaf }
                if (-not $fileNames.Contains($assetPath) -and -not $fileNames.Contains($leaf)) {
                    throw 'The installed Mizon MCP payload is missing a dependency. Finish deploying Mizon before reconnecting.'
                }
            }
        }
    }

    $entries = New-Object 'System.Collections.Generic.List[object]'
    $manifest = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($manifest, [Text.Encoding]::UTF8, $true)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($relative in $files) {
            $path = [IO.Path]::Combine($root, $relative.Replace('/', '\'))
            $stream = [IO.File]::OpenRead($path)
            try { $hash = $sha.ComputeHash($stream) }
            finally { $stream.Dispose() }
            $nameBytes = [Text.Encoding]::UTF8.GetBytes($relative)
            $writer.Write([int]$nameBytes.Length)
            $writer.Write([byte[]]$nameBytes)
            $writer.Write([byte[]]$hash)
            $entries.Add([pscustomobject]@{ Relative = $relative; Source = $path; Hash = [BitConverter]::ToString($hash).Replace('-', '').ToLowerInvariant() })
        }
        $writer.Flush()
        $manifest.Position = 0
        $fingerprint = [BitConverter]::ToString($sha.ComputeHash($manifest)).Replace('-', '').ToLowerInvariant()
        return [pscustomobject]@{ Fingerprint = $fingerprint; Entries = $entries.ToArray() }
    }
    finally { $sha.Dispose(); $writer.Dispose(); $manifest.Dispose() }
}

function Get-MizonMcpRuntime {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$RuntimeRoot
    )

    $source = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $root = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($root.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
        $root.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $source.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Mizon MCP runtime storage must be separate from its installed payload.'
    }
    [void][IO.Directory]::CreateDirectory($root)
    if (([IO.File]::GetAttributes($root) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Mizon MCP runtime storage cannot be a link.'
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $gate = $null
    try {
        while ($null -eq $gate) {
            try { $gate = [IO.File]::Open([IO.Path]::Combine($root, '.stage.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
            catch [IO.IOException] {
                if ($watch.Elapsed.TotalSeconds -ge 20) { throw 'Another Mizon MCP runtime preparation did not finish in time.' }
                Start-Sleep -Milliseconds 50
            }
        }

        $snapshot = Get-MizonMcpPayloadSnapshot -Directory $source
        $runtime = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $snapshot.Fingerprint))
        if ([IO.Path]::GetDirectoryName($runtime) -ne $root -or [IO.Path]::GetFileName($runtime) -notmatch '^[a-f0-9]{64}$') {
            throw 'Invalid Mizon MCP runtime destination.'
        }
        $marker = [IO.Path]::Combine($runtime, '.complete')
        if ([IO.Directory]::Exists($runtime)) {
            if (-not [IO.File]::Exists($marker) -or [IO.File]::ReadAllText($marker) -cne $snapshot.Fingerprint) {
                throw 'The cached Mizon MCP runtime is incomplete. It was not started.'
            }
            $cached = Get-MizonMcpPayloadSnapshot -Directory $runtime -Runtime
            if ($cached.Fingerprint -cne $snapshot.Fingerprint) { throw 'The cached Mizon MCP runtime failed verification. It was not started.' }
            return [IO.Path]::Combine($runtime, 'Mizon.Windows.Mcp.exe')
        }

        $staging = [IO.Path]::GetFullPath([IO.Path]::Combine($root, 'staging-' + [Guid]::NewGuid().ToString('N')))
        if ([IO.Path]::GetDirectoryName($staging) -ne $root -or [IO.Path]::GetFileName($staging) -notmatch '^staging-[a-f0-9]{32}$') {
            throw 'Invalid Mizon MCP staging destination.'
        }
        [void][IO.Directory]::CreateDirectory($staging)
        try {
            foreach ($entry in $snapshot.Entries) {
                $destination = [IO.Path]::GetFullPath([IO.Path]::Combine($staging, $entry.Relative.Replace('/', '\')))
                if (-not $destination.StartsWith($staging + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid Mizon MCP payload entry.' }
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
                [IO.File]::Copy($entry.Source, $destination, $false)
            }
            $copy = Get-MizonMcpPayloadSnapshot -Directory $staging
            $after = Get-MizonMcpPayloadSnapshot -Directory $source
            if ($copy.Fingerprint -cne $snapshot.Fingerprint -or $after.Fingerprint -cne $snapshot.Fingerprint) {
                throw 'The installed Mizon MCP payload changed during deployment. Reconnect after deployment completes.'
            }
            [IO.File]::WriteAllText([IO.Path]::Combine($staging, '.complete'), $snapshot.Fingerprint, (New-Object Text.UTF8Encoding($false)))
            # Both absolute paths were checked as immediate children of our root.
            # The lock makes this publication exclusive across simultaneous clients.
            [IO.Directory]::Move($staging, $runtime)
        }
        finally {
            if ([IO.Directory]::Exists($staging)) {
                # Remove only this invocation's generated staging directory. Old
                # immutable runtimes may still be executing and are never removed.
                $resolvedStaging = [IO.Path]::GetFullPath($staging)
                if ([IO.Path]::GetDirectoryName($resolvedStaging) -ne $root -or
                    [IO.Path]::GetFileName($resolvedStaging) -notmatch '^staging-[a-f0-9]{32}$' -or
                    (([IO.File]::GetAttributes($resolvedStaging) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
                    throw 'Unsafe Mizon MCP staging cleanup was refused.'
                }
                Remove-Item -LiteralPath $resolvedStaging -Recurse -Force -ErrorAction Stop
            }
        }
        return [IO.Path]::Combine($runtime, 'Mizon.Windows.Mcp.exe')
    }
    finally { if ($null -ne $gate) { $gate.Dispose() } }
}
