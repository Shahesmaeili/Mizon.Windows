# Resolve only the current user's registered Mizon package. Stdout is a single
# staged executable path consumed by Mizon-Mcp.cmd; diagnostics always use stderr.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
try {
    $package = Get-AppxPackage -Name '83999a60-c1ad-4cca-af16-4c69bf4a0067' |
        Where-Object { $_.PackageFamilyName -eq '83999a60-c1ad-4cca-af16-4c69bf4a0067_yvtn54431hrs8' } |
        Sort-Object -Property Version -Descending |
        Select-Object -First 1

    if ($null -eq $package) {
        throw 'Mizon is not installed for the current Windows user. Install/deploy Mizon first.'
    }

    $companion = Join-Path $package.InstallLocation 'Mcp\Payload\Mizon.Windows.Mcp.exe'
    if (-not (Test-Path -LiteralPath $companion -PathType Leaf)) {
        throw 'The installed Mizon package does not contain the MCP companion. Deploy a current Mizon build.'
    }

    . (Join-Path $PSScriptRoot 'Stage-MizonMcpRuntime.ps1')
    $runtimeRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Mizon\Mcp\Runtime'
    $runtimeExecutable = Get-MizonMcpRuntime -SourceDirectory ([IO.Path]::GetDirectoryName($companion)) -RuntimeRoot $runtimeRoot
    [Console]::Out.WriteLine($runtimeExecutable)
    exit 0
}
catch {
    [Console]::Error.WriteLine('Mizon MCP: ' + $_.Exception.Message)
    exit 2
}
