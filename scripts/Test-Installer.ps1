param(
    [Parameter(Mandatory = $true)]
    [string]$FirstInstaller,

    [string]$UpgradeInstaller,

    [switch]$AllUsers
)

$ErrorActionPreference = "Stop"
$installDirectory = if ($AllUsers) {
    Join-Path $env:ProgramFiles "HVAKR"
} else {
    Join-Path $env:LOCALAPPDATA "Programs\HVAKR\Revit Plugin"
}
$addinRoot = if ($AllUsers) { $env:ProgramData } else { $env:APPDATA }
$manifest2025 = Join-Path $addinRoot "Autodesk\Revit\Addins\2025\HVAKR.addin"
$manifest2026 = Join-Path $addinRoot "Autodesk\Revit\Addins\2026\HVAKR.addin"
$installerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS"

function Invoke-Installer([string]$Path, [switch]$UseAllUsers) {
    $arguments = $installerArguments
    if ($UseAllUsers) { $arguments += " /ALLUSERS /DIR=`"$installDirectory`"" }
    $process = Start-Process -FilePath $Path -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Installer exited with code $($process.ExitCode)."
    }
}

function Assert-Installed {
    if (!(Test-Path (Join-Path $installDirectory "HVAKR.Revit.dll"))) { throw "Plugin DLL was not installed." }
    if (!(Test-Path (Join-Path $installDirectory "HVAKR.Revit.Updater.exe"))) { throw "Updater was not installed." }
    if (!(Test-Path $manifest2025) -or !(Test-Path $manifest2026)) { throw "Both Revit manifests were not installed." }
    if (Test-Path (Join-Path $installDirectory "RevitAPI.dll")) { throw "RevitAPI.dll must not be packaged." }
    if (Test-Path (Join-Path $installDirectory "RevitAPIUI.dll")) { throw "RevitAPIUI.dll must not be packaged." }
    if ($AllUsers) {
        $userManifest2025 = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2025\HVAKR.addin"
        $userManifest2026 = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2026\HVAKR.addin"
        if ((Test-Path $userManifest2025) -or (Test-Path $userManifest2026)) { throw "An all-users upgrade must not add user Revit manifests." }
    }
}

Invoke-Installer $FirstInstaller -UseAllUsers:$AllUsers
Assert-Installed

if ($UpgradeInstaller) {
    Invoke-Installer $UpgradeInstaller
    Assert-Installed
}

$uninstaller = Join-Path $installDirectory "unins000.exe"
if (!(Test-Path $uninstaller)) { throw "Uninstaller was not created." }
$uninstall = Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -Wait -PassThru
if ($uninstall.ExitCode -ne 0) { throw "Uninstaller exited with code $($uninstall.ExitCode)." }

if (Test-Path $installDirectory) { throw "Plugin files remain after uninstall." }
if ((Test-Path $manifest2025) -or (Test-Path $manifest2026)) { throw "A user Revit manifest remains after uninstall." }
