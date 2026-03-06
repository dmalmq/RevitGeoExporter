; RevitGeoExporter installer definition (Inno Setup 6)
;
; Build with:
;   ISCC.exe /DMyAppVersion=1.0.0.0 /DDistDir="C:\path\to\install\dist" /DOutputDir="C:\path\to\install\output" install\RevitGeoExporter.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0.0"
#endif

#ifndef DistDir
  #define DistDir "dist"
#endif

#ifndef OutputDir
  #define OutputDir "output"
#endif

#define AppName "RevitGeoExporter"
#define AppPublisher "RevitGeoExporter"
#define RevitYear "2024"
#define AddinsRoot "{commonappdata}\Autodesk\Revit\Addins\" + RevitYear
#define InstallSubDir "RevitGeoExporter"

[Setup]
AppId={{69D307D9-6A11-4BD8-9B6A-8AA7D25A4E44}
AppName={#AppName}
AppVersion={#MyAppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={#AddinsRoot}\{#InstallSubDir}
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\RevitGeoExporter.dll
OutputDir={#OutputDir}
OutputBaseFilename=RevitGeoExporter-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[InstallDelete]
Type: filesandordirs; Name: "{app}"

[Files]
Source: "{#DistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "RevitGeoExporter.addin"; DestDir: "{#AddinsRoot}"; Flags: ignoreversion

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not FileExists(ExpandConstant('{pf}\Autodesk\Revit {#RevitYear}\Revit.exe')) then
  begin
    if MsgBox(
      'Autodesk Revit {#RevitYear} was not detected on this machine.'#13#10#13#10 +
      'Install anyway?',
      mbConfirmation,
      MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;
