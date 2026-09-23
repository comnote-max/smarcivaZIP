#requires -Version 5.1
<#
.SYNOPSIS
    ストア版（MSIX）の右クリックメニュー部品 SmarcivaZip.ShellExt.dll をビルドする。

.DESCRIPTION
    Visual Studio（または Build Tools）の C++ ツールを vswhere で探し、cl.exe で直接ビルドする。
    ファイルが 1 つだけの小さな DLL なので、プロジェクトファイルは作らない。

    ARM64 版には「MSVC ARM64 ビルドツール」が要る。無い環境では x64 だけ作る。

.EXAMPLE
    ./tools/build-shellext.ps1 -Architectures x64
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string[]]$Architectures = @('x64', 'arm64'),

    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'src/SmarcivaZip.ShellExt'
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts/shellext' }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe was not found. Install Visual Studio Build Tools with the C++ workload.' }
# vcvarsall.bat も中で vswhere を呼ぶので、PATH から見えるようにしておく。
$env:PATH = (Split-Path $vswhere) + ';' + $env:PATH

foreach ($arch in $Architectures) {
    # x64 の PC から ARM64 向けに作るときはクロスコンパイラ（x64_arm64）を使う。
    $component = if ($arch -eq 'arm64') { 'Microsoft.VisualStudio.Component.VC.Tools.ARM64' } else { 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64' }
    $vsPath = & $vswhere -latest -products * -requires $component -property installationPath | Select-Object -First 1
    if (-not $vsPath) {
        Write-Warning "C++ tools for $arch were not found; skipping. (Visual Studio Installer: '$component')"
        continue
    }

    $vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvarsall.bat'
    $target = if ($arch -eq 'arm64') { 'x64_arm64' } else { 'x64' }
    $outDir = Join-Path $OutputRoot $arch
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $cl = @(
        'cl /nologo /LD /O2 /EHsc /std:c++17 /utf-8 /W4 /permissive- /DUNICODE /D_UNICODE',
        "`"$source\ShellExt.cpp`"",
        "/Fo`"$outDir\\`"",
        "/link /DEF:`"$source\ShellExt.def`" /OUT:`"$outDir\SmarcivaZip.ShellExt.dll`" /IMPLIB:`"$outDir\SmarcivaZip.ShellExt.lib`"",
        'shell32.lib shlwapi.lib ole32.lib user32.lib kernel32.lib'
    ) -join ' '

    Write-Host "=== Building SmarcivaZip.ShellExt.dll ($arch) ==="
    & cmd.exe /d /s /c "`"$vcvars`" $target >nul && $cl"
    if ($LASTEXITCODE -ne 0) { throw "ShellExt build failed for $arch." }

    Remove-Item (Join-Path $outDir '*.obj'), (Join-Path $outDir '*.exp'), (Join-Path $outDir '*.lib') -ErrorAction SilentlyContinue
    Write-Host "  -> $(Join-Path $outDir 'SmarcivaZip.ShellExt.dll')"
}
