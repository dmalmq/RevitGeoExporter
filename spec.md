# RevitGeoExporter — Specification

## Overview

A Revit add-in that exports plan view data to GeoPackage format, enabling a single-source-of-truth workflow for indoor navigation applications. The add-in replaces the current DWG → Shapefile pipeline by exporting georeferenced 2D floor plan data directly from Revit.

## Context

The Revit models serve as the 3D source for an indoor navigation application used in Japanese rail stations and connected underground commercial complexes. Models are intentionally minimal, containing only navigation-relevant elements: floors (subdivided by zone), openings, stairs, escalators, and elevators.

Floor elements are subdivided by zone/room function using type names (e.g., `ラチ内コンコース_床`, `券売機室_床`, `多目的トイレ_床`). This means floor type names encode spatial classification — there are no Room objects. The `_床` suffix identifies floor types; the prefix is the zone/space name.

## Goals

1. **Phase 1:** Export unit polygons for selected plan views to GeoPackage, with IMDF categories, zone attributes, colors, and fare gate restriction data.
2. **Phase 2:** Export detail lines, openings, and level boundaries as additional feature type GeoPackages. Add full export dialog UI.
3. **Phase 3:** Add FBX export trigger alongside GeoPackage export (one-click, two outputs).
4. **Phase 4:** Optional File Geodatabase (.gdb) conversion via ogr2ogr post-processing.

## Data Model

The export follows IMDF (Indoor Mapping Data Format) feature types and categories. All spaces — concourses, platforms, restrooms, stairs, elevators — are exported as **units** differentiated by their `category` attribute. Cosmetic lines (e.g., stair step markings) are exported as **details**. Openings and the overall level boundary are separate feature types.

### GeoPackage Structure & Naming

One GeoPackage file per view per feature type. The user selects which plan views to export — each view is associated with a level. Filename pattern:

```
{ModelName}_{ViewName}_{feature_type}.gpkg
```

Example for a model named `SHINJUKU_LUMINE_EST` with a plan view named `7`:
```
SHINJUKU_LUMINE_EST_7_unit.gpkg
SHINJUKU_LUMINE_EST_7_detail.gpkg
SHINJUKU_LUMINE_EST_7_opening.gpkg
SHINJUKU_LUMINE_EST_7_level.gpkg
```

The view name is sanitized for filesystem use (spaces and special characters replaced). If multiple views reference the same level, each produces its own set of files — this allows different view configurations (e.g., different visibility overrides) to produce different exports.

