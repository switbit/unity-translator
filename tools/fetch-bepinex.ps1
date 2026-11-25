param(
    [string]$CacheDir = (Join-Path (Split-Path -Parent $PSScriptRoot) '.cache'),
    [string]$Version = '5.4.23.6'
)

$ErrorActionPreference = 'Stop'

$files = @(
    @{ Name = "BepInEx_win_x64_$Version.zip"; Url = "https://github.com/BepInEx/BepInEx/releases/download/v$Version/BepInEx_win_x64_$Version.zip" },
    @{ Name = "BepInEx_win_x86_$Version.zip"; Url = "https://github.com/BepInEx/BepInEx/releases/download/v$Version/BepInEx_win_x86_$Version.zip" }
)

New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null

foreach ($file in $files) {
    $target = Join-Path $CacheDir $file.Name
    if (Test-Path $target) {
        Write-Host "Already present: $($file.Name)"
        continue
    }

    Write-Host "Downloading $($file.Name)..."
    Invoke-WebRequest -Uri $file.Url -OutFile $target -UseBasicParsing
}

$coreRoot = Join-Path $CacheDir 'BepInEx'
$coreDll = Join-Path $coreRoot 'BepInEx\core\BepInEx.dll'
if (-not (Test-Path $coreDll)) {
    $x64 = Join-Path $CacheDir "BepInEx_win_x64_$Version.zip"
    Write-Host "Extracting BepInEx x64 core..."
    Expand-Archive -Path $x64 -DestinationPath $coreRoot -Force
}

if (-not (Test-Path $coreDll)) {
    throw "BepInEx core was not extracted to $coreDll"
}

Write-Host "BepInEx $Version is ready."
Write-Host "  Archives: $CacheDir"
Write-Host "  Core:     $coreRoot\BepInEx\core"
