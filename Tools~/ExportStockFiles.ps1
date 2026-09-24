<#
.SYNOPSIS
    Exports the stock GD SDK files the Ads Integration tool edits, one copy per unique file,
    for every GD SDK v5 release.

.DESCRIPTION
    Reads each non-beta v5.x.y tag of Monetization-SDK-Unity and writes, into
    Editor/Ads/Stock/ of this package:

      Files/<hash>.txt   each unique file once (normalized: LF line endings, no trailing
                         whitespace), stored as .txt so Unity never compiles it
      manifest.json      version -> reported version string -> path -> stored file

    A file that does not exist in a version is recorded with "file": null.
    Stable .meta files are written alongside, since a git-installed package is read-only
    and Unity ignores any asset without one. Stored files nothing references any more are
    removed.

    Adding a new GD SDK release: tag it in Monetization-SDK-Unity, then re-run this script.

.PARAMETER SdkRepo
    Path to the Monetization-SDK-Unity checkout. Only its tags are read; the working tree
    is never touched.

.PARAMETER Tags
    Optional explicit tag list. Default: every v5.x.y tag without a pre-release suffix.
#>
param(
    [string]$SdkRepo = (Join-Path $PSScriptRoot "..\..\Monetization-SDK-Unity"),
    [string[]]$Tags
)

$ErrorActionPreference = 'Stop'

# Every GD SDK file the tool edits, relative to the GD SDK root, plus the file the SDK
# version is read from. Keep in step with the steps that call SourcePatcher on GD SDK files.
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
    'Runtime/Scripts/Configurations/SDKConfiguration.cs',       # Remote Metica switch
    'Runtime/Scripts/Ads/Core/IAdNetworkService.cs',            # Remove the unused async init path
    'Runtime/Scripts/Ads/Core/AdNetworkController.cs',
    'Editor/Scripts/MenuItems/MonetizationRemover.cs',          # Finish up
    'Runtime/Scripts/MonetizationInitializeOnLoad.cs'           # version source, never patched
)

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

# Raw file bytes at a tag, or $null when the file is not in that release. Read as bytes
# so PowerShell's console encoding cannot mangle non-ASCII characters.
function Get-BlobBytes([string]$Tag, [string]$RepoPath) {
    $result = Invoke-Git @('cat-file', 'blob', "`"${Tag}:$RepoPath`"")
    if ($result.Code -ne 0) { return $null }
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
    $Tags = (Get-GitText @('tag', '--list', '"v5.*"')).Split("`n") |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^v5\.\d+\.\d+$' }
}
$Tags = $Tags | Sort-Object { ConvertTo-VersionKey $_.TrimStart('v') }
if (-not $Tags) { throw "No v5 release tags found in $SdkRepo" }

New-Item -ItemType Directory -Force $FilesRoot | Out-Null

$versions = @()
$stored = @{}   # file name -> normalized text

foreach ($tag in $Tags) {
    # Find the GD SDK root inside the repo at this tag, the same way the tool finds it in a
    # project: by the folder that holds Runtime/Scripts/Ads/Core/AdsManager.cs.
    $tree = (Get-GitText @('ls-tree', '-r', '--name-only', $tag)).Split("`n")
    $marker = $tree | Where-Object { $_ -like '*/Runtime/Scripts/Ads/Core/AdsManager.cs' } | Select-Object -First 1
    if (-not $marker) { throw "${tag}: GD SDK root not found" }
    $sdkRoot = $marker.Substring(0, $marker.Length - '/Runtime/Scripts/Ads/Core/AdsManager.cs'.Length)

    $files = @()
    $reports = $null

    foreach ($path in $Paths) {
        $bytes = Get-BlobBytes $tag "$sdkRoot/$path"
        if ($null -eq $bytes) {
            $files += [ordered]@{ path = $path; file = $null }
            continue
        }

        $text = ConvertTo-Normalized $bytes
        $name = (Get-Hash $text).Substring(0, 16) + '.txt'
        $stored[$name] = $text
        $files += [ordered]@{ path = $path; file = $name }

        if ($path -eq $VersionFile -and $text -match 'Version\s*=\s*"([^"]+)"') { $reports = $Matches[1] }
    }

    if (-not $reports) { throw "${tag}: could not read the Version string from $VersionFile" }

    $versions += [ordered]@{ version = $tag.TrimStart('v'); tag = $tag; reports = $reports; files = $files }
    Write-Host ("{0,-8} reports {1,-12} {2} of {3} files present" -f $tag, "`"$reports`"",
        @($files | Where-Object { $_.file }).Count, $Paths.Count)
}

# Two releases reporting the same string would make the version lookup ambiguous.
$versions | Group-Object { $_.reports } | Where-Object Count -gt 1 | ForEach-Object {
    Write-Warning ("Reported version `"{0}`" is shared by {1} - the tool will ask which one." -f
        $_.Name, (($_.Group | ForEach-Object { $_.tag }) -join ', '))
}

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
    note = 'Generated by Tools~/ExportStockFiles.ps1 - do not edit by hand. Paths are relative to the GD SDK root; file null = not in that release.'
    paths = $Paths
    versions = $versions
}
$manifestPath = Join-Path $StockRoot 'manifest.json'
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6) + "`n", $Utf8)

Write-Meta $manifestPath 'manifest.json'
Write-Meta $StockRoot 'folder:Stock' $true
Write-Meta $FilesRoot 'folder:Files' $true

Write-Host ""
Write-Host "$($versions.Count) versions, $($stored.Count) unique files -> $((Resolve-Path $StockRoot).Path)"
foreach ($path in $Paths) {
    $variants = @($versions | ForEach-Object { ($_.files | Where-Object { $_.path -eq $path }).file } | Where-Object { $_ } | Sort-Object -Unique).Count
    $absent = @($versions | Where-Object { -not ($_.files | Where-Object { $_.path -eq $path }).file }).Count
    Write-Host ("  {0,-58} {1} variant(s){2}" -f $path, $variants, $(if ($absent) { ", missing in $absent" } else { '' }))
}
