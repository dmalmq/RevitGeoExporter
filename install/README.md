# RevitGeoExporter Installer

This folder supports two installation flows for a selected Revit year. The default target is Revit 2024.

1. `build-installer.ps1` (recommended): builds a real installer `.exe` (Inno Setup).
2. `install.ps1`: direct admin copy install (legacy/dev convenience).

## Build a setup EXE

Prerequisites:
- .NET SDK (for `dotnet build`)
- Inno Setup 6 (`ISCC.exe`)

Command:

```powershell
pwsh ./install/build-installer.ps1 -RevitYear 2024
```

Optional parameters:

```powershell
pwsh ./install/build-installer.ps1 -Configuration Release -RevitYear <year> -RevitApiDir "C:\Program Files\Autodesk\Revit <year>" -Version <version> -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Output:
- `install/output/RevitGeoExporter-Setup-<year>-<version>.exe`

## Installer behavior

- Installs add-in payload to:
  - `C:\ProgramData\Autodesk\Revit\Addins\<year>\RevitGeoExporter\`
- Installs manifest to:
  - `C:\ProgramData\Autodesk\Revit\Addins\<year>\RevitGeoExporter.addin`
- Registers proper uninstall entry in Windows Apps/Programs.
- Requires admin rights.

## Notes

- Revit must be restarted after install/uninstall.
- If the selected Revit year is not detected, the installer prompts for confirmation before continuing.

## Examples

The installer also places importable starter room-mapping JSON files under:
- `C:\ProgramData\Autodesk\Revit\Addins\<year>\RevitGeoExporter\Examples\`

These can be imported from the exporter project mappings flow as a baseline for room-to-unit categorization.
