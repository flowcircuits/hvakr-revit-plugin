; HVAKR Revit Plugin Installer
; Supports Revit 2025–2026 with dynamic .addin file generation and logging

#define MyAppName "HVAKR Revit Plugin"
#define MyAppVersion "1.914.12"
#define MyAppPublisher "HVAKR"
#define MyAppURL "https://www.hvakr.com/"

[Setup]
AppId={{21C25DB7-6EBD-4F7D-8891-7FDC50D02A90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\HVAKR
DefaultGroupName=HVAKR
LicenseFile=license.txt
PrivilegesRequired=admin
OutputBaseFilename=HVAKR-Setup
SetupIconFile=hvakr.ico
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\Deploy\Plugin\net8.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Code]
var
  RevitVersionsToCheck: array[0..1] of String;


function IsRevitVersionInstalled(version: String): Boolean;
var
  key: String;
begin
  key := 'SOFTWARE\Autodesk\Revit\Autodesk Revit ' + version;
  // explicitly check the 64-bit registry view
  Result := RegKeyExists(HKLM64, key);
end;


procedure CreateAddinFileForVersion(version: String);
var
  folder, filePath, content: String;
begin
  folder := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + version);
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
var
  i: Integer;
begin
  if CurStep = ssPostInstall then begin
    Log('--- Starting Revit add-in installation step ---');
    RevitVersionsToCheck[0] := '2025';
    RevitVersionsToCheck[1] := '2026';

    for i := 0 to GetArrayLength(RevitVersionsToCheck) - 1 do begin
      if IsRevitVersionInstalled(RevitVersionsToCheck[i]) then begin
        Log('Revit ' + RevitVersionsToCheck[i] + ' is installed');
        CreateAddinFileForVersion(RevitVersionsToCheck[i]);
      end else begin
        Log('Revit ' + RevitVersionsToCheck[i] + ' NOT found');
      end;
    end;
    Log('--- Revit add-in installation step complete ---');
  end;
end;