All geometry is 2D (projected onto the level's XY plane).

### IMDF Attribute Schema

#### Unit Attributes

| Column | Type | Source | Description |
|---|---|---|---|
| `id` | TEXT (UUID) | Auto-generated, persisted to Revit | Stable unique identifier. Generated on first export, written back to `IMDF_Id` shared instance parameter. Reused on subsequent exports. |
| `category` | TEXT | ZoneCatalog lookup | IMDF unit category (e.g., `walkway`, `stairs`, `elevator`, `restroom.male`). |
| `restrict` | TEXT | ZoneCatalog lookup | Fare gate restriction: `rachi_nai`, `rachi_gai`, or `NULL`. |
| `name` | TEXT | Revit shared instance parameter `IMDF_Name` | Human-curated display name (e.g., "Ticket Gate A"). NULL for generic spaces. |
| `alt_name` | TEXT | Revit shared instance parameter `IMDF_AltName` | Alternative / English name. NULL if not set. |
| `level_id` | TEXT (UUID) | Auto-generated on Level element | References the Level's stable UUID (stored as `IMDF_LevelId` on the Revit Level element). |
| `source` | TEXT | Derived from model metadata | Data source identifier (e.g., model name or constant). |
| `display_point` | TEXT (WKT Point) | Computed centroid of unit polygon | Display point for label placement. |

Note: `fid` (integer primary key) is automatically created by GeoPackage and is not a meaningful attribute. Colors are handled downstream in QGIS via styling rules based on `category` and `restrict`.

#### Detail Attributes

| Column | Type | Source | Description |
|---|---|---|---|
| `id` | TEXT (UUID) | Auto-generated | Stable unique identifier. |
| `level_id` | TEXT (UUID) | From Level element's `IMDF_LevelId` | References the parent level. |
| `element_id` | INTEGER | Revit ElementId | Revit element ID. |

#### Opening Attributes

| Column | Type | Source | Description |
|---|---|---|---|
| `id` | TEXT (UUID) | Auto-generated, persisted to Revit | Stable unique identifier. |
| `category` | TEXT | Derived from element | Opening category (e.g., `pedestrian`). |
| `level_id` | TEXT (UUID) | From Level element's `IMDF_LevelId` | References the parent level. |
| `element_id` | INTEGER | Revit ElementId | Revit element ID. |

#### Level Attributes

| Column | Type | Source | Description |
|---|---|---|---|
| `id` | TEXT (UUID) | Auto-generated, persisted to Revit Level | Stable unique identifier (same as `IMDF_LevelId` on the Level element). |
| `level_name` | TEXT | Revit Level name | Display name of the level. |
| `ordinal` | INTEGER | Revit Level or manual | Floor ordering number (0 = ground, -1 = B1, 1 = 2F, etc.). |
| `elevation_m` | REAL | Revit Level elevation (converted from feet) | Elevation in meters. |

### Revit Shared Parameters Strategy

The add-in manages IMDF identity and metadata through Revit shared parameters. This ensures stable IDs across exports and allows human-curated fields to be edited directly in Revit.

#### Auto-managed parameters (created and populated by the add-in)

| Parameter Name | Type | Instance/Type | Applied To | Description |
|---|---|---|---|---|
| `IMDF_Id` | TEXT | Instance | Floors, Stairs, Generic Models (EV/escalator), Openings | UUID generated on first export, reused on subsequent exports. |
| `IMDF_LevelId` | TEXT | Instance | Levels | UUID generated on first export for each Level element. |

On first export, the add-in checks if these parameters exist in the shared parameter file. If not, it creates them. For each element, if `IMDF_Id` is empty or missing, a new UUID is generated and written back to the element within a Revit transaction. On subsequent exports, the existing UUID is read and used.

#### Human-curated parameters (created by the add-in, populated by users)

| Parameter Name | Type | Instance/Type | Applied To | Description |
|---|---|---|---|---|
| `IMDF_Name` | TEXT | Instance | Floors, Stairs, Generic Models (EV/escalator) | Display name for the unit. Only needed for named spaces (shops, restrooms, info desks). |
| `IMDF_AltName` | TEXT | Instance | Floors, Stairs, Generic Models (EV/escalator) | Alternative / English name. |

These are created by the add-in (if they don't exist) but left empty. Team members fill them in through Revit's Properties panel for elements that need labels. Empty values export as NULL.

#### Parameter creation workflow

1. On first run, the add-in checks for a shared parameter file at a configurable path (default: project-local `.txt` file).
2. If the IMDF parameters don't exist, the add-in creates them in the shared parameter file and binds them to the relevant Revit categories.
3. A Revit transaction is opened to write auto-generated UUIDs to elements that don't have one yet.
4. Subsequent exports skip UUID generation for elements that already have an `IMDF_Id`.

#### Phase 1 Feature Types

| File Suffix | Geometry Type | Source |
|---|---|---|
| `_unit` | MultiPolygon | Floor elements + elevator/escalator/stair families |

#### Phase 2 Feature Types

| File Suffix | Geometry Type | Source |
|---|---|---|
| `_unit` | MultiPolygon | Floor elements + elevator/escalator/stair families |
| `_detail` | LineString | Model Lines / stair step lines |
| `_opening` | LineString | Opening elements |
| `_level` | MultiPolygon | Derived (union of all units on the level) |

### File Geodatabase Conversion (Phase 4)

GeoPackage is the primary export format. For teams that need Esri File Geodatabase (.gdb), conversion is handled as a post-processing step using GDAL's `ogr2ogr`:

```bash
ogr2ogr -f "OpenFileGDB" output.gdb input.gpkg
```

Requires GDAL 3.6+ (which includes OpenFileGDB write support). This can be:
- A standalone batch/Python script that converts all GPKGs in an output folder.
- An optional checkbox in the export dialog ("Also export to File Geodatabase") that shells out to `ogr2ogr` after the GeoPackage export completes. Requires GDAL to be installed on the machine.

The add-in does not write .gdb directly to avoid native GDAL/Esri SDK dependencies in the Revit plugin environment.

### Coordinate System & Georeferencing

- **Project display units:** Meters (all models use metric).
- **Revit internal units:** The Revit API always returns geometry in feet regardless of display units. The add-in must convert feet → meters during export.
- Export CRS: Configurable, defaulting to **EPSG:6677** (JGD2011 / Japan Plane Rectangular CS IX — appropriate for Tokyo area stations). Other Japan Plane Rectangular zones should be selectable.
- The transform chain: Revit Internal Coords (feet) → meters → Shared Coordinates offset/rotation → Target CRS.
- **Requirement:** Models must have shared coordinates properly configured with a known real-world reference point. The add-in should validate this and warn if shared coordinates appear to be at origin (0,0).

### Zone Name Extraction

Floor type names follow the pattern `j {ZoneName}_床`. The add-in extracts the zone name by:
1. Getting the floor element's type name.
2. Stripping the `j ` prefix and `_床` suffix if present.
3. Using the result to look up `category` and `restrict` from the zone catalog.

The zone name itself is not exported — it is only used internally as a lookup key. If a floor type name does not match the expected pattern, the full type name is used as the lookup key and a warning is logged.

### Zone Catalog & IMDF Category Mapping

All spaces are exported as units. Each unit gets classification attributes derived from a zone catalog lookup:
- `category` — IMDF unit category (English, standardized).
- `restrict` — Fare gate restriction: `"rachi_nai"` (inside fare gates), `"rachi_gai"` (outside fare gates), or `null` (not applicable). Exported as column name `restrict` in the GeoPackage to align with IMDF conventions.

Colors are not exported — styling is handled downstream in QGIS via rules based on `category` and `restrict` values.

#### Floor-based Units (from floor type names)

| Zone Name (Japanese) | IMDF Category | Restriction |
|---|---|---|
| ラチ外コンコース | `walkway` | `rachi_gai` |
| ラチ内コンコース | `walkway` | `rachi_nai` |
| ラチ内コンコース(JR東新幹線) | `walkway` | `rachi_nai` |
| ラチ内コンコース(JR東海新幹線) | `walkway` | `rachi_nai` |
| ラチ内店舗 | `retail` | `rachi_nai` |
| ラチ外店舗 | `retail` | `rachi_gai` |
| 新幹線ホーム | `platform` | `rachi_nai` |
| 在来線ホーム | `platform` | `rachi_nai` |
| その他 | `unspecified` | `null` |
| 案内所 | `information` | `null` |
| みどりの窓口 | `ticketing` | `rachi_gai` |
| 道路 | `road` | `null` |
| 外構 | `outdoors` | `null` |
| 男子トイレ | `restroom.male` | `null` |
| 女子トイレ | `restroom.female` | `null` |
| 多目的トイレ | `restroom.unisex` | `null` |
| 券売機室 | `ticketing` | `null` |
| 待合室 | `waitingroom` | `null` |
| 店舗（他商業施設） | `retail` | `rachi_gai` |

#### Family-based Units (identified by Revit family name)

Stairs, escalators, and elevators are also units — their polygonal footprint is exported into the same `_unit` GeoPackage as all other spaces. They are identified by Revit family name rather than floor type name.

| Revit Family Name | IMDF Category | Identification Method |
|---|---|---|
| *(Stair elements not matching escalator family)* | `stairs` | Revit Stairs category, excluding escalator family |
| `j エスカレータ-lightweight` | `escalator` | Exact family name match |
| `j EV` | `elevator` | Exact family name match |

These units do not go through the zone name parser — their `category` and `restrict` are set directly from the family lookup. `restrict` is `null` as these elements exist across fare gate zones.

**Notes on IMDF category choices:**
- `walkway` covers all concourse areas (IMDF's term for pedestrian circulation paths).
- `platform` is used for train platforms. This is a custom extension category for rail station modeling.
- `stairs`, `escalator`, `elevator` are standard IMDF unit categories.
- `restroom.male`, `restroom.female`, `restroom.unisex` follow the IMDF restroom subcategory hierarchy.
- `restriction` is a custom attribute not part of IMDF, specific to the Japanese rail fare gate system. It is critical for navigation routing.

This catalog should be embedded in the add-in as a lookup table and be easily extensible (JSON config file or a static dictionary) for future projects.

## User Interface

### Ribbon Panel

A custom ribbon tab "GeoExporter" with:
- **Export GeoPackage** button — opens the export dialog.
- **Settings** button — opens CRS and project settings.

### Export Dialog

- Checklist of plan views to export (auto-populated from model, filtering for `ViewType.FloorPlan` and `ViewType.CeilingPlan`, excluding view templates). Shows view name and associated level.
- Checklist of feature types to include: `unit`, `detail`, `opening`, `level`.
- Output directory picker.
- CRS selector (dropdown of Japan Plane Rectangular zones + custom EPSG input).
- "Export" button.
- Progress bar with per-view status.

### Settings Dialog (Phase 2+)

- Default CRS.
- Default output directory.
- Zone name extraction settings (prefix `j ` and suffix `_床` stripping, customizable per project).
- Zone catalog overrides (add/modify zone name → IMDF category mappings).
- Family-to-category overrides (for custom elevator/escalator family names in other projects).

## Geometry Extraction

### Units (from Floor Elements)
- Use `Floor.GetBoundarySegments()` or extract from the element's geometry solid by finding the bottom face.
- Project all curves onto the level's elevation plane.
- Convert to 2D polygon (drop Z).
- Handle floors with openings/holes as polygons with interior rings.

### Units (from Elevator & Escalator Families)
- **Elevators** (`j EV`): Extract the solid geometry's footprint as a polygon. Category set to `elevator`.
- **Escalators** (`j エスカレータ-lightweight`): Extract the solid geometry's footprint as a polygon. Category set to `escalator`.
- **Stairs** (Revit Stairs category, excluding escalator family): Extract the solid footprint covering stair runs and landings as a polygon. Category set to `stairs`.
- For elements spanning multiple levels, include the footprint polygon in each relevant level's `_unit` export.
- These are all written into the same `_unit` GeoPackage alongside floor-based units.

### Details (LineString)
- Extract model lines and other lineal geometry that represent cosmetic detail (stair step markings, etc.).
- Output as LineString features with a reference to the level.

### Openings (LineString)
- Extract opening elements as lineal geometry following the IMDF Opening specification.
- Each opening is a line segment or multi-segment line representing the threshold/doorway.

### Level Boundary (MultiPolygon)
- Derived by computing the union of all unit polygons on the level.
- Alternatively, use the level's bounding geometry if available in the model.
- Includes `level_name`, `ordinal` (floor number), and `elevation_m`.

## Technology Stack

- **Language:** C# (.NET Framework — matching Revit's target framework)
- **Revit API:** For element querying, geometry extraction, coordinate transforms.
- **GeoPackage writing:** Use GDAL/OGR via `MaxRev.Gdal.Core` and `MaxRev.Gdal.LinuxRuntime.Minimal` / `MaxRev.Gdal.WindowsRuntime.Minimal` NuGet packages. Alternatively, write GeoPackage via raw SQLite (`System.Data.SQLite` or `Microsoft.Data.Sqlite`) using the GeoPackage spec directly — this avoids GDAL dependency complexity.
- **Coordinate transforms:** `ProjNet` (formerly ProjNET4GeoAPI) NuGet package for CRS transformations, or GDAL's `OSR` module if already using GDAL.
- **Testing:** NUnit or xUnit. Geometry extraction tests use mock Revit geometry or exported test data. GeoPackage output validated by reading back with GDAL/OGR or SQLite.

## Assumptions & Constraints

- Models follow the convention of zone-typed floor elements and standardized family names for elevators/escalators as described above.
- Shared coordinates are configured in the model.
- The add-in targets **Revit 2024** (`.NET Framework 4.8`). Revit API references must match the 2024 SDK.
- GeoPackage files are consumed downstream by QGIS and the navigation application backend.
- The add-in runs on Windows (Revit is Windows-only).

## Future Considerations

- **Phase 3 FBX export:** Trigger Revit's built-in FBX export or use a custom exporter for Unity-optimized meshes, from the same UI.
- **Navigation network data:** Define a modeling convention for navigation graphs in Revit (nodes/edges) and export as GeoPackage layers. Deferred — not needed for initial deployment.
- **Incremental export:** Detect changes since last export and only re-export affected levels.
- **Validation report:** Generate a summary of exported elements, warnings (missing zone names, ungeoreferenced models, elements on wrong levels), and statistics.
