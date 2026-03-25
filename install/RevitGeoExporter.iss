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

#ifndef RevitYear
  #define RevitYear "2024"
#endif

#define AppName "RevitGeoExporter"
#define AppPublisher "RevitGeoExporter"
#define AppId "RevitGeoExporter.Revit" + RevitYear
#define AddinsRoot "{commonappdata}\Autodesk\Revit\Addins\" + RevitYear
#define InstallSubDir "RevitGeoExporter"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#MyAppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={#AddinsRoot}\{#InstallSubDir}
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName} for Revit {#RevitYear}
UninstallDisplayIcon={app}\RevitGeoExporter.dll
OutputDir={#OutputDir}
OutputBaseFilename=RevitGeoExporter-Setup-{#RevitYear}-{#MyAppVersion}
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
Source: "{#DistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "RevitGeoExporter.addin"
Source: "examples\*"; DestDir: "{app}\Examples"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#DistDir}\RevitGeoExporter.addin"; DestDir: "{#AddinsRoot}"; Flags: ignoreversion

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
