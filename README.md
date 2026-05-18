# RevitGeoExporter

RevitGeoExporter is a Revit add-in (default target **Revit 2024**) that turns floor and ceiling plan views into IMDF-style GeoPackage or Shapefile output for indoor mapping, digital-twin, and navigation workflows. The build is parameterized by Revit year so additional versions can be targeted from the same source without forking.

<p>
A native Revit add-in that turns floor and ceiling plan views into georeferenced GeoPackage<br />
or Shapefile output for indoor mapping, digital twin, and navigation workflows.<br />
Built for teams that model indoor spaces in Revit and need a repeatable path to GIS-ready deliverables.
</p>

<p>
  <img src="https://img.shields.io/badge/Platform-Revit_2024-0f766e?style=for-the-badge" />
  <img src="https://img.shields.io/badge/Output-GeoPackage_%7C_Shapefile-0284c7?style=for-the-badge" />
  <img src="https://img.shields.io/badge/Schema-IMDF--Style-0891b2?style=for-the-badge" />
</p>

---

## About

RevitGeoExporter extracts units, circulation, openings, fixtures, and level geometry from Revit plan views and writes them into GeoPackage (or Shapefile) files with full IMDF-style attribution. It handles coordinate transforms, stable ID persistence, category resolution, and incremental re-export so the data is ready for downstream GIS and indoor navigation systems without manual conversion steps.

The add-in is designed around Japanese rail-station and commercial-complex workflows, but works with any Revit model that uses floor or ceiling plan views.

---

## Features

### Export

- **Plan view export** — select one or more floor or ceiling plan views and export `unit`, `detail`, `opening`, `level`, and `fixture` layers per view.
- **Multiple output formats** — write GeoPackage (default) or Shapefile.
- **Packaging modes** — one file per (view × feature), one GeoPackage per view, per level, or per building.
- **Linked-model support** — include geometry from selected loaded Revit links in the same export.
- **3D section-box mode** — optional alternate geometry source that builds a thin 3D slab around each plan view's cut plane (configurable above/below floor offsets) for cases where plan-view cuts mis-represent geometry.
- **Stair & escalator simplification** — optional cleanup that reduces noisy stair / escalator outlines into simpler unit polygons.
- **Incremental export** — reuses unchanged artifacts from a fingerprinted baseline so re-exports only rewrite views whose contents or settings have changed.
- **Batch export** — run a sequence of saved profiles in one job.

### Coordinate systems

- **Shared coordinates** — exports using Revit's shared coordinate system.
- **Target CRS / EPSG conversion** — reproject on the fly via ProjNet.
- **Built-in CRS presets** — including the full Japan Plane Rectangular zone catalog.

### Interactive workflow

- **Guided export dialog** with output, feature, coordinate, advanced, and profile sections.
- **Preview window** — inspect geometry, toggle layers, color-code categories, search features, and resolve unassigned floor types before writing files.
- **Readiness & validation forms** — pre-export checks for duplicate IDs, empty views, unsupported opening families, vertical-circulation audits, and coordinate-system issues; navigate from issues straight to the element in Revit.
- **Issue resolution** — assign categories to unclassified floors/rooms and regenerate stable IDs without leaving the dialog.

### Settings & configuration

- **Settings hub** — global defaults, project-specific mappings, accepted opening families, basemap settings.
- **Export profiles** — reusable export configurations, scoped per-project or global.
- **Schema profiles** — map custom Revit parameters to IMDF attribute fields per layer.
- **Validation policy profiles** — promote / demote / suppress individual validation checks.

### Output

- **Stable IMDF IDs** — auto-generated UUIDs stored as Revit shared parameters and preserved across exports.
- **Diagnostics report** — JSON output with phase timings, fingerprints, change summary against the previous baseline, and validation findings.
- **Package output** — optional bundled package directory with manifest, legend, and post-write package validation.
- **Post-export actions** — optional QGIS artifact generation and configurable post-export steps.
- **Bilingual offline help** — embedded help viewer covering export, preview, settings, validation, and troubleshooting topics in English and Japanese.

---

## Usage

1. Open a supported plan-view model in the Revit version the add-in was built for.
2. Start `GeoExporter > Export GeoPackage`.
3. Choose the plan views, output folder, output format, packaging mode, and feature types to export.
4. Open the settings hub from the export flow if you need to adjust defaults, mappings, basemap settings, schema or validation profiles, or export profiles.
5. Review the coordinate summary and expand coordinate settings only if you need to convert to a target CRS / EPSG.
6. Open `Preview...` to verify units, openings, details, levels, fixtures, warnings, and vertical circulation.
7. If needed, assign categories to unassigned floor- or room-derived units in the preview.
8. Run export, review the readiness / validation summary, and then inspect the generated files and diagnostics output.

---

## Installation

For installer-based deployment, see [install/README.md](install/README.md).

The generated installer:

- Installs the add-in under `C:\ProgramData\Autodesk\Revit\Addins\<selected-year>\`
- Registers a normal Windows uninstall entry
- Requires administrator rights

Revit must be restarted after install or uninstall.

---

## Build from source

Prerequisites:

- Windows
- .NET SDK
- Revit API installed locally for the target year (defaults to Revit 2024)
- Inno Setup 6 if you want to build the installer EXE

Useful commands:

```powershell
dotnet build src/RevitGeoExporter/RevitGeoExporter.csproj -p:RevitYear=2024
dotnet test RevitGeoExporter.sln
pwsh ./install/build-installer.ps1 -RevitYear 2024
pwsh ./install/build-installer.ps1 -RevitYear 2026 -RevitApiDir "C:\Program Files\Autodesk\Revit 2026"
```

---

## Repository layout

- `src/RevitGeoExporter/` — Revit add-in: ribbon, commands, extractors, WPF + WinForms UI, export orchestration
- `src/RevitGeoExporter.Core/` — Revit-API-free library: geometry, GeoPackage / Shapefile writing, CRS, validation, diagnostics, preview, schema, persistence
- `tests/RevitGeoExporter.Core.Tests/` — xUnit tests for the core logic
- `install/` — installer scripts and Inno Setup definition
- `tools/` — helper scripts for data conversion and local workflows
- `docs/` — internal docs (WPF migration plan, release smoke checklist)
