; HVAKR Revit Plugin Installer
; Supports Revit 2025–2026 with dynamic .addin file generation and logging

#define MyAppName "HVAKR Revit Plugin"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef PluginSource
  #define PluginSource "..\Deploy\Plugin"
#endif
#ifndef InstallerOutput
  #define InstallerOutput "Output"
#endif
#define MyAppPublisher "Flow Circuits, Inc."
#define MyAppURL "https://www.hvakr.com/"

[Setup]
AppId={{21C25DB7-6EBD-4F7D-8891-7FDC50D02A90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\HVAKR\Revit Plugin
DefaultGroupName=HVAKR
LicenseFile=license.txt
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousAppDir=yes
UsePreviousPrivileges=yes
CloseApplications=no
RestartApplications=no
OutputDir={#InstallerOutput}
OutputBaseFilename=HVAKR-Revit-Plugin-{#MyAppVersion}
SetupIconFile=hvakr.ico
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PluginSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Code]
procedure CreateAddinFileForVersion(version: String);
var
  folder, filePath, content: String;
begin
  if IsAdminInstallMode then
    folder := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + version)
  else
    folder := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' + version);
  filePath := folder + '\HVAKR.addin';

  Log('Creating .addin file for Revit ' + version);
  Log('Target folder: ' + folder);
  Log('Target file path: ' + filePath);

  ForceDirectories(folder);

content :=
  '<?xml version="1.0" encoding="utf-8" standalone="no"?>' + #13#10 +
  '<RevitAddIns>' + #13#10 +
  '  <AddIn Type="Application">' + #13#10 +
  '    <Name>HVAKR</Name>' + #13#10 +
  '    <AddInId>F6EF6882-4685-4EE2-8D99-8ECEB6914358</AddInId>' + #13#10 +
  '    <VendorId>DESIGN MUX LLC</VendorId>' + #13#10 +
  '    <FullClassName>HVAKR.Revit.App</FullClassName>' + #13#10 +
  '    <Text>HVAKR</Text>' + #13#10 +
  '    <VisibilityMode>AlwaysVisible</VisibilityMode>' + #13#10 +
  '    <Assembly>' + ExpandConstant('{app}\HVAKR.Revit.dll') + '</Assembly>' + #13#10 +
  '  </AddIn>' + #13#10 +
  '</RevitAddIns>';

  if SaveStringToFile(filePath, content, False) then
    Log('Successfully wrote .addin file for Revit ' + version)
  else
    Log('Failed to write .addin file for Revit ' + version);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    CreateAddinFileForVersion('2025');
    CreateAddinFileForVersion('2026');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  addinRoot: String;
begin
  if CurUninstallStep = usUninstall then begin
    if IsAdminInstallMode then
      addinRoot := '{commonappdata}'
    else
      addinRoot := '{userappdata}';
    DeleteFile(ExpandConstant(addinRoot + '\Autodesk\Revit\Addins\2025\HVAKR.addin'));
    DeleteFile(ExpandConstant(addinRoot + '\Autodesk\Revit\Addins\2026\HVAKR.addin'));
  end;
end;
