#Requires -Version 5.1
<#
.SYNOPSIS
Stages and registers only the native Mizon Palette provider for the current user.
.EXAMPLE
.\Deploy.ps1 -PublishDirectory .\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish
.EXAMPLE
.\Deploy.ps1 -Inspect
.NOTES
Does not enable Developer Mode, change publisher policies, restart Windows Search,
create catalog files, or register the legacy indexer. Registration alone does not
prove that this private Windows extension contract accepts third-party publishers.
#>
[CmdletBinding(DefaultParameterSetName = 'Deploy')]
param(
    [Parameter(Mandatory, Position = 0, ParameterSetName = 'Deploy')]
    [string] $PublishDirectory,

    [Parameter(ParameterSetName = 'Deploy')]
    [switch] $StageOnly,

    [Parameter(Mandatory, ParameterSetName = 'Inspect')]
    [switch] $Inspect
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$packageName = 'Mizon.Windows.PaletteProvider'
$executableName = 'Mizon.Windows.PaletteProvider.exe'
$assemblyName = 'Mizon.Windows.PaletteProvider.dll'
$clientRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$providerRoot = Join-Path $env:USERPROFILE '.mizon\WindowsPaletteProvider'
$deploymentRoot = Join-Path $providerRoot 'Packages'
$providerLog = Join-Path $providerRoot 'Logs\WindowsPaletteProvider.log'
$deploymentLog = $null

function Write-DeploymentMessage([string] $Message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss.fff zzz} {1}' -f [DateTimeOffset]::Now, $Message
    Write-Host $line
    if ($null -ne $script:deploymentLog) {
        [IO.File]::AppendAllText($script:deploymentLog, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    }
}

function Show-PublisherPolicy {
    $policyPath = 'SOFTWARE\Microsoft\Windows\System Composer\AllowedPublishers'
    foreach ($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryHive]::CurrentUser)) {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, [Microsoft.Win32.RegistryView]::Registry64)
        try {
            $policyKey = $baseKey.OpenSubKey($policyPath, $false)
            try {
                $names = if ($null -eq $policyKey) { '(key absent)' } else { ($policyKey.GetValueNames() | Sort-Object) -join ', ' }
                Write-DeploymentMessage "$hive publisher allowlist: $names"
            }
            finally { if ($null -ne $policyKey) { $policyKey.Dispose() } }
        }
        finally { $baseKey.Dispose() }
    }
    Write-DeploymentMessage 'Publisher policies are inspected only; no allowlist or Windows security setting is changed.'
}

function Show-Registration {
    $packages = @(Get-AppxPackage -Name $script:packageName -ErrorAction Stop)
    if ($packages.Count -eq 0) {
        Write-DeploymentMessage 'The Mizon Palette provider package is not registered for this user.'
    }
    foreach ($package in $packages) {
        Write-DeploymentMessage "Package: $($package.PackageFullName)"
        Write-DeploymentMessage "Publisher: $($package.Publisher); publisher ID: $($package.PublisherId)"
        Write-DeploymentMessage "Install location: $($package.InstallLocation)"
        $manifest = Get-AppxPackageManifest -Package $package.PackageFullName
        $extensions = @($manifest.SelectNodes("//*[local-name()='AppExtension' and @Name='com.microsoft.palettes']"))
        foreach ($extension in $extensions) {
            $kinds = @($extension.SelectNodes(".//*[local-name()='KindSpec']") | ForEach-Object { $_.InnerText }) -join ', '
            Write-DeploymentMessage "Registered extension: $($extension.GetAttribute('Id')); kinds: $kinds"
        }
        $binaryPath = Join-Path $package.InstallLocation $script:executableName
        if (Test-Path -LiteralPath $binaryPath -PathType Leaf) {
            Write-DeploymentMessage "Registered executable SHA256: $((Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash)"
        }
        $assemblyPath = Join-Path $package.InstallLocation $script:assemblyName
        if (Test-Path -LiteralPath $assemblyPath -PathType Leaf) {
            Write-DeploymentMessage "Registered application assembly SHA256: $((Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash)"
        }
    }
    Show-PublisherPolicy
    Write-DeploymentMessage "Provider trace: $script:providerLog"
    Write-DeploymentMessage 'Package registration is distinct from PaletteCatalog acceptance and visible search results.'
}

function Get-NextPackageVersion {
    $highest = [Version]'1.0.0.0'
    foreach ($package in @(Get-AppxPackage -Name $script:packageName -ErrorAction Stop)) {
        if ([Version]$package.Version -gt $highest) { $highest = [Version]$package.Version }
    }
    if (Test-Path -LiteralPath $script:deploymentRoot -PathType Container) {
        foreach ($directory in Get-ChildItem -LiteralPath $script:deploymentRoot -Directory) {
            $candidate = $null
            if ([Version]::TryParse(($directory.Name -split '-')[0], [ref]$candidate) -and $candidate -gt $highest) {
                $highest = $candidate
            }
        }
    }
    if ($highest.Revision -lt 65535) { return [Version]::new($highest.Major, $highest.Minor, $highest.Build, $highest.Revision + 1) }
    if ($highest.Build -lt 65535) { return [Version]::new($highest.Major, $highest.Minor, $highest.Build + 1, 0) }
    if ($highest.Minor -lt 65535) { return [Version]::new($highest.Major, $highest.Minor + 1, 0, 0) }
    throw 'The package version range is exhausted.'
}

