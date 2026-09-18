<#
.SYNOPSIS
    Builds the Hearthstone Reconnect HDT plugin and installs it into Hearthstone Deck Tracker.

.DESCRIPTION
    Compiles MyReconnectorPlugin.dll against an HDT installation, checks the binary, and copies
    it to %AppData%\HearthstoneDeckTracker\Plugins\MyReconnector. Re-run it after an HDT update
    if the plugin stops loading.

.PARAMETER HdtPath
    HDT folder that contains the real HearthstoneDeckTracker.exe (the app-x.y.z folder).
    Default: the newest version installed under %LocalAppData%\HearthstoneDeckTracker.

.PARAMETER SkipInstall
    Build only; do not copy the DLL into HDT's Plugins folder.

.PARAMETER Package
    Also create the release zip and SHA256SUMS.txt in .\artifacts.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build.ps1

.EXAMPLE
    .\build.ps1 -HdtPath "C:\Tools\Hearthstone Deck Tracker" -SkipInstall -Package
#>
param(
    [string]$HdtPath,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipInstall,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\MyReconnectorPlugin\MyReconnectorPlugin.csproj'
$artifacts = Join-Path $root 'artifacts'

# --- Locate HDT --------------------------------------------------------------
if (-not $HdtPath) {
    $hdtRoot = Join-Path $env:LOCALAPPDATA 'HearthstoneDeckTracker'
    $appDir = Get-ChildItem $hdtRoot -Directory -Filter 'app-*' -ErrorAction SilentlyContinue |
        ForEach-Object {
            $v = $null
            if ([version]::TryParse(($_.Name -replace '^app-', ''), [ref]$v)) {
                [pscustomobject]@{ Dir = $_; Version = $v }
            }
        } |
        Sort-Object Version |
        Select-Object -Last 1
    if (-not $appDir) {
        throw "No Hearthstone Deck Tracker installation found in $hdtRoot. Install HDT or pass -HdtPath."
    }
    $HdtPath = $appDir.Dir.FullName
}
if (-not (Test-Path (Join-Path $HdtPath 'HearthstoneDeckTracker.exe'))) {
    throw "HearthstoneDeckTracker.exe not found in '$HdtPath'."
}
$hdtVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $HdtPath 'HearthstoneDeckTracker.exe')).Version
Write-Host "Building against HDT $hdtVersion" -ForegroundColor Cyan

# --- Build -------------------------------------------------------------------
dotnet build $project -c $Configuration "-p:HdtPath=$HdtPath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

$dll = Join-Path $root "src\MyReconnectorPlugin\bin\$Configuration\MyReconnectorPlugin.dll"
$version = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3)

# --- Check the binary before it goes anywhere --------------------------------
# 1. HDT's plugin loader refuses plugins with a static iphlpapi import. The name may appear as a
#    UTF-16 runtime string (see ReconnectCore.cs), but never in the ASCII/UTF-8 metadata heaps
#    where DllImport module names live.
# 2. No absolute path of the build machine may be embedded (PathMap in Directory.Build.props).
function Test-BinaryContains([byte[]]$Bytes, [string]$Text) {
    $ascii = [Text.Encoding]::ASCII.GetString($Bytes)
    if ($ascii.IndexOf($Text, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    foreach ($offset in 0, 1) {
        $utf16 = [Text.Encoding]::Unicode.GetString($Bytes, $offset, $Bytes.Length - $offset)
        if ($utf16.IndexOf($Text, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

$bytes = [IO.File]::ReadAllBytes($dll)
if ([Text.Encoding]::ASCII.GetString($bytes) -match '(?i)iphlpapi') {
    throw 'The DLL imports iphlpapi statically; HDT would refuse to load it (and every other plugin).'
}
foreach ($local in @($root.TrimEnd('\'), $env:USERPROFILE) | Where-Object { $_ }) {
    if (Test-BinaryContains $bytes $local) {
        throw "The DLL contains the local path '$local'. Check PathMap in Directory.Build.props."
    }
}
Write-Host "Built MyReconnectorPlugin $version - binary checks passed." -ForegroundColor Green

# --- Package -----------------------------------------------------------------
if ($Package) {
    if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
    $staging = Join-Path $artifacts 'staging\MyReconnector'
    New-Item -ItemType Directory -Force $staging | Out-Null
    Copy-Item $dll $staging
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $staging 'LICENSE.txt')

    Copy-Item $dll $artifacts
    $zip = Join-Path $artifacts "MyReconnectorPlugin-v$version.zip"
    Compress-Archive -Path $staging -DestinationPath $zip
    Remove-Item (Join-Path $artifacts 'staging') -Recurse -Force

    Get-ChildItem $artifacts -File |
        Sort-Object Name |
        ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } |
        Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Packaged: $zip" -ForegroundColor Green
}

# --- Install into HDT --------------------------------------------------------
if (-not $SkipInstall) {
    $pluginDir = Join-Path $env:APPDATA 'HearthstoneDeckTracker\Plugins\MyReconnector'
    New-Item -ItemType Directory -Force $pluginDir | Out-Null
    try {
        Copy-Item $dll $pluginDir -Force
        Write-Host "Installed to $pluginDir - restart HDT (as administrator) to load it." -ForegroundColor Green
    } catch {
        Write-Warning "Could not copy the plugin - close HDT and re-run. ($_)"
    }
}
