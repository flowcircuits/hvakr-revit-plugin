# Builds the release path. CI and release.yml both call this so a green PR exercises
# the same commands as a release, minus Azure login, signing, and publishing.
#
#   -Step Prepare  Release build, tests, and HVAKR.Revit\Deploy\Plugin staging (signing happens after this)
#   -Step Package  Installer from the already-staged files, then version checks
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Prepare", "Package")]
    [string]$Step
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
    dotnet msbuild $project /t:PrepareInstaller /p:Configuration=Release @properties
    return
}

dotnet msbuild $project /t:BuildInstaller /p:Configuration=Release /p:SkipPrepareInstaller=true @properties

$installer = Join-Path $root "HVAKR.Revit/Deploy/Installer/HVAKR-Revit-Plugin-$Version.exe"
Get-ChildItem (Join-Path $root "HVAKR.Revit/Deploy/Plugin") -Recurse -Include HVAKR*.dll, HVAKR*.exe | ForEach-Object {
    if ($_.VersionInfo.ProductVersion -ne $Version) { throw "$($_.Name) ProductVersion is $($_.VersionInfo.ProductVersion)." }
}
# Inno pads the ProductVersion string resource with trailing spaces.
$installerVersion = (Get-Item $installer).VersionInfo.ProductVersion.Trim()
if ($installerVersion -ne $Version) { throw "Installer ProductVersion is $installerVersion, expected $Version." }
