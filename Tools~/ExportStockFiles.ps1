<#
.SYNOPSIS
    Exports every stock GD SDK code file, one copy per unique file, for every GD SDK v5 and
    v6 release.

.DESCRIPTION
    Reads each non-beta v5.x.y and v6.x.y tag of Monetization-SDK-Unity and writes, into
    Editor/Ads/Stock/ of this package:

      Files/<hash>.txt   each unique file once (normalized: LF line endings, no trailing
                         whitespace), stored as .txt so Unity never compiles it
      manifest.json      the releases, the files the tool patches, and for every path
                         which stored file each release has

    Covered: the code and text files (see $Extensions) under the GD SDK root and the
    Monetization services folder next to it. Assets, prefabs and scenes are left out: they
    hold each game's own ids and settings, so they always differ and say nothing.

    Paths are relative to the GD SDK root; the services folder's are "../Monetization/...".
    A path a release does not have is simply not listed for it.

    Stable .meta files are written alongside, since a git-installed package is read-only
    and Unity ignores any asset without one. Stored files nothing references any more are
    removed.

    Adding a new GD SDK release: tag it in Monetization-SDK-Unity, then re-run this script.

.PARAMETER SdkRepo
    Path to the Monetization-SDK-Unity checkout. Only its tags are read; the working tree
    is never touched.

.PARAMETER Tags
    Optional explicit tag list. Default: every v5.x.y and v6.x.y tag without a suffix.
#>
param(
    [string]$SdkRepo = (Join-Path $PSScriptRoot "..\..\Monetization-SDK-Unity"),
    [string[]]$Tags
)

$ErrorActionPreference = 'Stop'

# The GD SDK files the tool edits, relative to the GD SDK root, plus the file the SDK
# version is read from. Keep in step with the steps that call SourcePatcher on GD SDK files.
# The modified-file check and the patch harness use this list; the compare window uses all.
$Paths = @(
    'Runtime/Scripts/Ads/AdPlatforms.cs',                       # Patch the existing SDK files
    'Runtime/Scripts/Logger/Tag.cs',
    'Runtime/Scripts/MonetizationConfigurationsPath.cs',
    'Runtime/Scripts/Ads/AdRevenueInfo.cs',
    'Runtime/Scripts/Configurations/AdUnitsConfiguration.cs',
    'Runtime/Scripts/MonetizationPreferences.cs',
    'Runtime/Scripts/Configurations/RemoteConfiguration.cs',
    'Runtime/Scripts/Remote/RemoteConfigManager.cs',
    'Runtime/Scripts/Ads/Core/AdsManager.cs',
    'Runtime/Scripts/Analytics/AnalyticsManager.cs',
    'Runtime/Scripts/Analytics/Services/AdjustAnalyticsNetwork.cs',
    'Runtime/Scripts/Consent/Core/ConsentManager.cs',                # 5.0 - 5.2: Metica consent in the fixed list
    'Runtime/Scripts/Configurations/SDKConfiguration.cs',       # Remote Metica switch
    'Runtime/Scripts/Ads/Core/IAdNetworkService.cs',            # Remove the unused async init path
    'Runtime/Scripts/Ads/Core/AdNetworkController.cs',
    'Editor/Scripts/MenuItems/MonetizationRemover.cs',          # Finish up
    'Runtime/Scripts/MonetizationInitializeOnLoad.cs'           # version source, never patched
)

# Code and text files. Keep in step with StockFiles.Extensions in the tool.
$Extensions = @('.cs', '.asmdef', '.asmref', '.java', '.kt', '.xml', '.json', '.gradle', '.m', '.mm', '.h', '.txt', '.md')

$VersionFile = 'Runtime/Scripts/MonetizationInitializeOnLoad.cs'
$SdkRepo = (Resolve-Path $SdkRepo).Path
$StockRoot = Join-Path $PSScriptRoot "..\Editor\Ads\Stock"
$FilesRoot = Join-Path $StockRoot "Files"
$Utf8 = New-Object System.Text.UTF8Encoding($false)

# -- git helpers -----------------------------------------------------------------