try {
    if (-not [Environment]::Is64BitProcess) { throw 'Run this script from 64-bit Windows PowerShell.' }
    if ($Inspect) {
        Show-Registration
        return
    }

    $publishPath = (Resolve-Path -LiteralPath $PublishDirectory).ProviderPath
    $sourceExecutable = Join-Path $publishPath $executableName
    if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
        throw "Publish the .NET provider first. Missing executable: $sourceExecutable"
    }

    $clientManifest = [xml](Get-Content -LiteralPath (Join-Path $clientRoot 'Package.appxmanifest') -Raw)
    $publisher = [string]$clientManifest.Package.Identity.Publisher
    if ([string]::IsNullOrWhiteSpace($publisher)) { throw 'The main client package has no Publisher identity.' }
    $manifest = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw)
    if ($manifest.Package.Identity.Name -ne $packageName) { throw 'Unexpected provider package identity in the manifest template.' }
    $version = Get-NextPackageVersion
    $manifest.Package.Identity.SetAttribute('Publisher', $publisher)
    $manifest.Package.Identity.SetAttribute('Version', $version.ToString())

    # A regular .NET apphost can stay identical while its application DLL changes.
    $sourceAssembly = Join-Path $publishPath $assemblyName
    $payloadFile = if (Test-Path -LiteralPath $sourceAssembly -PathType Leaf) { $sourceAssembly } else { $sourceExecutable }
    $payloadName = [IO.Path]::GetFileName($payloadFile)
    $hash = (Get-FileHash -LiteralPath $payloadFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $directoryName = '{0}-{1}' -f $version, $hash.Substring(0, 16)
    $destination = Join-Path $deploymentRoot $directoryName
    if (Test-Path -LiteralPath $destination) { throw "Immutable deployment directory already exists: $destination" }
    $staging = Join-Path $deploymentRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))

    $deploymentLog = Join-Path $providerRoot 'Logs\WindowsPaletteProvider.Deploy.log'
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($deploymentLog)) | Out-Null
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    Write-DeploymentMessage "Preparing native provider version $version from $sourceExecutable"
    Write-DeploymentMessage "Source $payloadName SHA256: $hash"
    foreach ($item in Get-ChildItem -LiteralPath $publishPath -Force) {
        if ($item.Extension -eq '.pdb') { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $staging -Recurse -Force
    }

    $assets = Join-Path $staging 'Assets'
    $public = Join-Path $staging 'Public'
    [IO.Directory]::CreateDirectory($assets) | Out-Null
    [IO.Directory]::CreateDirectory($public) | Out-Null
    foreach ($name in @('StoreLogo', 'Square44x44Logo', 'Square150x150Logo')) {
        $source = Join-Path $clientRoot "Assets\AppIcon\$name.scale-100.png"
        Copy-Item -LiteralPath $source -Destination (Join-Path $assets "$name.png") -Force
    }
    Copy-Item -LiteralPath (Join-Path $assets 'Square150x150Logo.png') -Destination (Join-Path $public 'Music.png') -Force
    Copy-Item -LiteralPath (Join-Path $clientRoot 'Assets\Cover1.jpg') -Destination (Join-Path $public 'Cover.jpg') -Force
    $manifest.Save((Join-Path $staging 'AppxManifest.xml'))
    $stagedHash = (Get-FileHash -LiteralPath (Join-Path $staging $payloadName) -Algorithm SHA256).Hash
    if (-not $stagedHash.Equals($hash, [StringComparison]::OrdinalIgnoreCase)) { throw 'The staged application hash differs from the published application.' }
    # Validate both absolute targets before moving this directory tree.
    $rootPrefix = [IO.Path]::GetFullPath($deploymentRoot).TrimEnd('\') + '\'
    foreach ($targetPath in @($staging, $destination)) {
        $absoluteTarget = [IO.Path]::GetFullPath($targetPath)
        if (-not $absoluteTarget.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Deployment target is outside the provider directory: $absoluteTarget"
        }
    }
    # Final directory names are never reused or overwritten while COM may hold a process.
    [IO.Directory]::Move($staging, $destination)
    Write-DeploymentMessage "Prepared immutable package: $destination"

    if ($StageOnly) {
        Write-DeploymentMessage 'StageOnly: no package registration or system changes were performed.'
        Show-PublisherPolicy
        return
    }

    Write-DeploymentMessage 'Registering the distinct provider package for the current user.'
    Add-AppxPackage -Register (Join-Path $destination 'AppxManifest.xml') -ForceTargetApplicationShutdown -ErrorAction Stop
    $registered = @(Get-AppxPackage -Name $packageName -ErrorAction Stop | Where-Object { [Version]$_.Version -eq $version })
    if ($registered.Count -ne 1) { throw 'Registration returned without the expected package version being present.' }
    Write-DeploymentMessage 'Provider package registration completed.'
    Show-Registration
}
catch {
    Write-DeploymentMessage "FAILED: $($_.Exception.Message)"
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    exit 1
}
