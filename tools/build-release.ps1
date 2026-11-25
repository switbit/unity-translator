param(
    [string]$ManagedDir = $env:UNITY_MANAGED_DIR,
    [string]$BepInExCore = $env:UNITY_BEPINEX_CORE,
    [string]$CacheDir = (Join-Path (Split-Path -Parent $PSScriptRoot) '.cache'),
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0',
    [string]$BepInExVersion = '5.4.23.6',
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$PluginProject = Join-Path $Root 'src\UnityTranslator.Plugin\UnityTranslator.Plugin.csproj'
$Dist = Join-Path $Root 'dist'
$PluginDll = Join-Path $Dist 'plugin\UnityTranslator.Plugin.dll'
$SetupExe = Join-Path $Dist 'UnityTranslator-Setup.exe'
$PackageDir = Join-Path $Dist "UnityTranslator-$Version"
$PackageZip = Join-Path $Dist "UnityTranslator-$Version.zip"

if ([string]::IsNullOrWhiteSpace($BepInExCore)) {
    $BepInExCore = Join-Path $CacheDir 'BepInEx\BepInEx\core'
}

if ([string]::IsNullOrWhiteSpace($ManagedDir)) {
    throw 'Set UNITY_MANAGED_DIR to the Managed folder of a Mono Unity game, or pass -ManagedDir.'
}
if (-not (Test-Path (Join-Path $ManagedDir 'UnityEngine.CoreModule.dll'))) {
    throw "UnityEngine.CoreModule.dll was not found in $ManagedDir"
}
if (-not (Test-Path (Join-Path $BepInExCore 'BepInEx.dll'))) {
    throw "BepInEx.dll was not found in $BepInExCore. Run tools\fetch-bepinex.ps1 first."
}

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$Csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Csc) {
    throw '.NET Framework 4.x csc.exe was not found. Install .NET Framework 4.x or Visual Studio Build Tools.'
}

$dotnetCommand = Get-Command $Dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCommand) {
    throw "dotnet was not found: $Dotnet. Install the .NET SDK 8+ or pass -Dotnet <path>."
}

$env:UNITY_MANAGED_DIR = $ManagedDir
$env:UNITY_BEPINEX_CORE = $BepInExCore

Write-Host 'Building plugin...'
& $dotnetCommand.Source build $PluginProject -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

$icon = Join-Path $Root 'assets\UnityTranslator.ico'
$manifest = Join-Path $Root 'src\UnityTranslator.Setup\app.manifest'
$setupSource = Join-Path $Root 'src\UnityTranslator.Setup\Setup.cs'
$bep64 = Join-Path $CacheDir "BepInEx_win_x64_$BepInExVersion.zip"
$bep86 = Join-Path $CacheDir "BepInEx_win_x86_$BepInExVersion.zip"

foreach ($required in @($icon, $manifest, $setupSource, $bep64, $bep86, $PluginDll)) {
    if (-not (Test-Path $required)) {
        throw "Required file is missing: $required. Run tools\fetch-bepinex.ps1 first."
    }
}

New-Item -ItemType Directory -Force -Path $Dist | Out-Null

$rsp = Join-Path ([System.IO.Path]::GetTempPath()) 'unity-translator-setup.rsp'
$lines = @(
    '/nologo',
    '/target:winexe',
    '/codepage:65001',
    "/out:`"$SetupExe`"",
    "/win32manifest:`"$manifest`"",
    "/win32icon:`"$icon`"",
    "/resource:`"$bep64`",BepInEx_x64.zip",
    "/resource:`"$bep86`",BepInEx_x86.zip",
    "/resource:`"$PluginDll`",UnityTranslator.Plugin.dll",
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll',
    "`"$setupSource`""
)
Set-Content -Path $rsp -Value $lines -Encoding UTF8

Write-Host 'Building installer...'
& $Csc "@$rsp"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

Write-Host 'Packaging...'
if (Test-Path $PackageDir) { Remove-Item $PackageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $PackageDir 'manual') | Out-Null
Copy-Item (Join-Path $Root 'README.md') $PackageDir -Force
Copy-Item $SetupExe $PackageDir -Force
Copy-Item $PluginDll (Join-Path $PackageDir 'manual') -Force

if (Test-Path $PackageZip) { Remove-Item $PackageZip -Force }
Compress-Archive -Path $PackageDir -DestinationPath $PackageZip -CompressionLevel Optimal

$hashes = @(
    (Get-FileHash $SetupExe -Algorithm SHA256),
    (Get-FileHash $PackageZip -Algorithm SHA256),
    (Get-FileHash $PluginDll -Algorithm SHA256)
)
$hashLines = foreach ($h in $hashes) {
    $name = Split-Path -Leaf $h.Path
    "$($h.Hash.ToLowerInvariant())  $name"
}
Set-Content -Path (Join-Path $Dist 'SHA256SUMS.txt') -Value $hashLines -Encoding ASCII

Write-Host ''
Write-Host 'Done:'
Write-Host "  $SetupExe"
Write-Host "  $PackageZip"
Write-Host "  $(Join-Path $Dist 'SHA256SUMS.txt')"
