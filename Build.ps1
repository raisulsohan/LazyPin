[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Building Pin to top Executable & Setup " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Compile PinToTop.ps1 -> PinToTop.exe using ps2exe
Write-Host "`n[1/2] Compiling PinToTop.exe..." -ForegroundColor Yellow

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
    inputFile         = Join-Path $scriptDir "PinToTop.ps1"
    outputFile        = Join-Path $scriptDir "PinToTop.exe"
    iconFile          = Join-Path $scriptDir "PinToTop.ico"
    title             = "Pin to top"
    description       = "Pin to top - Always on top title bar pin button by Raisul Sohan"
    company           = "Raisul Sohan"
    product           = "Pin to top"
    copyright         = "Copyright (c) 2026 Raisul Sohan. All rights reserved."
    version           = "1.0.3.0"
    noConsole         = $true
    sta               = $true
    x64               = $true
    winFormsDPIAware  = $true
}

Invoke-ps2exe @ps2exeParams

if (-not (Test-Path (Join-Path $scriptDir "PinToTop.exe"))) {
    throw "Failed to produce PinToTop.exe"
}
Write-Host "PinToTop.exe built successfully!" -ForegroundColor Green

# 2. Compile PinToTopSetup.iss -> PinToTopSetup.exe using Inno Setup
Write-Host "`n[2/2] Compiling Inno Setup Installer (PinToTopSetup.exe)..." -ForegroundColor Yellow

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
& $isccPath (Join-Path $scriptDir "PinToTopSetup.iss")

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host " SUCCESS! PinToTopSetup.exe is ready!   " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
