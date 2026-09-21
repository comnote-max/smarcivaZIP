#requires -Version 5.1
<#
.SYNOPSIS
    ビルドに必要な 7z.dll を 7-zip.org から取得して native\win-<arch>\ に配置する。

.DESCRIPTION
    smarcivaZIP は 7z.dll を動的リンクで利用する（LGPL-2.1 の要件を満たすため、
    改変も静的リンクもしない）。

    取得元は公式インストーラー (7zXXXX-<arch>.exe)。
    "extra" パッケージにも DLL は入っているが、あちらは 7za.dll という縮小版で
    RAR ハンドラを含まない。smarcivaZIP は RAR5 展開が主要機能なので、
    必ずインストーラー側のフル版 7z.dll を使う。

    取得したバイナリはリポジトリにコミットしない（.gitignore で native/ を除外済み）。
    CI とローカルの両方で、publish の前にこのスクリプトを実行する。

.PARAMETER Version
    取得する 7-Zip のバージョン（例: 26.03）。省略時は download.html から最新を判定する。

.PARAMETER Architectures
    取り出すアーキテクチャ。既定は x64 と arm64。
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('x64', 'arm64', 'x86')]
    [string[]]$Architectures = @('x64', 'arm64')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $repoRoot 'native'
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ('smarcivazip-7z-' + [guid]::NewGuid().ToString('N'))

function Resolve-LatestVersion {
    Write-Host 'Resolving latest 7-Zip version from 7-zip.org...'
    $page = Invoke-WebRequest -Uri 'https://www.7-zip.org/download.html' -UseBasicParsing
    $match = [regex]::Match($page.Content, '7z(?<v>\d{4})-x64\.exe')
    if (-not $match.Success) {
        throw 'Could not determine the latest 7-Zip version. Pass -Version explicitly (e.g. -Version 26.03).'
    }
    $raw = $match.Groups['v'].Value
    return ($raw.Substring(0, 2) + '.' + $raw.Substring(2, 2))
}

function Get-SevenZipExtractor {
    # インストーラーは 7-Zip の SFX なので、取り出すにも 7-Zip が要る。
    # 既にインストールされていればそれを使い、無ければ公式の 7zr.exe を取得する。
    $installed = @(
        (Join-Path $env:ProgramFiles '7-Zip\7z.exe'),
        (Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe')
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    if ($installed) { return $installed }

    Write-Host 'No local 7-Zip found; downloading the standalone extractor (7zr.exe)...'
    $extractorPath = Join-Path $workDir '7zr.exe'
    Invoke-WebRequest -Uri 'https://www.7-zip.org/a/7zr.exe' -OutFile $extractorPath -UseBasicParsing
    return $extractorPath
}

try {
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null

    if (-not $Version) { $Version = Resolve-LatestVersion }
    $compact = $Version -replace '\.', ''
    Write-Host "Using 7-Zip $Version"

    $extractor = Get-SevenZipExtractor

    foreach ($arch in $Architectures) {
        # x86 版だけファイル名にアーキテクチャが入らない。
        $suffix = if ($arch -eq 'x86') { '' } else { "-$arch" }
        $installerUrl = "https://www.7-zip.org/a/7z$compact$suffix.exe"
        $installerPath = Join-Path $workDir "7z$compact$suffix.exe"
        $extractDir = Join-Path $workDir $arch

        Write-Host "Downloading $installerUrl"
        try {
            Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing
        }
        catch {
            Write-Warning "Could not download the $arch installer: $($_.Exception.Message)"
            continue
        }

        & $extractor x $installerPath "-o$extractDir" -y '7z.dll' 'License.txt' | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Failed to extract 7z.dll for $arch."
            continue
        }

        $source = Join-Path $extractDir '7z.dll'
        if (-not (Test-Path $source)) {
            Write-Warning "7z.dll was not present in the $arch installer."
            continue
        }

        $destDir = Join-Path $nativeRoot "win-$arch"
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        Copy-Item $source (Join-Path $destDir '7z.dll') -Force

        # ライセンス文書も配布物に含める（LGPL の要件）。
        $licenseSource = Join-Path $extractDir 'License.txt'
        if (Test-Path $licenseSource) {
            Copy-Item $licenseSource (Join-Path $nativeRoot '7-Zip-License.txt') -Force
        }

        $sizeKb = [math]::Round((Get-Item (Join-Path $destDir '7z.dll')).Length / 1KB)
        Write-Host "  win-$arch <- 7z.dll ($sizeKb KB)"
    }

    Write-Host "Done. 7z.dll placed under $nativeRoot"
}
finally {
    if (Test-Path $workDir) { Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue }
}
