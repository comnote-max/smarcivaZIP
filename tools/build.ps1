#requires -Version 5.1
<#
.SYNOPSIS
    smarcivaZIP を配布用に発行し、ポータブル ZIP を作る。

.DESCRIPTION
    ランタイム同梱の単一 exe として発行する。Lhaplus のように
    「落として解凍したらすぐ使える」ことを優先しているため、
    .NET ランタイムの別途インストールは不要。

    7z.dll だけは exe に埋め込まず隣に置く。LGPL-2.1 が求める
    「利用者がライブラリを差し替えられること」を満たすため。

.EXAMPLE
    ./tools/build.ps1
    ./tools/build.ps1 -Runtimes win-x64 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]]$Runtimes = @('win-x64', 'win-arm64'),

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # ランタイム同梱版に加えて、.NET ランタイムを別途入れてもらう軽量版も作る。
    # 同梱版は 57 MB 前後あり、Lhaplus の後継としては重い。
    [switch]$SkipFrameworkDependent,

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repoRoot 'dist'
$appProject = Join-Path $repoRoot 'src/SmarcivaZip.App/SmarcivaZip.App.csproj'
$testProject = Join-Path $repoRoot 'src/SmarcivaZip.Tests/SmarcivaZip.Tests.csproj'

# 7z.dll が無いと何もできないので先に確認する。
$missing = $Runtimes | Where-Object { -not (Test-Path (Join-Path $repoRoot "native/$_/7z.dll")) }
if ($missing) {
    Write-Host "Fetching 7z.dll for: $($missing -join ', ')"
    & (Join-Path $PSScriptRoot 'fetch-7zip.ps1') -Architectures ($missing | ForEach-Object { $_ -replace '^win-', '' })
}

if (-not $SkipTests) {
    Write-Host '=== Running tests ==='
    & dotnet test $testProject --nologo -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if (Test-Path $distRoot) { Remove-Item $distRoot -Recurse -Force }
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

$version = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props'))).Project.PropertyGroup.Version

function Publish-Variant {
    param(
        [string]$Runtime,
        [bool]$SelfContained,
        [string]$Suffix
    )

    $publishDir = Join-Path $distRoot "$Runtime$Suffix"

    $arguments = @(
        'publish', $appProject,
        '--nologo',
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', $SelfContained.ToString().ToLowerInvariant(),
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=none',
        '-o', $publishDir
    )

    # 単一ファイル内の圧縮は自己完結版でしか効果が無い。
    if ($SelfContained) { $arguments += '-p:EnableCompressionInSingleFile=true' }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Runtime$Suffix." }

    # 配布物にライセンス文書を入れる（MIT と LGPL の両方を明示する義務がある）。
    Copy-Item (Join-Path $repoRoot 'LICENSE') $publishDir -Force
    Copy-Item (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') $publishDir -Force
    Copy-Item (Join-Path $repoRoot 'README.md') $publishDir -Force

    $archive = Join-Path $distRoot "smarcivaZIP-$version-$Runtime$Suffix.zip"
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $archive -Force

    $sizeMb = [math]::Round((Get-Item $archive).Length / 1MB, 1)
    Write-Host "  -> $(Split-Path -Leaf $archive) ($sizeMb MB)"
}

foreach ($runtime in $Runtimes) {
    Write-Host "=== Publishing $runtime (self-contained) ==="
    Publish-Variant -Runtime $runtime -SelfContained $true -Suffix ''

    if (-not $SkipFrameworkDependent) {
        Write-Host "=== Publishing $runtime (requires .NET Desktop Runtime) ==="
        Publish-Variant -Runtime $runtime -SelfContained $false -Suffix '-runtime-required'
    }
}

Write-Host ''
Write-Host "Done. Artifacts are in $distRoot"
