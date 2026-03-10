# RevitGeoExporter

RevitGeoExporter is a Revit 2024 add-in that turns selected floor and ceiling plan views into IMDF-style GeoPackage files for indoor mapping workflows.

It is intended for teams that already model indoor spaces in Revit and need a repeatable way to extract units, circulation, openings, and level geometry into GIS-friendly deliverables.

At a glance, the add-in:

- Exports selected Revit plan views into `_unit`, `_detail`, `_opening`, and `_level` GeoPackages.
- Includes a preview step so the user can inspect export geometry before writing files.
- Helps recover from inconsistent floor naming by letting the user assign categories to unclassified floor-derived units.
- Keeps export IDs stable and supports configurable output CRS / EPSG settings.

## What the add-in does

- Adds a `GeoExporter` ribbon tab in Revit with:
  - `Export GeoPackage`
  - `Settings`
- Lets the user select one or more floor or ceiling plan views to export.
- Exports one GeoPackage per selected view and feature type:
  - `_unit`
  - `_detail`
  - `_opening`
  - `_level`
- Supports configurable output CRS / EPSG selection.
- Preserves stable IMDF IDs during export by using shared parameters in the Revit model.

## Preview workflow

The export dialog includes a read-only preview for `unit` and `opening` output.

The preview can:

- Show the selected view before export.
- Color-code unit categories.
- Toggle `Units` and `Openings`.
- Filter vertical circulation types like `stairs`, `escalators`, and `elevators`.
- Pan, zoom, fit, and inspect feature metadata.
- List floor-derived units that fell back to `unspecified`.
- Let the user assign a category to those unassigned floor types before export.

Floor category assignments are stored as project-specific exporter overrides. They are not written back into Revit floor names or parameters.

## Typical export flow

1. Open a supported plan view model in Revit 2024.
2. Start `GeoExporter > Export GeoPackage`.
3. Choose the views and feature types to export.
4. Open `Preview...` to verify units, openings, and vertical circulation.
5. If needed, assign categories to unassigned floor-derived units in the preview.
6. Run export and review the generated GeoPackage files.

## Installation

For installer-based deployment, see [install/README.md](install/README.md).

The generated installer:

- Installs the add-in under `C:\ProgramData\Autodesk\Revit\Addins\2024\`
- Registers a normal Windows uninstall entry
- Requires administrator rights

## Build from source

Prerequisites:

- Windows
- .NET SDK
- Revit 2024 API installed locally
- Inno Setup 6 if you want to build the installer EXE

Useful commands:

```powershell
dotnet build RevitGeoExporter.sln
dotnet test RevitGeoExporter.sln
pwsh ./install/build-installer.ps1
```

## Repository layout

- `src/RevitGeoExporter/`: Revit add-in UI, commands, extractors, and export orchestration
- `src/RevitGeoExporter.Core/`: geometry, GeoPackage writing, preview helpers, and shared models
- `tests/RevitGeoExporter.Core.Tests/`: automated tests for core logic
- `install/`: installer scripts and Inno Setup definition
- `tools/`: helper scripts for data conversion and local workflows

## Current scope

The add-in currently targets Revit 2024 and focuses on plan-view export. The preview-side floor assignment workflow is limited to floor-derived units; it does not rename Revit types or write category overrides back into the model.

## UI modernization roadmap

- See [docs/wpf-migration-plan.md](docs/wpf-migration-plan.md) for an incremental WinForms-to-WPF migration plan tailored to this repository.