function Invoke-Git([string[]]$Arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'git'
    $info.Arguments = '-C "' + $SdkRepo + '" ' + ($Arguments -join ' ')
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($info)
    $buffer = New-Object System.IO.MemoryStream
    $process.StandardOutput.BaseStream.CopyTo($buffer)
    $null = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    return @{ Code = $process.ExitCode; Bytes = $buffer.ToArray() }
}

function Get-GitText([string[]]$Arguments) {
    $result = Invoke-Git $Arguments
    if ($result.Code -ne 0) { return $null }
    return $Utf8.GetString($result.Bytes)
}

# Raw blob bytes by object id. Read as bytes so PowerShell's console encoding cannot mangle
# non-ASCII characters.
function Get-BlobBytes([string]$Sha) {
    $result = Invoke-Git @('cat-file', 'blob', $Sha)
    if ($result.Code -ne 0) { throw "git cat-file failed for $Sha" }
    return $result.Bytes
}

# -- normalizing and hashing -----------------------------------------------------

# Same rules the tool uses when comparing: no BOM, LF line endings, no trailing whitespace,
# exactly one newline at the end. So git autocrlf or an editor's trailing spaces never
# count as a change.
function ConvertTo-Normalized([byte[]]$Bytes) {
    $text = $Utf8.GetString($Bytes).TrimStart([char]0xFEFF)
    $lines = $text.Replace("`r`n", "`n").Replace("`r", "`n").Split("`n") | ForEach-Object { $_.TrimEnd(' ', "`t") }
    return (($lines -join "`n").TrimEnd("`n")) + "`n"
}

