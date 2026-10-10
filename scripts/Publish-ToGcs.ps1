# Copies a verified, published release zip to gs://<bucket>/revit/. Called only by publish-to-gcs.yml.
#
# Order matters. Users only ever see the alias and latest.json, so they move last:
#   1. revit/releases/<v>/HVAKR-Revit-Plugin-<v>.zip  immutable, never overwritten
#   2. revit/HVAKR-Revit-Plugin.zip                   stable alias, server-side copy of (1)
#   3. revit/latest.json                              points at (1)
# Each step is verified over public HTTPS before the next one starts. A rerun is safe:
# an identical immutable object is accepted, and the alias and manifest are rewritten with the same bytes.
param(
    [Parameter(Mandatory = $true)]
    [string]$Zip,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PublishedAt,

    [Parameter(Mandatory = $true)]
    [string]$ReleaseUrl,

    [string]$Bucket = "hvakr-desktop-releases",

    [string]$PublicBaseUrl = "https://storage.googleapis.com"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$public = "$PublicBaseUrl/$Bucket"
$immutable = "revit/releases/$Version/HVAKR-Revit-Plugin-$Version.zip"
$alias = "revit/HVAKR-Revit-Plugin.zip"
$manifestObject = "revit/latest.json"
$hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
$work = Join-Path ([System.IO.Path]::GetTempPath()) "HVAKR.Publish.$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $work | Out-Null

# This workflow owns revit/ only. Never write anywhere else in the bucket (desktop/ belongs to another app).
function Get-RevitUri([string]$Object) {
    if (!$Object.StartsWith("revit/")) { throw "Refusing to write outside revit/: $Object" }
    "gs://$Bucket/$Object"
}

# SHA256 of the public object, or $null if it does not exist.
function Get-PublicHash([string]$Object) {
    $path = Join-Path $work ([guid]::NewGuid().ToString("N"))
    try {
        Invoke-WebRequest "$public/$Object" -OutFile $path -Headers @{ "Cache-Control" = "no-cache" }
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) { return $null }
        throw
    }
    (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-PublicManifest {
    try {
        Invoke-RestMethod "$public/$manifestObject" -Headers @{ "Cache-Control" = "no-cache" }
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) { return $null }
        throw
    }
}

try {
    # 1. Immutable versioned zip.
    $existing = Get-PublicHash $immutable
    if ($null -eq $existing) {
        gcloud storage cp $Zip (Get-RevitUri $immutable) --if-generation-match=0 --cache-control="public, max-age=31536000, immutable" --content-type=application/zip
    } elseif ($existing -ne $hash) {
        throw "$immutable already exists with SHA256 $existing, not $hash. Immutable objects are never overwritten."
    } else {
        Write-Host "$immutable already published with the same SHA256."
    }
    if ((Get-PublicHash $immutable) -ne $hash) { throw "Public $immutable does not match SHA256 $hash." }

    # Publishing an older patch after a newer release must not roll users back.
    $current = Get-PublicManifest
    if ($current -and [version]$current.version -gt [version]$Version) {
        Write-Host "::notice::latest.json is at $($current.version), newer than $Version. Leaving the alias and latest.json alone."
        return
    }

    # 2. Stable alias, copied server-side from the verified immutable object.
    gcloud storage cp (Get-RevitUri $immutable) (Get-RevitUri $alias) --cache-control="no-cache" --content-type=application/zip
    if ((Get-PublicHash $alias) -ne $hash) { throw "Public $alias does not match SHA256 $hash." }

    # 3. latest.json last. Schema 1 is what HVAKR.Api.Updates.UpdateManifest validates.
    $manifest = [ordered]@{
        schemaVersion = 1
        version = $Version
        publishedAt = $PublishedAt
        installer = [ordered]@{
            url = "$public/$immutable"
            sha256 = $hash
            size = (Get-Item $Zip).Length
        }
        releaseNotesUrl = $ReleaseUrl
    }
    $manifestPath = Join-Path $work "latest.json"
    $manifest | ConvertTo-Json -Depth 4 | Set-Content $manifestPath -Encoding utf8NoBOM
    gcloud storage cp $manifestPath (Get-RevitUri $manifestObject) --cache-control="no-cache" --content-type=application/json

    $published = Get-PublicManifest
    if ($published.version -ne $Version) { throw "Public latest.json version is $($published.version), expected $Version." }
    if ($published.installer.sha256 -ne $hash) { throw "Public latest.json SHA256 does not match." }
    if ($published.installer.url -ne "$public/$immutable") { throw "Public latest.json URL is $($published.installer.url)." }

    Write-Host "Published $Version. SHA256 $hash"
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
