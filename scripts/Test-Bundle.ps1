# Verifies a HVAKR-Revit-Plugin-<version>.zip the way Revit's Autoloader will read it.
# CI runs it on every PR. Release and publish runs add -RequireSignature.
param(
    [Parameter(Mandatory = $true)]
    [string]$Zip,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$RequireSignature
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$expectedSeries = @("R2025", "R2026")
$addInId = "F6EF6882-4685-4EE2-8D99-8ECEB6914358"
$entryClass = "HVAKR.Revit.App"

function Assert-True([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}

# 1. Every zip entry sits under HVAKR.bundle/ with no Revit API assemblies.
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Zip))
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
} finally {
    $archive.Dispose()
}
Assert-True ($entries.Count -gt 0) "The zip is empty."
$outside = $entries | Where-Object { $_ -notlike "HVAKR.bundle/*" }
Assert-True (!$outside) "Entries outside HVAKR.bundle/: $($outside -join ', ')"
$revitApi = $entries | Where-Object { $_ -match '/RevitAPI(UI)?\.dll$' }
Assert-True (!$revitApi) "Revit API assemblies must not be packaged: $($revitApi -join ', ')"

# 2. Unzip into an ApplicationPlugins-style folder, as a user would.
$plugins = Join-Path ([System.IO.Path]::GetTempPath()) "HVAKR.Bundle.Test/$([guid]::NewGuid().ToString('N'))/ApplicationPlugins"
New-Item -ItemType Directory -Force $plugins | Out-Null
try {
    Expand-Archive -Path $Zip -DestinationPath $plugins
    $bundle = Join-Path $plugins "HVAKR.bundle"

    # 3. PackageContents.xml is well-formed and complete.
    [xml]$package = Get-Content -Raw (Join-Path $bundle "PackageContents.xml")
    $app = $package.ApplicationPackage
    Assert-True ($null -ne $app) "PackageContents.xml has no ApplicationPackage root."
    Assert-True ($app.AutodeskProduct -eq "Revit") "AutodeskProduct must be Revit."
    Assert-True ($app.AppVersion -eq $Version) "AppVersion is '$($app.AppVersion)', expected $Version."
    foreach ($code in @($app.ProductCode, $app.UpgradeCode)) {
        Assert-True ($code -match '^\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}$') "Invalid GUID '$code' in PackageContents.xml."
    }
    if ($app.Icon) {
        Assert-True (Test-Path (Join-Path $bundle $app.Icon)) "Icon $($app.Icon) is missing."
    }

    $components = @($package.ApplicationPackage.Components)
    $series = @($components | ForEach-Object { $_.RuntimeRequirements.SeriesMin })
    Assert-True ((Compare-Object $expectedSeries $series | Measure-Object).Count -eq 0) "Components cover $($series -join ', '), expected $($expectedSeries -join ', ')."

    foreach ($component in $components) {
        $requirements = $component.RuntimeRequirements
        Assert-True ($requirements.SeriesMin -eq $requirements.SeriesMax) "Each component must target one Revit release: $($requirements.SeriesMin)-$($requirements.SeriesMax)."
        Assert-True ($requirements.Platform -eq "Revit" -and $requirements.OS -eq "Win64") "$($requirements.SeriesMin) must require Revit on Win64."
        $entry = $component.ComponentEntry
        Assert-True ($entry.Version -eq $Version) "$($requirements.SeriesMin) ComponentEntry Version is '$($entry.Version)'."
        $year = $requirements.SeriesMin.Substring(1)
        Assert-True ($entry.ModuleName -eq "./Contents/$year/HVAKR.addin") "$($requirements.SeriesMin) points at $($entry.ModuleName)."

        # 4. Each .addin manifest points at an assembly that exists and defines the entry class.
        $manifestPath = Join-Path $bundle $entry.ModuleName
        Assert-True (Test-Path $manifestPath) "$($entry.ModuleName) is missing."
        [xml]$manifest = Get-Content -Raw $manifestPath
        $addIns = @($manifest.RevitAddIns.AddIn)
        Assert-True ($addIns.Count -eq 1) "$($entry.ModuleName) must declare exactly one add-in."
        $addIn = $addIns[0]
        Assert-True ($addIn.Type -eq "Application") "$($entry.ModuleName) must declare an Application add-in."
        Assert-True ($addIn.AddInId -eq $addInId) "$($entry.ModuleName) AddInId changed to $($addIn.AddInId)."
        Assert-True ($addIn.FullClassName -eq $entryClass) "$($entry.ModuleName) FullClassName is $($addIn.FullClassName)."
        Assert-True (![System.IO.Path]::IsPathRooted($addIn.Assembly)) "$($entry.ModuleName) must use a relative Assembly path."

        $assembly = Join-Path (Split-Path $manifestPath -Parent) $addIn.Assembly
        Assert-True (Test-Path $assembly) "$($entry.ModuleName) points at missing $($addIn.Assembly)."
        $stream = [System.IO.File]::OpenRead($assembly)
        try {
            $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader([System.Reflection.PortableExecutable.PEReader]::new($stream))
            $types = $reader.TypeDefinitions | ForEach-Object {
                $type = $reader.GetTypeDefinition($_)
                "$($reader.GetString($type.Namespace)).$($reader.GetString($type.Name))"
            }
        } finally {
            $stream.Dispose()
        }
        Assert-True ($types -contains $entryClass) "$($addIn.Assembly) does not define $entryClass."
    }

    # 5. Every HVAKR DLL carries the release version. Release builds also check signatures.
    $dlls = @(Get-ChildItem $bundle -Recurse -Filter *.dll)
    $hvakrDlls = @($dlls | Where-Object Name -like "HVAKR*.dll")
    Assert-True ($hvakrDlls.Count -gt 0) "The bundle has no HVAKR assemblies."
    foreach ($dll in $hvakrDlls) {
        $productVersion = $dll.VersionInfo.ProductVersion
        Assert-True ($productVersion -eq $Version) "$($dll.FullName.Substring($bundle.Length)) ProductVersion is $productVersion, expected $Version."
    }

    if ($RequireSignature) {
        foreach ($dll in $dlls) {
            $signature = Get-AuthenticodeSignature $dll.FullName
            Assert-True ($signature.Status -eq "Valid") "$($dll.Name) signature is $($signature.Status)."
            if ($dll.Name -like "HVAKR*.dll") {
                Assert-True ($signature.SignerCertificate.Subject -match 'O=Flow Circuits') "$($dll.Name) is not signed by Flow Circuits."
            }
        }
    }

    $hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Bundle OK: $Zip"
    Write-Host "SHA256: $hash"
} finally {
    Remove-Item -Recurse -Force (Split-Path $plugins -Parent) -ErrorAction SilentlyContinue
}
