<div align="center">

# RevitGeoExporter

### Revit 2024 Add-in · Indoor GeoPackage Export · IMDF-Style Workflows

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

<p>
  <img src="https://img.shields.io/badge/.NET_Framework-4.8-68217a?style=flat-square" />
  <img src="https://img.shields.io/badge/Language-C%23-68217a?style=flat-square" />
  <img src="https://img.shields.io/badge/CRS-Configurable_EPSG-0ea5e9?style=flat-square" />
  <img src="https://img.shields.io/badge/Help-EN_/_JA-c4a7e7?style=flat-square" />
</p>

</div>

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

---

## Typical Export Flow

1. Open a plan view model in Revit 2024
2. Start **GeoExporter → Export GeoPackage**
3. Choose views, output folder, and feature types
4. Optionally adjust coordinate, mapping, or profile settings
5. Open **Preview** to verify geometry, warnings, and assignments
6. Assign categories to any unassigned floor-derived units
7. Run export and review the validation results

---

## Architecture

The repository splits into two projects to keep Revit API coupling minimal and core logic fully testable.

```
RevitGeoExporter              Revit-dependent add-in
├── Commands                   Ribbon commands and workflow orchestration
├── Extractors                 Geometry extraction from Revit elements
├── UI                         WPF export dialog, preview, and help viewer
└── Core                       Shared parameter management

RevitGeoExporter.Core          Zero Revit dependencies
├── Models                     Feature types, zone catalog, category resolution
├── GeoPackage                 SQLite writer and WKB encoder
├── Coordinates                CRS transforms and EPSG catalog
├── Validation                 Pre-export checks and vertical circulation audit
├── Diagnostics                Export reports and change tracking
└── Preview                    Map context, bounds, and tile calculations
```

---

## Technologies

<p>
  <img src="https://img.shields.io/badge/C%23-68217a?style=for-the-badge&logo=csharp&logoColor=ffffff" />
  <img src="https://img.shields.io/badge/.NET_4.8-512bd4?style=for-the-badge&logo=dotnet&logoColor=ffffff" />
  <img src="https://img.shields.io/badge/Revit_API-0f766e?style=for-the-badge" />
  <img src="https://img.shields.io/badge/WPF-3178c6?style=for-the-badge" />
</p>

<p>
  <img src="https://img.shields.io/badge/SQLite-003b57?style=for-the-badge&logo=sqlite&logoColor=ffffff" />
  <img src="https://img.shields.io/badge/NetTopologySuite-475569?style=for-the-badge" />
  <img src="https://img.shields.io/badge/ProjNet-0369a1?style=for-the-badge" />
  <img src="https://img.shields.io/badge/xUnit-512bd4?style=for-the-badge" />
</p>

---

## Build from Source

Prerequisites: Windows, .NET SDK, Revit 2024 API installed locally. Add Inno Setup 6 if building the installer.

```powershell
dotnet build RevitGeoExporter.sln
dotnet test RevitGeoExporter.sln
pwsh ./install/build-installer.ps1
```

---

## Installation

See [install/README.md](install/README.md) for installer-based deployment.

The generated installer places the add-in under `C:\ProgramData\Autodesk\Revit\Addins\2024\`, registers a standard Windows uninstall entry, and requires administrator rights.

---

## Repository Layout

| Path | Contents |
|------|----------|
| `src/RevitGeoExporter/` | Revit add-in — UI, commands, extractors, export orchestration |
| `src/RevitGeoExporter.Core/` | Core logic — geometry, GeoPackage writing, coordinates, validation |
| `tests/RevitGeoExporter.Core.Tests/` | Automated tests for core logic |
| `install/` | Installer scripts, Inno Setup definition, example mappings |
| `docs/` | Specification, development guide, WPF migration plan |
| `tools/` | Helper scripts for data conversion and local workflows |

---

<div align="center">

Revit plan views → georeferenced GeoPackage → indoor mapping & navigation

</div>
