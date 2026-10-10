# Builds the release path. CI and release.yml both call this so a green PR exercises
# the same commands as a release, minus Azure login, signing, and the GitHub release.
#
#   -Step Prepare  Release build, tests, and HVAKR.Revit\Deploy\Bundle staging (signing happens after this)
#   -Step Package  Zips the staged bundle, verifies it, and writes the .sha256 next to the zip
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Prepare", "Package")]
    [string]$Step,

    [switch]$RequireSignature
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Version must be strict major.minor.patch."
}

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "HVAKR.Revit/HVAKR.Revit.csproj"
$properties = @(
    "/p:VersionPrefix=$Version",
    "/p:VersionSuffix=",
    "/p:Version=$Version",
    "/p:InformationalVersion=$Version",
    "/p:FileVersion=$Version.0",
    "/p:AssemblyVersion=$Version.0"
)

if ($Step -eq "Prepare") {
    dotnet build (Join-Path $root "HVAKR.Revit.sln") --configuration Release @properties
    dotnet test (Join-Path $root "HVAKR.Api.Tests/HVAKR.Api.Tests.csproj") --configuration Release --no-build --verbosity normal @properties
    dotnet msbuild $project /t:PrepareBundle /p:Configuration=Release @properties
    return
}

dotnet msbuild $project /t:BuildBundle /p:Configuration=Release /p:SkipPrepareBundle=true @properties

$zip = Join-Path $root "HVAKR.Revit/Deploy/HVAKR-Revit-Plugin-$Version.zip"
& (Join-Path $PSScriptRoot "Test-Bundle.ps1") -Zip $zip -Version $Version -RequireSignature:$RequireSignature
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content "$zip.sha256" "$hash  $(Split-Path $zip -Leaf)" -NoNewline -Encoding ascii
