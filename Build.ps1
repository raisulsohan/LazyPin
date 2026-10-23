[CmdletBinding()]
param(
    # Build only LazyPin.exe, not the installer.
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   Building LazyPin Executable & Setup  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# The version lives in one place: AppInfo.Version in LazyPin.cs.
$source = Join-Path $scriptDir "LazyPin.cs"
$versionMatch = Select-String -LiteralPath $source -Pattern 'public const string Version = "([0-9]+\.[0-9]+\.[0-9]+)"' | Select-Object -First 1
if (-not $versionMatch) {
    throw "AppInfo.Version was not found in LazyPin.cs"
}
$version = $versionMatch.Matches[0].Groups[1].Value
Write-Host "Version: $version" -ForegroundColor DarkGray

# 1. Compile LazyPin.cs -> LazyPin.exe with the C# compiler of the .NET Framework 4.x
#    that is part of every Windows 10 and 11 installation.
Write-Host "`n[1/2] Compiling LazyPin.exe..." -ForegroundColor Yellow

$cscCandidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw "csc.exe of the .NET Framework 4.x was not found under $env:WINDIR\Microsoft.NET."
}
Write-Host "Using csc: $csc" -ForegroundColor DarkGray

$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/warn:4",
    "/warnaserror+",
    "/utf8output",
    "/codepage:65001",
    "/win32icon:LazyPin.ico",
    "/win32manifest:LazyPin.manifest",
    "/resource:LazyPin.ico,LazyPin.ico",
    "/reference:System.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/out:LazyPin.exe",
    "LazyPin.cs"
)
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "csc.exe failed with exit code $LASTEXITCODE"
}
Write-Host "LazyPin.exe built successfully!" -ForegroundColor Green

if ($SkipInstaller) {
    return
}

# 2. Compile LazyPinSetup.iss -> LazyPinSetup.exe using Inno Setup 6.
Write-Host "`n[2/2] Compiling Inno Setup Installer (LazyPinSetup.exe)..." -ForegroundColor Yellow

$isccCandidates = @(
    $env:ISCC,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\InnoSetup\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$isccPath = $isccCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $isccPath) {
    $whereISCC = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($whereISCC) {
        $isccPath = $whereISCC.Source
    }
}

if (-not $isccPath) {
    throw "Inno Setup Compiler (ISCC.exe) not found. Install Inno Setup 6 or set the ISCC environment variable to ISCC.exe."
}

Write-Host "Using ISCC: $isccPath" -ForegroundColor DarkGray
& $isccPath "/DMyAppVersion=$version" (Join-Path $scriptDir "LazyPinSetup.iss")

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  SUCCESS! LazyPinSetup.exe is ready!   " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
