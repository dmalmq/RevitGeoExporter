# RevitGeoExporter

RevitGeoExporter is a Revit 2024 add-in that turns selected floor and ceiling plan views into IMDF-style GeoPackage files for indoor mapping workflows.

It is intended for teams that already model indoor spaces in Revit and need a repeatable way to extract units, circulation, openings, and level geometry into GIS-friendly deliverables.

At a glance, the add-in:

- Exports selected Revit plan views into `_unit`, `_detail`, `_opening`, and `_level` GeoPackages.
- Includes a guided export dialog with output, feature, coordinate, and advanced sections.
- Includes a preview step so the user can inspect export geometry, warnings, and assignments before writing files.
- Helps recover from inconsistent floor naming by letting the user assign categories to unclassified floor-derived units.
- Keeps export IDs stable and supports configurable shared-coordinate or target CRS / EPSG export settings.
- Ships with a built-in bilingual offline help viewer for export, preview, settings, validation, and troubleshooting topics.

## What the add-in does

- Adds a `GeoExporter` ribbon tab in Revit with:
  - `Export GeoPackage`
  - `Help`
- Lets the user select one or more floor or ceiling plan views to export.
- Exports one GeoPackage per selected view and feature type:
  - `_unit`
  - `_detail`
  - `_opening`
  - `_level`
- Includes a settings hub for global defaults, project mappings, accepted opening families, and export profiles.
- Supports configurable shared-coordinate export or conversion to a target CRS / EPSG.
- Preserves stable IMDF IDs during export by using shared parameters in the Revit model.

## Preview workflow

The export dialog includes a full preview workflow before export.

The preview can:

- Show the selected view before export.
- Toggle `Units`, `Openings`, `Details`, `Levels`, and vertical circulation layers.
- Color-code unit categories.
- Search by feature name, category, or export ID.
- Filter warnings, overrides, unassigned content, and vertical circulation types like `stairs`, `escalators`, and `elevators`.
- Pan, zoom, fit, reset, and inspect feature metadata.
- Show warnings, assignments, and selected-feature details in a dedicated inspector.
- List floor-derived units that fell back to `unspecified`.
- Let the user assign a category to those unassigned floor types before export.
- Optionally show a basemap and survey point when the current preview context supports them.

Floor category assignments are stored as project-specific exporter overrides. They are not written back into Revit floor names or parameters.

## Typical export flow

1. Open a supported plan view model in Revit 2024.
2. Start `GeoExporter > Export GeoPackage`.
3. Choose the plan views, output folder, and feature types to export.
4. Review the coordinate summary and expand coordinate settings only if you need to convert to a target CRS / EPSG.
5. Open `Preview...` to verify units, openings, details, levels, warnings, and vertical circulation.
6. If needed, assign categories to unassigned floor-derived units in the preview.
7. Run export, review validation results, and then inspect the generated GeoPackage files and diagnostics output.

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

The add-in currently targets Revit 2024 and focuses on plan-view export. The preview-side floor assignment workflow is limited to floor-derived units; it does not rename Revit types or write category overrides back into the model. The built-in help content is offline and embedded with the add-in for English and Japanese workflows.

## UI modernization roadmap

- The main export, preview, and help experiences now use the newer WPF shell. See [docs/wpf-migration-plan.md](docs/wpf-migration-plan.md) for the remaining incremental WinForms-to-WPF migration plan across the rest of the repository.
