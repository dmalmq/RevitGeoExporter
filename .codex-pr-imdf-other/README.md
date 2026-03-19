# RevitGeoExporter

RevitGeoExporter is a Revit 2024 add-in that turns selected floor and ceiling plan views into IMDF-style GeoPackage files for indoor mapping workflows.

<p>
A native Revit add-in that turns floor and ceiling plan views into georeferenced GeoPackage files<br />
for indoor mapping, digital twin, and navigation workflows.<br />
Built for teams that model indoor spaces in Revit and need a repeatable path to GIS-ready deliverables.
</p>

<p>
  <img src="https://img.shields.io/badge/Platform-Revit_2024-0f766e?style=for-the-badge" />
  <img src="https://img.shields.io/badge/Output-GeoPackage-0284c7?style=for-the-badge" />
  <img src="https://img.shields.io/badge/Schema-IMDF--Style-0891b2?style=for-the-badge" />
</p>

- Exports selected Revit plan views into `_unit`, `_detail`, `_opening`, and `_level` GeoPackages.
- Includes a guided export dialog with output, feature, coordinate, and advanced sections.
- Includes a preview step so the user can inspect export geometry, warnings, and assignments before writing files.
- Helps recover from inconsistent floor naming by letting the user assign categories to unclassified floor-derived units.
- Keeps export IDs stable and supports configurable shared-coordinate or target CRS / EPSG export settings.
- Ships with a built-in bilingual offline help viewer for export, preview, settings, validation, and troubleshooting topics.

## What the add-in does

---

## About

RevitGeoExporter extracts units, circulation, openings, and level geometry from Revit plan views and writes them into individual GeoPackage files with full IMDF-style attribution. It handles coordinate transforms, stable ID persistence, and category resolution so the exported data is ready for downstream GIS and indoor navigation systems without manual conversion steps.

The add-in is designed around Japanese rail station and commercial complex workflows but works with any Revit model that uses floor and ceiling plan views.

---

## Features

- **Plan view export** — select one or more floor or ceiling plan views and export `_unit`, `_detail`, `_opening`, and `_level` GeoPackages per view
- **Interactive preview** — inspect geometry, toggle layers, color-code categories, search features, and resolve unassigned floor types before writing files
- **Stable IMDF IDs** — auto-generated UUIDs stored as Revit shared parameters and preserved across exports
- **Coordinate system support** — shared coordinates, configurable target CRS / EPSG, and a built-in Japan Plane Rectangular zone catalog
- **Validation & diagnostics** — pre-export checks for duplicate IDs, empty views, and vertical circulation audits, plus JSON diagnostics output
- **Settings hub** — global defaults, project-specific zone mappings, accepted opening families, and reusable export profiles
- **Bilingual help** — embedded offline help viewer with English and Japanese content

1. Open a supported plan view model in Revit 2024.
2. Start `GeoExporter > Export GeoPackage`.
3. Choose the plan views, output folder, and feature types to export.
4. Open the settings hub from the export flow if you need to adjust defaults, mappings, basemap settings, or export profiles.
5. Review the coordinate summary and expand coordinate settings only if you need to convert to a target CRS / EPSG.
6. Open `Preview...` to verify units, openings, details, levels, warnings, and vertical circulation.
7. If needed, assign categories to unassigned floor-derived units in the preview.
8. Run export, review validation results, and then inspect the generated GeoPackage files and diagnostics output.

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

<div align="center">

Revit plan views → georeferenced GeoPackage → indoor mapping & navigation

</div>
