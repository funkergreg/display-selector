#requires -Version 5.1
<#
.SYNOPSIS
    Build, test, publish, and package the Display Selector installer.
.DESCRIPTION
    Pipeline: unit tests -> publish (self-contained single-file win-x64) -> Inno Setup compile.
.PARAMETER IncludeIntegration
    Also run the Category=Integration tests (real Windows APIs; needs a desktop session).
.PARAMETER SkipTests
    Skip all tests and go straight to publish + package.
#>
[CmdletBinding()]
param(
    [switch]$IncludeIntegration,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'src/DisplaySelector'
$tests = Join-Path $root 'tests/DisplaySelector.Tests'
$publishDir = Join-Path $root 'publish'
$iss = Join-Path $root 'installer/setup.iss'

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed (exit $LASTEXITCODE)."
    }
}

if (-not $SkipTests) {
    Invoke-Step 'Unit tests' {
        dotnet test $tests -c Release --filter 'Category!=Integration'
    }
    if ($IncludeIntegration) {
        Invoke-Step 'Integration tests' {
            dotnet test $tests -c Release --filter 'Category=Integration'
        }
    }
}

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Invoke-Step 'Publish' {
    dotnet publish $app -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -o $publishDir
}

# Inno Setup is optional locally; warn rather than fail if the compiler is absent.
# Prefer the stable v6, then fall back to any installed edition (e.g. 7.x), then PATH.
$roots = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) | Where-Object { $_ }
$iscc = $roots | ForEach-Object { Join-Path $_ 'Inno Setup 6\ISCC.exe' } |
    Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $iscc = $roots |
        ForEach-Object { Get-ChildItem -Path (Join-Path $_ 'Inno Setup *\ISCC.exe') -ErrorAction SilentlyContinue } |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}

if (-not $iscc) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if ($iscc) {
    Invoke-Step 'Package installer' {
        & $iscc $iss
    }
    $installer = Join-Path $root 'installer/Output/DisplaySelectorSetup.exe'
    if (Test-Path $installer) {
        Write-Host "Installer: $installer" -ForegroundColor Green
    }
}
else {
    Write-Warning 'Inno Setup (ISCC.exe) not found; skipped packaging. Published app is in /publish.'
}
