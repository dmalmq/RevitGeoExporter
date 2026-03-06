# RevitGeoExporter Installer

This folder supports two installation flows for Revit 2024:

1. `build-installer.ps1` (recommended): builds a real installer `.exe` (Inno Setup).
2. `install.ps1`: direct admin copy install (legacy/dev convenience).

## Build a setup EXE

Prerequisites:
- .NET SDK (for `dotnet build`)
- Inno Setup 6 (`ISCC.exe`)

Command:

```powershell
pwsh ./install/build-installer.ps1
```

Optional parameters:

```powershell
pwsh ./install/build-installer.ps1 -Configuration Release -Version 1.2.0 -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Output:
- `install/output/RevitGeoExporter-Setup-<version>.exe`

## Installer behavior

- Installs add-in payload to:
  - `C:\ProgramData\Autodesk\Revit\Addins\2024\RevitGeoExporter\`
- Installs manifest to:
  - `C:\ProgramData\Autodesk\Revit\Addins\2024\RevitGeoExporter.addin`
- Registers proper uninstall entry in Windows Apps/Programs.
- Requires admin rights.

## Notes

- Revit must be restarted after install/uninstall.
- If Revit 2024 is not detected, the installer prompts for confirmation before continuing.