function Get-Hash([string]$Text, [string]$Algorithm = 'SHA256') {
    $hasher = [System.Security.Cryptography.HashAlgorithm]::Create($Algorithm)
    $bytes = $hasher.ComputeHash($Utf8.GetBytes($Text))
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

# A GUID derived from the asset's name, so re-running the script never churns .meta files.
function Write-Meta([string]$AssetPath, [string]$Seed, [bool]$Folder = $false) {
    $guid = (Get-Hash "metica-stock/$Seed" 'MD5')
    $body = if ($Folder) {
        "folderAsset: yes`nDefaultImporter:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
    } else {
        "TextScriptImporter:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
    }
    [System.IO.File]::WriteAllText("$AssetPath.meta", "fileFormatVersion: 2`nguid: $guid`n$body", $Utf8)
}

function ConvertTo-VersionKey([string]$Version) {
    $parts = $Version.Split('.') | ForEach-Object { [int]$_ }
    return ($parts | ForEach-Object { $_.ToString('D4') }) -join '.'
}

# -- export ----------------------------------------------------------------------

if (-not $Tags) {
    $Tags = (Get-GitText @('tag', '--list')).Split("`n") |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^v[56]\.\d+\.\d+$' }
}
$Tags = $Tags | Sort-Object { ConvertTo-VersionKey $_.TrimStart('v') }
if (-not $Tags) { throw "No v5/v6 release tags found in $SdkRepo" }

New-Item -ItemType Directory -Force $FilesRoot | Out-Null

$versions = @()
$stored = @{}      # stored file name -> normalized text
$byBlob = @{}      # git blob id -> stored file name, so each unique blob is read once
$files = [ordered]@{}   # path -> stored file name -> versions that have it

foreach ($tag in $Tags) {
    $version = $tag.TrimStart('v')

    # "<mode> <type> <id>`t<path>", NUL-separated so unusual paths come through as they are.
    $entries = (Get-GitText @('ls-tree', '-r', '-z', $tag)).Split([char]0) | Where-Object { $_ }

    # The GD SDK root, found the same way the tool finds it in a project: by the folder that
    # holds Runtime/Scripts/Ads/Core/AdsManager.cs. The services folder sits next to it.
    $tail = '/Runtime/Scripts/Ads/Core/AdsManager.cs'
    $marker = $entries | ForEach-Object { $_.Split("`t")[1] } | Where-Object { $_.EndsWith($tail) } | Select-Object -First 1
    if (-not $marker) { throw "${tag}: GD SDK root not found" }
    $sdkRoot = $marker.Substring(0, $marker.Length - $tail.Length)
    $servicesRoot = (Split-Path $sdkRoot -Parent).Replace('\', '/') + '/Monetization'

    $count = 0
    $reports = $null

    foreach ($entry in $entries) {
        $head, $path = $entry.Split("`t", 2)
        $kind, $sha = $head.Split(' ')[1, 2]
        if ($kind -ne 'blob') { continue }
        if ($Extensions -notcontains [System.IO.Path]::GetExtension($path).ToLowerInvariant()) { continue }

        if ($path.StartsWith("$sdkRoot/")) { $relative = $path.Substring($sdkRoot.Length + 1) }
        elseif ($path.StartsWith("$servicesRoot/")) { $relative = '../Monetization/' + $path.Substring($servicesRoot.Length + 1) }
        else { continue }

        if (-not $byBlob.ContainsKey($sha)) {
            $text = ConvertTo-Normalized (Get-BlobBytes $sha)
            $name = (Get-Hash $text).Substring(0, 16) + '.txt'
            $stored[$name] = $text
            $byBlob[$sha] = $name
        }
        $name = $byBlob[$sha]

        if (-not $files.Contains($relative)) { $files[$relative] = [ordered]@{} }
        if (-not $files[$relative].Contains($name)) { $files[$relative][$name] = New-Object System.Collections.Generic.List[string] }
        $files[$relative][$name].Add($version)
        $count++

        # \b: v6.2.x declares BaseVersion first, which is not the version.
        if ($relative -eq $VersionFile -and $stored[$name] -match '\bVersion\s*=\s*"([^"]+)"') { $reports = $Matches[1] }
    }

    if (-not $reports) { throw "${tag}: could not read the Version string from $VersionFile" }

    $versions += [ordered]@{ version = $version; tag = $tag; reports = $reports }
    Write-Host ("{0,-8} reports {1,-10} {2} files" -f $tag, "`"$reports`"", $count)
}

# Two releases reporting the same string would make the version lookup ambiguous.
$versions | Group-Object { $_.reports } | Where-Object Count -gt 1 | ForEach-Object {
    Write-Warning ("Reported version `"{0}`" is shared by {1}." -f
        $_.Name, (($_.Group | ForEach-Object { $_.tag }) -join ', '))
}

$missingPatched = $Paths | Where-Object { -not $files.Contains($_) }
if ($missingPatched) { throw "Patched paths in no release: $($missingPatched -join ', ')" }

# -- write -----------------------------------------------------------------------

foreach ($name in $stored.Keys) {
    $target = Join-Path $FilesRoot $name
    [System.IO.File]::WriteAllText($target, $stored[$name], $Utf8)
    Write-Meta $target "Files/$name"
}

# Drop stored files no version references any more.
Get-ChildItem $FilesRoot -Filter '*.txt' | Where-Object { -not $stored.ContainsKey($_.Name) } | ForEach-Object {
    Remove-Item $_.FullName, "$($_.FullName).meta" -Force -ErrorAction SilentlyContinue
    Write-Host "removed unused $($_.Name)"
}

$manifest = [ordered]@{
    note = 'Generated by Tools~/ExportStockFiles.ps1 - do not edit by hand. Paths are relative to the GD SDK root ("../Monetization/..." is the services folder next to it); files maps each path to its stored copies and the releases that have each.'
    format = 2
    patched = $Paths
    versions = $versions
    files = $files
}
$manifestPath = Join-Path $StockRoot 'manifest.json'
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6 -Compress) + "`n", $Utf8)

Write-Meta $manifestPath 'manifest.json'
Write-Meta $StockRoot 'folder:Stock' $true
Write-Meta $FilesRoot 'folder:Files' $true

$bytes = ($stored.Values | ForEach-Object { $Utf8.GetByteCount($_) } | Measure-Object -Sum).Sum
Write-Host ""
Write-Host "$($versions.Count) versions, $($files.Count) paths, $($stored.Count) unique files ($([math]::Round($bytes / 1KB)) KB) -> $((Resolve-Path $StockRoot).Path)"
foreach ($path in $Paths) {
    $variants = $files[$path].Count
    $present = ($files[$path].Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
    Write-Host ("  {0,-58} {1} variant(s), in {2} of {3}" -f $path, $variants, $present, $versions.Count)
}
