[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   Building LazyPin Executable & Setup  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Compile LazyPin.ps1 -> LazyPin.exe using ps2exe
Write-Host "`n[1/2] Compiling LazyPin.exe..." -ForegroundColor Yellow

$ps2exePath = $null
$moduleCandidates = @(
    "C:\Users\iamra\OneDrive\Documents\WindowsPowerShell\Modules\ps2exe\1.0.18\ps2exe.psd1",
    "C:\Users\iamra\OneDrive\Documents\PowerShell\Modules\ps2exe\1.0.18\ps2exe.psd1",
    "$env:USERPROFILE\Documents\WindowsPowerShell\Modules\ps2exe\*\ps2exe.psd1",
    "$env:USERPROFILE\Documents\PowerShell\Modules\ps2exe\*\ps2exe.psd1"
)

foreach ($c in $moduleCandidates) {
    $found = Resolve-Path $c -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) {
        $ps2exePath = $found.Path
        break
    }
}

if ($ps2exePath) {
    Import-Module $ps2exePath -Force
} else {
    $ps2exeModule = Get-Module -ListAvailable -Name ps2exe | Select-Object -First 1
    if (-not $ps2exeModule) {
        throw "Module 'ps2exe' is not found. Please install it with: Install-Module ps2exe -Scope CurrentUser"
    }
    Import-Module $ps2exeModule.Path -Force
}

$ps2exeParams = @{
    inputFile         = Join-Path $scriptDir "LazyPin.ps1"
    outputFile        = Join-Path $scriptDir "LazyPin.exe"
    iconFile          = Join-Path $scriptDir "LazyPin.ico"
    title             = "LazyPin"
    description       = "LazyPin - Always on top title bar pin button by Raisul Sohan"
    company           = "Raisul Sohan"
    product           = "LazyPin"
    copyright         = "Copyright (c) 2026 Raisul Sohan. All rights reserved."
    version           = "1.0.4.0"
    noConsole         = $true
    sta               = $true
    x64               = $true
    winFormsDPIAware  = $true
}

Invoke-ps2exe @ps2exeParams

if (-not (Test-Path (Join-Path $scriptDir "LazyPin.exe"))) {
    throw "Failed to produce LazyPin.exe"
}
Write-Host "LazyPin.exe built successfully!" -ForegroundColor Green

# 2. Compile LazyPinSetup.iss -> LazyPinSetup.exe using Inno Setup
Write-Host "`n[2/2] Compiling Inno Setup Installer (LazyPinSetup.exe)..." -ForegroundColor Yellow

$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\InnoSetup\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Users\iamra\AppData\Local\Temp\codex-innosetup-6.7.3\ISCC.exe"
)

$isccPath = $null
foreach ($path in $isccCandidates) {
    if (Test-Path $path) {
        $isccPath = $path
        break
    }
}

if (-not $isccPath) {
    $whereISCC = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($whereISCC) {
        $isccPath = $whereISCC.Source
    }
}

if (-not $isccPath) {
    throw "Inno Setup Compiler (ISCC.exe) not found."
}

Write-Host "Using ISCC: $isccPath" -ForegroundColor DarkGray
& $isccPath (Join-Path $scriptDir "LazyPinSetup.iss")

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  SUCCESS! LazyPinSetup.exe is ready!   " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
