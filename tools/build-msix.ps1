#requires -Version 5.1
<#
.SYNOPSIS
    Microsoft Store 向けの MSIX パッケージを作る。

.DESCRIPTION
    手順:
      1. アプリをランタイム同梱で発行する（単一ファイルにはしない。MSIX 自体がフォルダを持つので不要で、
         単一ファイルだと起動のたびにネイティブ DLL を一時フォルダへ展開する分だけ遅くなる）
      2. 右クリックメニューの部品（tools/build-shellext.ps1）とロゴ類を入れる
      3. アプリの翻訳（Strings.*.resx）から、種類名とアプリの説明だけを抜き出して resources.pri を作る
      4. makeappx でパッケージにまとめる。両方の CPU 向けを作ったら .msixbundle にもまとめる

    ストアに出すときは、Partner Center の「製品 ID」ページにある Package/Identity/Name と
    Publisher を -IdentityName と -Publisher に渡す。ストアが署名するので、ここでは署名しない。

    手元で試すときは -Register を付ける。開発者モードが有効なら、署名なしのまま
    発行フォルダをそのまま登録できる（Add-AppxPackage -Register）。

.EXAMPLE
    ./tools/build-msix.ps1 -Architectures x64 -Register
    ./tools/build-msix.ps1 -IdentityName 12345Smarciva.smarcivaZIP -Publisher "CN=XXXXXXXX-..." -Version 1.1.0.0
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string[]]$Architectures = @('x64', 'arm64'),

    # 4 つの数字。最後はストアの決まりで 0 にする。省略時は Directory.Build.props の Version に .0 を足す。
    [string]$Version,

    # 手元で試すための仮の値。ストアに出すときは Partner Center の値に置き換える。
    [string]$IdentityName = 'smarciva.smarcivaZIP.Dev',
    [string]$Publisher = 'CN=smarcivaZIP Development',
    [string]$PublisherDisplayName = 'smarciva',

    # 発行済みのフォルダを使い回す（素材やマニフェストだけ直したとき用）。
    [switch]$SkipPublish,

    # できあがった x64 のフォルダを、この PC に仮登録する（開発者モードが必要）。
    [switch]$Register
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src/SmarcivaZip.App/SmarcivaZip.App.csproj'
$resources = Join-Path $repoRoot 'src/SmarcivaZip.Core/Resources'
$packaging = Join-Path $repoRoot 'packaging/msix'
$outRoot = Join-Path $repoRoot 'artifacts/msix'

if (-not $Version) {
    $baseVersion = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
    $Version = "$baseVersion.0"
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version must look like 1.2.3.0 (got '$Version')." }

# --- Windows SDK の道具 -------------------------------------------------------------

function Find-SdkTool([string]$name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $found = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) { throw "$name was not found. Install the Windows 10/11 SDK." }
    return $found
}

$makeappx = Find-SdkTool 'makeappx.exe'
$makepri = Find-SdkTool 'makepri.exe'

# --- 翻訳 --------------------------------------------------------------------------

# アプリの言語コード → パッケージの言語。英語を既定にする。
$languageMap = [ordered]@{
    '' = 'en-US'; 'ja' = 'ja'; 'zh-Hans' = 'zh-Hans'; 'zh-Hant' = 'zh-Hant'; 'ko' = 'ko'
    'es' = 'es'; 'pt' = 'pt'; 'fr' = 'fr'; 'de' = 'de'; 'ru' = 'ru'; 'ar' = 'ar'; 'id' = 'id'
    'tr' = 'tr'; 'it' = 'it'; 'vi' = 'vi'; 'pl' = 'pl'; 'th' = 'th'; 'nl' = 'nl'; 'uk' = 'uk'; 'hi' = 'hi'
}

# マニフェストから ms-resource: で引くキー。
$manifestKeys = @('About_Description', 'FileType_Zip', 'FileType_SevenZip', 'FileType_Rar', 'FileType_Tar',
                  'FileType_Compressed', 'FileType_Lzh', 'FileType_Archive')

function Write-Resw([string]$resxPath, [string]$reswPath) {
    [xml]$resx = Get-Content -Raw -Encoding UTF8 $resxPath
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    New-Item -ItemType Directory -Force -Path (Split-Path $reswPath) | Out-Null
    $writer = [System.Xml.XmlWriter]::Create($reswPath, $settings)
    $writer.WriteStartElement('root')
    foreach ($key in $manifestKeys) {
        $node = $resx.root.data | Where-Object { $_.name -eq $key } | Select-Object -First 1
        if (-not $node) { throw "$key is missing from $resxPath" }
        $writer.WriteStartElement('data')
        $writer.WriteAttributeString('name', $key)
        $writer.WriteAttributeString('xml', 'space', 'http://www.w3.org/XML/1998/namespace', 'preserve')
        $writer.WriteElementString('value', $node.value)
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
    $writer.Close()
}

# --- パッケージごと ----------------------------------------------------------------

$packages = @()

foreach ($arch in $Architectures) {
    $runtime = "win-$arch"
    $work = Join-Path $outRoot $arch
    $layout = Join-Path $work 'layout'

    Write-Host "=== MSIX $Version ($arch) ==="

    $shellExt = Join-Path $repoRoot "artifacts/shellext/$arch/SmarcivaZip.ShellExt.dll"
    & (Join-Path $PSScriptRoot 'build-shellext.ps1') -Architectures $arch
    if (-not (Test-Path $shellExt)) {
        Write-Warning "SmarcivaZip.ShellExt.dll for $arch is missing; skipping this architecture."
        continue
    }

    if (-not $SkipPublish -or -not (Test-Path (Join-Path $layout 'SmarcivaZip.exe'))) {
        if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
        & dotnet publish $appProject --nologo -c Release -r $runtime --self-contained true `
            -p:PublishSingleFile=false -p:DebugType=none -o $layout
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime." }
    }

    Copy-Item $shellExt $layout -Force
    Copy-Item (Join-Path $packaging 'Assets') $layout -Recurse -Force
    foreach ($doc in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'PRIVACY.md') {
        Copy-Item (Join-Path $repoRoot $doc) $layout -Force
    }

    # マニフェスト
    $languageLines = ($languageMap.Values | ForEach-Object { "    <Resource Language=`"$_`" />" }) -join "`r`n"
    $manifest = (Get-Content -Raw -Encoding UTF8 (Join-Path $packaging 'AppxManifest.xml')).
        Replace('${IdentityName}', $IdentityName).
        Replace('${Publisher}', [System.Security.SecurityElement]::Escape($Publisher)).
        Replace('${PublisherDisplayName}', [System.Security.SecurityElement]::Escape($PublisherDisplayName)).
        Replace('${Version}', $Version).
        Replace('${Architecture}', $arch).
        Replace('${ResourceLanguages}', $languageLines)
    [System.IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))

    # resources.pri。翻訳は一時的に Strings\<言語>\Resources.resw として置き、pri に取り込んだら消す。
    $strings = Join-Path $layout 'Strings'
    foreach ($entry in $languageMap.GetEnumerator()) {
        $resx = if ($entry.Key -eq '') { 'Strings.resx' } else { "Strings.$($entry.Key).resx" }
        Write-Resw (Join-Path $resources $resx) (Join-Path $strings "$($entry.Value)\Resources.resw")
    }

    $priConfig = Join-Path $work 'priconfig.xml'
    & $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig failed.' }
    & $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makepri new failed.' }
    Remove-Item $strings -Recurse -Force

    $package = Join-Path $outRoot "smarcivaZIP-$Version-$arch.msix"
    & $makeappx pack /o /h SHA256 /d $layout /p $package | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed for $arch." }

    $sizeMb = [math]::Round((Get-Item $package).Length / 1MB, 1)
    Write-Host "  -> $(Split-Path -Leaf $package) ($sizeMb MB)"
    $packages += $package
}

# 両方の CPU 向けがそろったら、ストアに出しやすい .msixbundle にまとめる。
if ($packages.Count -gt 1) {
    $bundleDir = Join-Path $outRoot 'bundle'
    if (Test-Path $bundleDir) { Remove-Item $bundleDir -Recurse -Force }
    New-Item -ItemType Directory -Path $bundleDir | Out-Null
    $packages | ForEach-Object { Copy-Item $_ $bundleDir }

    $bundle = Join-Path $outRoot "smarcivaZIP-$Version.msixbundle"
    & $makeappx bundle /o /bv $Version /d $bundleDir /p $bundle | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makeappx bundle failed.' }
    Write-Host "  -> $(Split-Path -Leaf $bundle)"
}

if ($Register) {
    $layout = Join-Path $outRoot 'x64/layout/AppxManifest.xml'
    if (-not (Test-Path $layout)) { throw 'The x64 layout was not built, so there is nothing to register.' }
    Write-Host '=== Registering the x64 layout for this user (Developer Mode) ==='
    Add-AppxPackage -Register $layout -ForceApplicationShutdown
    Get-AppxPackage -Name $IdentityName | Select-Object Name, Version, PackageFamilyName, InstallLocation | Format-List
}
