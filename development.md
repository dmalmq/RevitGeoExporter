# RevitGeoExporter — Development Guide

## Project Setup

### Solution Structure

```
RevitGeoExporter/
├── RevitGeoExporter.sln
├── src/
│   ├── RevitGeoExporter/                  # Main Revit add-in project
│   │   ├── RevitGeoExporter.csproj
│   │   ├── App.cs                         # IExternalApplication — ribbon setup
│   │   ├── Commands/
│   │   │   └── ExportGeoPackageCommand.cs # IExternalCommand — entry point
│   │   ├── UI/
│   │   │   ├── ExportDialog.xaml/.cs      # WPF export dialog
│   │   │   └── SettingsDialog.xaml/.cs    # WPF settings dialog (Phase 2+)
│   │   ├── Extractors/
│   │   │   ├── UnitExtractor.cs           # Floors/families → unit polygons
│   │   │   ├── DetailExtractor.cs         # Model lines → detail LineStrings (Phase 2)
│   │   │   ├── OpeningExtractor.cs        # Opening elements → LineStrings (Phase 2)
│   │   │   └── LevelBoundaryBuilder.cs    # Union of units → level polygon (Phase 2)
│   │   ├── Core/
│   │   │   ├── CoordinateTransformer.cs   # Revit coords → CRS
│   │   │   ├── ViewCollector.cs           # Collect and filter plan views
│   │   │   ├── SharedParameterManager.cs  # Create/manage IMDF shared parameters & UUIDs
│   │   │   └── FileNaming.cs              # {ModelName}_{ViewName}_{feature_type}.gpkg
│   │   └── RevitGeoExporter.addin         # Revit add-in manifest
│   │
│   └── RevitGeoExporter.Core/            # Non-Revit-dependent logic (testable)
│       ├── RevitGeoExporter.Core.csproj
│       ├── Models/
│       │   ├── ExportFeature.cs           # 2D geometry with full IMDF attributes
│       │   ├── FeatureType.cs             # Enum: Unit, Detail, Opening, Level
│       │   ├── LayerDefinition.cs         # Feature type → geometry type + attribute schema
│       │   ├── ExportConfig.cs            # Export settings model
│       │   ├── ZoneNameParser.cs          # Floor type name → zone name
│       │   ├── ZoneCatalog.cs             # Zone name / family name → category + restriction
│       │   └── DisplayPointCalculator.cs  # Polygon → centroid point
│       ├── GeoPackage/
│       │   ├── GpkgWriter.cs              # GeoPackage file writer
│       │   ├── GpkgSchema.cs              # Table creation SQL (IMDF-compliant column definitions)
│       │   └── WkbEncoder.cs              # Geometry → WKB encoding (Polygon + LineString + Point)
│       └── Coordinates/
│           ├── CrsTransformer.cs          # CRS transformation logic
│           └── JapanPlaneRectangular.cs   # Japan-specific CRS definitions
│
├── tests/
│   ├── RevitGeoExporter.Core.Tests/       # Unit tests (no Revit dependency)
│   │   ├── RevitGeoExporter.Core.Tests.csproj
│   │   ├── GeoPackage/
│   │   │   ├── GpkgWriterTests.cs
│   │   │   └── WkbEncoderTests.cs
│   │   ├── Coordinates/
│   │   │   └── CrsTransformerTests.cs
│   │   └── Models/
│   │       ├── ZoneNameParserTests.cs
│   │       └── ZoneCatalogTests.cs
│   │
│   └── RevitGeoExporter.Integration.Tests/ # Integration tests (require Revit or test models)
│       ├── RevitGeoExporter.Integration.Tests.csproj
│       └── GpkgOutputTests.cs             # Validate GPKG files in QGIS/GDAL
│
├── testdata/                              # Sample data for tests
│   ├── sample_polygons.json               # Known polygons for round-trip tests
│   └── expected/                          # Expected GPKG outputs for comparison
│
├── tools/
│   └── convert_to_gdb.py                 # GPKG → File Geodatabase conversion script
│
└── docs/
    ├── spec.md
    └── development.md
```

### Key Design Decisions

**Two-project split:** `RevitGeoExporter` depends on the Revit API and can only be tested inside Revit. `RevitGeoExporter.Core` has zero Revit dependencies — all GeoPackage writing, WKB encoding, coordinate transforms, and data models live here and are fully unit-testable without Revit installed.

**GeoPackage via raw SQLite:** Rather than depending on GDAL (which has complex native binary dependencies), write GeoPackage files using `Microsoft.Data.Sqlite`. GeoPackage is just a SQLite database with a specific schema and WKB-encoded geometry. This keeps the dependency chain simple and avoids GDAL DLL hell in the Revit plugin environment.

**WKB encoding:** Implement a minimal Well-Known Binary encoder for Polygon and MultiPolygon types. This is straightforward (~200 lines) and avoids pulling in a full geometry library.

**ProjNet for CRS:** Use the `ProjNet` NuGet package for coordinate system transforms. It's pure C#, no native dependencies, and handles the Japan Plane Rectangular projections well.

### NuGet Dependencies

| Package | Project | Purpose |
|---|---|---|
| `Microsoft.Data.Sqlite` | Core | GeoPackage (SQLite) writing |
| `ProjNet` | Core | CRS coordinate transformations |
| `Newtonsoft.Json` | Core | Config serialization (Revit already bundles this) |
| `xunit` | Tests | Test framework |
| `xunit.runner.visualstudio` | Tests | Test runner |

### Revit API References

The main add-in project references (from Revit install directory):
- `RevitAPI.dll`
- `RevitAPIUI.dll`

Set **Copy Local = false** for these — they're loaded by Revit at runtime.

### Target Framework

**Revit 2024 → .NET Framework 4.8**

Both `RevitGeoExporter` and `RevitGeoExporter.Core` target `net48`. Use `Microsoft.Data.Sqlite` version 7.x (last version supporting .NET Framework 4.8 via .NET Standard 2.0). ProjNet also supports .NET Standard 2.0 so it works on 4.8.

---

## Phase 1: Units to GeoPackage

### Goal
Export unit polygons (floor-based spaces + stairs/escalators/elevators) for selected plan views as georeferenced polygons in GeoPackage files, with IMDF categories and zone attributes.

### Scope
- Unit feature type only (floor elements + elevator/escalator/stair families).
- One GPKG per selected view, named `{ModelName}_{ViewName}_unit.gpkg`.
- Zone name extraction from floor type name (`j {ZoneName}_床` pattern) with IMDF category and restriction lookup.
- Family-based identification for stairs (`stairs`), escalators (`escalator`), and elevators (`elevator`) categories.
- CRS hardcoded to EPSG:6677 (configurable later).
- Minimal UI: a command that lets the user select plan views and an output folder.

### Implementation Steps

#### 1.1 — Core: WKB Encoder
File: `Core/GeoPackage/WkbEncoder.cs`

Encode 2D polygons (exterior ring + optional interior rings) to OGC Well-Known Binary.

**Format (little-endian):**
```
byte    byteOrder (01 = little-endian)
uint32  wkbType (03 = Polygon, 06 = MultiPolygon)
uint32  numRings
  uint32  numPoints
  double  x, y (per point)
```

GeoPackage uses a standard header prepended to the WKB:
```
bytes   "GP"  (magic)
byte    version (0)
byte    flags
int32   srs_id
[envelope - optional]
[WKB payload]
```

#### 1.2 — Core: GeoPackage Writer
File: `Core/GeoPackage/GpkgWriter.cs`

Create a valid GeoPackage file:
1. Create SQLite database.
2. Write `gpkg_spatial_ref_sys` table with the target CRS.
3. Write `gpkg_contents` table entry per layer.
4. Write `gpkg_geometry_columns` entry per layer.
5. Create layer table using the `LayerDefinition` for the feature type. For units, the table schema is:

```sql
CREATE TABLE unit (
    fid INTEGER PRIMARY KEY AUTOINCREMENT,
    geom BLOB,                -- GeoPackage-header-wrapped WKB (MultiPolygon)
    id TEXT NOT NULL,          -- UUID
    category TEXT NOT NULL,    -- IMDF unit category
    restrict TEXT,             -- rachi_nai / rachi_gai / NULL
    name TEXT,                 -- Human display name (nullable)
    alt_name TEXT,             -- Alternative name (nullable)
    level_id TEXT NOT NULL,    -- UUID referencing the Level
    source TEXT,               -- Data source identifier
    display_point TEXT         -- WKT Point for label placement
);
```

6. Insert features with GeoPackage-header-wrapped WKB geometry and all attribute values.

#### 1.3 — Core: Zone Name Parser & Color Lookup
File: `Core/Models/ZoneNameParser.cs`

Floor type names follow the pattern `j {ZoneName}_床`. Parse by stripping both prefix and suffix:
```csharp
public static string Parse(string typeName)
{
    var name = typeName;
    if (name.StartsWith("j "))
        name = name.Substring(2);
    if (name.EndsWith("_床"))
        name = name.Substring(0, name.Length - 2);
    return name;
}
```

File: `Core/Models/ZoneCatalog.cs`

Static lookup of zone name → IMDF category and fare gate restriction. Colors are handled downstream in QGIS via styling rules.

```csharp
public record ZoneInfo(string Category, string Restriction);

public static class ZoneCatalog
{
    public static readonly Dictionary<string, ZoneInfo> Zones = new()
    {
        ["ラチ外コンコース"]              = new("walkway",          "rachi_gai"),
        ["ラチ内コンコース"]              = new("walkway",          "rachi_nai"),
        ["ラチ内コンコース(JR東新幹線)"]   = new("walkway",          "rachi_nai"),
        ["ラチ内コンコース(JR東海新幹線)"] = new("walkway",          "rachi_nai"),
        ["ラチ内店舗"]                    = new("retail",           "rachi_nai"),
        ["ラチ外店舗"]                    = new("retail",           "rachi_gai"),
        ["新幹線ホーム"]                  = new("platform",         "rachi_nai"),
        ["在来線ホーム"]                  = new("platform",         "rachi_nai"),
        ["その他"]                       = new("unspecified",       null),
        ["案内所"]                       = new("information",       null),
        ["みどりの窓口"]                  = new("ticketing",        "rachi_gai"),
        ["道路"]                         = new("road",             null),
        ["外構"]                         = new("outdoors",         null),
        ["男子トイレ"]                    = new("restroom.male",    null),
        ["女子トイレ"]                    = new("restroom.female",  null),
        ["多目的トイレ"]                  = new("restroom.unisex",  null),
        ["券売機室"]                      = new("ticketing",        null),
        ["待合室"]                       = new("waitingroom",       null),
        ["店舗（他商業施設）"]             = new("retail",           "rachi_gai"),
    };

    public static readonly ZoneInfo Default = new("unspecified", null);

    public static ZoneInfo Lookup(string zoneName)
    {
        return Zones.TryGetValue(zoneName, out var info) ? info : Default;
    }
}
```

```csharp
public class ZoneInfo
{
    public string Category { get; }
    public string Restriction { get; }

    public ZoneInfo(string category, string restriction)
    {
        Category = category;
        Restriction = restriction;
    }
}
```

The catalog includes two lookup paths:

**Floor-based lookup** (by zone name parsed from floor type name):
Returns `Default` and logs a warning if the zone name is not found.

**Family-based lookup** (by Revit family name, for stairs/escalators/elevators):
```csharp
public static readonly Dictionary<string, ZoneInfo> FamilyLookup = new Dictionary<string, ZoneInfo>
{
    ["j EV"] = new ZoneInfo("elevator", null),
    ["j エスカレータ-lightweight"] = new ZoneInfo("escalator", null),
};

// Stair elements that don't match any family in FamilyLookup
// default to category "stairs"
public static readonly ZoneInfo StairsDefault = new ZoneInfo("stairs", null);
```

The catalog can later be externalized to a JSON config file for project-specific customization.

#### 1.4 — Core: CRS Transformer
File: `Core/Coordinates/CrsTransformer.cs`

Transform from Revit internal coordinates to the target CRS. Steps:
1. Convert feet → meters (Revit API always returns feet internally, even though the project display units are meters). Multiply by `0.3048`.
2. Apply the shared coordinate offset and rotation (passed in from Revit side).
3. If the shared coordinates are already in the target CRS (meters), no projection is needed — just the unit conversion and offset.
4. If shared coordinates are in WGS84 lat/lon, project to the target CRS using ProjNet.

#### 1.5 — Add-in: Shared Parameter Manager
File: `RevitGeoExporter/Core/SharedParameterManager.cs`

Manages IMDF shared parameters in Revit. Called once at the start of each export.

```csharp
// Parameters to create/verify
// Auto-managed:
//   IMDF_Id       (TEXT, Instance) → Floors, Stairs, GenericModels, Openings
//   IMDF_LevelId  (TEXT, Instance) → Levels
// Human-curated:
//   IMDF_Name     (TEXT, Instance) → Floors, Stairs, GenericModels
//   IMDF_AltName  (TEXT, Instance) → Floors, Stairs, GenericModels
```

Steps:
1. Check for existing shared parameter file at configurable path. Create if missing.
2. For each parameter definition, check if it exists in the file and is bound to the correct categories.
3. If any parameter is missing, create it and bind to the relevant Revit categories within a transaction.
4. This is idempotent — safe to run on every export.

UUID assignment (called per-element during export):
```csharp
public static string GetOrCreateId(Element element)
{
    var param = element.LookupParameter("IMDF_Id");
    if (param != null && !string.IsNullOrEmpty(param.AsString()))
        return param.AsString();

    var newId = Guid.NewGuid().ToString();
    // Must be inside an active transaction
    param.Set(newId);
    return newId;
}
```

Same pattern for `IMDF_LevelId` on Level elements.

#### 1.6 — Add-in: Unit Extractor
File: `RevitGeoExporter/Extractors/UnitExtractor.cs`

Extracts unit polygons from three sources on a given level:

**Floor elements** (zone-based units):
1. Get the element's geometry (`element.get_Geometry(options)` with `Options.DetailLevel = ViewDetailLevel.Fine`).
2. Traverse the `GeometryElement` to find `Solid` objects.
3. For each solid, find the bottom `PlanarFace` (face with normal pointing downward, i.e., normal Z ≈ -1).
4. Extract the face's `EdgeLoops` — the outer loop becomes the exterior ring, any inner loops become interior rings (holes).
5. Convert each `XYZ` point to 2D by dropping the Z coordinate.
6. Apply the coordinate transform from 1.4.
7. Parse zone name via `ZoneNameParser`, look up `ZoneCatalog` for category and restriction.
8. Read or generate `IMDF_Id` via `SharedParameterManager.GetOrCreateId()`.
9. Read `IMDF_Name` and `IMDF_AltName` (may be null/empty).
10. Compute `display_point` as the centroid of the polygon.
11. Package as an `ExportFeature` with all attributes.

**Fallback:** If `GetBoundarySegments()` works reliably for the floor types used, prefer it over solid geometry traversal — it's simpler and gives cleaner boundary curves.

**Elevator/escalator families** (`j EV`, `j エスカレータ-lightweight`):
1. Query family instances by exact family name using `FilteredElementCollector`.
2. Extract solid geometry footprint (bottom face projection) as polygon.
3. Look up `ZoneCatalog.FamilyLookup` for category.
4. Read or generate `IMDF_Id`, read `IMDF_Name` / `IMDF_AltName`.

**Stair elements** (excluding escalator family):
1. Query Stairs category elements, filter out any matching the escalator family name.
2. Extract solid footprint covering stair runs and landings.
3. Assign category `stairs` via `ZoneCatalog.StairsDefault`.
4. For stairs/escalators/elevators spanning multiple levels, include the footprint in each relevant level.

#### 1.7 — Add-in: Export Command
File: `RevitGeoExporter/Commands/ExportGeoPackageCommand.cs`

1. Collect all plan views from the model (filter for `ViewType.FloorPlan`, exclude templates and non-exportable views).
2. Show a simple selection dialog: checklist of plan views (name + associated level), output folder picker.
3. Get the model name from `Document.Title`.
4. Ensure shared parameters exist via `SharedParameterManager.EnsureParameters()`.
5. Open a Revit transaction (`"IMDF Export - Assign IDs"`).
6. Generate `IMDF_LevelId` for all Level elements referenced by selected views that don't have one yet.
7. For each selected view:
   a. Get the view's associated level.
   b. Query all Floor elements visible in that view (using `FilteredElementCollector` with the view ID — this respects visibility/graphics overrides and crop regions).
   c. Query elevator/escalator family instances and stair elements visible in that view.
   d. Extract geometry and attributes via `UnitExtractor` (which generates `IMDF_Id` for new elements).
   e. Look up IMDF category and restriction via `ZoneCatalog` (floor-based or family-based).
   f. Write to `{ModelName}_{ViewName}_unit.gpkg` using `GpkgWriter`.
8. Commit the transaction (UUIDs are now persisted in the model).
9. Show completion message with count of exported units per view.

**Important:** Using `FilteredElementCollector(document, viewId)` instead of just `FilteredElementCollector(document)` ensures that only elements visible in the selected view are exported. This means view-specific visibility overrides, filters, and crop regions are respected — the export matches what the user sees in that view.

### Tests — Phase 1

#### Unit Tests (no Revit required)

| Test | What it validates |
|---|---|
| `WkbEncoder_SimpleSquare` | Encode a 4-point polygon, decode with GDAL or a known-good WKB reader, verify coordinates match. |
| `WkbEncoder_PolygonWithHole` | Encode polygon with interior ring. Verify ring count and coordinates. |
| `WkbEncoder_GpkgHeader` | Verify the GeoPackage binary header is correctly prepended (magic bytes, SRS ID, envelope). |
| `GpkgWriter_CreatesValidFile` | Write a GPKG with one layer and one feature with full IMDF unit schema. Open with `Microsoft.Data.Sqlite` and verify: `gpkg_spatial_ref_sys` has the CRS, `gpkg_contents` has the layer, `gpkg_geometry_columns` references it, layer table has the row with all columns. |
| `GpkgWriter_MultipleFeatures` | Write 10 features to a layer, read back, verify count and attributes. |
| `GpkgWriter_UnitSchema` | Verify the unit table has all required columns: `id`, `category`, `restrict`, `name`, `alt_name`, `level_id`, `source`, `display_point`, and geometry. |
| `GpkgWriter_NullableFields` | Write a feature with `name`, `alt_name`, `restrict` set to NULL. Read back, verify NULLs are preserved. |
| `GpkgWriter_ValidatesInQGIS` | Write a test GPKG, open it with GDAL/OGR (via command-line `ogrinfo`), confirm it's recognized as valid. This is an integration-level test but can run in CI if GDAL CLI is available. |
| `UuidGenerator_Format` | Generated UUIDs are valid RFC 4122 format. |
| `UuidGenerator_Unique` | 1000 generated UUIDs are all distinct. |
| `DisplayPoint_Centroid` | Compute centroid of a known polygon, verify coordinates. |
| `DisplayPoint_ConcavePolygon` | Centroid of a concave polygon falls inside the polygon boundary. |
| `ZoneNameParser_StandardPattern` | `"j ラチ内コンコース_床"` → `"ラチ内コンコース"` |
| `ZoneNameParser_NoSuffix` | `"SomeOtherType"` → `"SomeOtherType"` (fallback, logs warning) |
| `ZoneNameParser_WithParentheses` | `"j ラチ内コンコース(JR東新幹線)_床"` → `"ラチ内コンコース(JR東新幹線)"` |
| `ZoneNameParser_NoPrefixOrSuffix` | `"CustomFloor"` → `"CustomFloor"` |
| `ZoneCatalog_KnownZone` | `"ラチ内コンコース"` → category `"walkway"`, restriction `"rachi_nai"` |
| `ZoneCatalog_UnknownZone` | `"未知のゾーン"` → default: category `"unspecified"`, restriction `null` |
| `ZoneCatalog_AllEntries` | Verify all 19 catalog entries return valid ZoneInfo with non-null category. |
| `ZoneCatalog_RestroomCategories` | Verify 男子/女子/多目的 map to `restroom.male`/`.female`/`.unisex` respectively. |
| `ZoneCatalog_RestrictionValues` | All ラチ内 zones return `"rachi_nai"`, all ラチ外 zones return `"rachi_gai"`, others return `null`. |
| `ZoneCatalog_FamilyLookup_Elevator` | `"j EV"` → category `"elevator"`. |
| `ZoneCatalog_FamilyLookup_Escalator` | `"j エスカレータ-lightweight"` → category `"escalator"`. |
| `ZoneCatalog_StairsDefault` | Stair default returns category `"stairs"`. |
| `CrsTransformer_FeetToMeters` | Verify `1.0 ft × 0.3048 = 0.3048 m`. Revit API always returns feet even though project uses metric display units. |
| `CrsTransformer_OffsetAndRotation` | Apply known offset/rotation, verify output coordinates. |

#### Manual Validation (requires Revit)

| Test | How to validate |
|---|---|
| Open exported GPKG in QGIS | Unit polygons should appear, IMDF categories and restriction values should populate the attribute table, geometry should look correct relative to known station layout. |
| Compare with existing shapefiles | Overlay exported GPKG on current shapefiles — geometries should align. |
| Check CRS | In QGIS, verify the layer's CRS is EPSG:6677 (or whatever was configured). |
| Spot-check categories | Verify a sample of floor elements have correct IMDF categories and restriction values in the attribute table. |
| Verify stairs/elevators/escalators | These should appear as unit polygons with categories `stairs`, `elevator`, `escalator`. |
| Check file naming | Exported files should follow `{ModelName}_{ViewName}_unit.gpkg` pattern. |
| UUID stability | Export twice. Verify `id` values are identical across both exports for the same elements. |
| UUID in Revit | After export, check that `IMDF_Id` parameter is visible and populated in Revit's Properties panel. |
| Name/AltName round-trip | Set `IMDF_Name` on a floor element in Revit, export, verify `name` column has the value. |

### Definition of Done — Phase 1
- [ ] All unit tests pass.
- [ ] Exported GPKG opens in QGIS without errors.
- [ ] Unit polygons match existing shapefile geometry (visual overlay check).
- [ ] IMDF categories correctly assigned for all floor types, stairs, escalators, and elevators.
- [ ] Stairs, escalators, and elevators appear as units with correct categories.
- [ ] Restriction attribute correctly set for ラチ内/ラチ外 zones.
- [ ] File naming follows `{ModelName}_{ViewName}_unit.gpkg` convention.
- [ ] Only elements visible in the selected view are exported.
- [ ] All IMDF columns present: `id`, `category`, `restrict`, `name`, `alt_name`, `level_id`, `source`, `display_point`.
- [ ] UUIDs are stable across repeated exports (persisted to `IMDF_Id` shared parameter).
- [ ] `IMDF_Name` and `IMDF_AltName` shared parameters are created and editable in Revit.
- [ ] CRS is correct in the output file.
- [ ] Works on at least one real station model.

---

## Phase 2: Detail, Opening & Level Feature Types

### Goal
Export all remaining IMDF feature types: detail lines, openings, and level boundaries alongside units.

### Scope
- Add detail extraction (stair step lines and other cosmetic model lines).
- Add opening extraction (lineal geometry from opening elements).
- Add level boundary generation (union of all units or explicit boundary).
- Full UI: WPF export dialog with view and feature type selection.
- Add settings persistence.
- All outputs follow the naming convention: `{ModelName}_{ViewName}_{feature_type}.gpkg`.

### Implementation Steps

#### 2.1 — Detail Geometry Extraction
- Query model lines and detail lines associated with stair elements and other cosmetic features.
- Extract as LineString geometry.
- Each detail gets `element_id` and `level_id` attributes.
- Write to `{ModelName}_{ViewName}_detail.gpkg`.

#### 2.2 — Opening Geometry Extraction
- Query opening elements using `FilteredElementCollector`.
- Extract the opening as lineal geometry (line segment along the threshold) following the IMDF Opening spec.
- Alternatively, find openings by querying opening elements directly via `FilteredElementCollector`.
- Capture `category` (e.g., `pedestrian`, `emergencyexit`) if determinable from the element.
- Write to `{ModelName}_{ViewName}_opening.gpkg`.

#### 2.3 — Level Boundary Generation
- Compute the union of all unit polygons on a given level to derive the level boundary.
- Alternatively, if the model has an explicit level boundary element, extract it.
- Attributes: `level_name`, `ordinal` (floor number), `elevation_m`.
- Write to `{ModelName}_{ViewName}_level.gpkg`.

#### 2.4 — WKB Encoder: LineString Support
- Extend the WKB encoder to handle LineString geometry type.
- WKB type code: `02` for LineString.
- Needed for both detail and opening layers.

#### 2.5 — Export Dialog (WPF)
- Plan view checklist with select all / deselect all (auto-populated, shows view name + associated level).
- Feature type checklist: `unit`, `detail`, `opening`, `level`.
- Output folder picker.
- CRS dropdown: Japan Plane Rectangular zones I–XIX + custom EPSG.
- Export button with progress bar.
- Save last-used settings (including last selected views) to `%AppData%/RevitGeoExporter/settings.json`.

#### 2.6 — Layer Definition System
- `LayerDefinition` class defines: feature type name, geometry type (Polygon, LineString), attribute schema.
- One `LayerDefinition` per feature type.
- `GpkgWriter` uses `LayerDefinition` to create tables and insert features.

### Tests — Phase 2

#### Unit Tests

| Test | What it validates |
|---|---|
| `WkbEncoder_LineString` | Encode a LineString geometry, verify coordinates round-trip correctly. |
| `GpkgWriter_LineStringLayer` | Write a GPKG with a LineString layer. Verify table exists and geometry column type is correct. |
| `GpkgWriter_MultipleFeatureTypes` | Write unit (Polygon) and detail (LineString) layers in separate GPKGs. Verify both are valid. |
| `LayerDefinition_SchemaCreation` | Verify SQL table creation matches expected schema for each feature type. |

#### Manual Validation

| Test | How to validate |
|---|---|
| All feature types visible in QGIS | Open all 4 GPKGs for a level, verify unit polygons, detail lines, openings, and level boundary appear correctly. |
| Detail lines inside stair units | Stair step lines should visually sit inside stair unit polygons. |
| Openings align with unit boundaries | Openings should appear at unit polygon edges. |
| Level boundary encompasses all units | The level polygon should contain all unit polygons. |
| Settings persist | Close and reopen the dialog — last-used values should restore. |

### Definition of Done — Phase 2
- [ ] All Phase 1 tests still pass.
- [ ] All new unit tests pass.
- [ ] Export dialog allows view and feature type selection.
- [ ] All 4 feature types export correctly for at least one real model.
- [ ] File naming follows `{ModelName}_{ViewName}_{feature_type}.gpkg` convention.
- [ ] Settings persist between sessions.
- [ ] Output validated in QGIS with all feature types.

---

## Phase 3: Unified Export (GeoPackage + FBX)

### Goal
Single export action produces both GeoPackage files (for GIS/navigation backend) and FBX (for Unity 3D navigation).

### Scope
- Add FBX export trigger to the export dialog.
- FBX uses Revit's built-in exporter or custom IExportContext.
- Shared UI for selecting what to export.

### Implementation Steps

#### 3.1 — FBX Export Integration
- Use Revit's `Document.Export()` with `FBXExportOptions`.
- Or implement `IExportContext` for more control over mesh output.
- Export one FBX per level (matching GPKG structure) or one FBX for the whole model (depending on Unity pipeline needs).

#### 3.2 — Unified Export Dialog
- Add checkboxes: "Export GeoPackage" / "Export FBX".
- Add FBX-specific options (file-per-level vs single file, mesh detail level).
- Single "Export" button triggers both.

#### 3.3 — Export Pipeline
- Run GeoPackage export first, then FBX.
- Shared progress bar.
- Summary dialog showing all exported files.

### Tests — Phase 3

| Test | What it validates |
|---|---|
| FBX files created | Verify FBX files exist in output directory with expected names. |
| Both formats exported | Single export produces both GPKG and FBX files. |
| FBX loads in Unity | Manual test — import FBX into Unity project. |

### Definition of Done — Phase 3
- [ ] All Phase 1–2 tests still pass.
- [ ] Single export action produces both GPKG and FBX outputs.
- [ ] FBX files import correctly into Unity.
- [ ] Export dialog clearly shows what will be exported.

---

## Phase 4: File Geodatabase Conversion

### Goal
Optionally convert exported GeoPackages to Esri File Geodatabase (.gdb) format for teams that use ArcGIS.

### Scope
- Standalone conversion script (batch/Python) using GDAL's `ogr2ogr`.
- Optional integration into the export dialog as a checkbox.
- Requires GDAL 3.6+ installed on the machine (for OpenFileGDB write support).

### Implementation Steps

#### 4.1 — Standalone Conversion Script
File: `tools/convert_to_gdb.py` (or `.bat`)

```python
import subprocess
import sys
from pathlib import Path

def convert_folder(gpkg_dir, gdb_dir=None):
    """Convert all .gpkg files in a directory to .gdb"""
    gpkg_dir = Path(gpkg_dir)
    gdb_dir = Path(gdb_dir) if gdb_dir else gpkg_dir / "gdb"
    gdb_dir.mkdir(exist_ok=True)

    for gpkg in gpkg_dir.glob("*.gpkg"):
        gdb_path = gdb_dir / gpkg.stem  # .gdb extension added by ogr2ogr
        subprocess.run([
            "ogr2ogr", "-f", "OpenFileGDB",
            str(gdb_path) + ".gdb",
            str(gpkg)
        ], check=True)
        print(f"Converted: {gpkg.name} -> {gdb_path.name}.gdb")

if __name__ == "__main__":
    convert_folder(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
```

Usage:
```bash
python tools/convert_to_gdb.py ./exports/ ./exports/gdb/
```

#### 4.2 — Export Dialog Integration (Optional)
- Add checkbox to export dialog: "Also export to File Geodatabase (.gdb)".
- After GeoPackage export completes, shell out to `ogr2ogr` via `Process.Start()`.
- Detect if GDAL/ogr2ogr is available on PATH. If not, show a warning with install instructions.
- Run conversion for each exported GPKG.

```csharp
var process = Process.Start(new ProcessStartInfo
{
    FileName = "ogr2ogr",
    Arguments = $"-f \"OpenFileGDB\" \"{gdbPath}\" \"{gpkgPath}\"",
    UseShellExecute = false,
    RedirectStandardError = true,
    CreateNoWindow = true
});
```

### Tests — Phase 4

| Test | What it validates |
|---|---|
| Script converts single GPKG | Run script on a test GPKG, verify .gdb is created and readable by `ogrinfo`. |
| Script converts folder | Run on a folder with multiple GPKGs, verify all are converted. |
| Attributes preserved | Open converted .gdb, verify all columns and values match the source GPKG. |
| GDAL not found handling | If ogr2ogr is not on PATH, script/add-in shows a clear error message. |

### Definition of Done — Phase 4
- [ ] Standalone script converts GPKGs to .gdb successfully.
- [ ] Converted .gdb files open in ArcGIS / QGIS with all attributes intact.
- [ ] Geometry and CRS are preserved in conversion.
- [ ] Clear error messaging when GDAL is not installed.

---

## Development Workflow with Claude Code

### Getting Started
1. Create the solution and project structure as defined above.
2. Start with `RevitGeoExporter.Core` — it has no Revit dependency and is fully testable locally.
3. Write the WKB encoder and GeoPackage writer first (Phase 1, steps 1.1–1.2).
4. Write tests alongside each component.
5. Only then move to the Revit add-in project to wire up geometry extraction.

### Testing Strategy
- **Unit tests** in `RevitGeoExporter.Core.Tests` run with `dotnet test` — no Revit needed.
- **Manual Revit testing:** Build the add-in, copy DLLs to the Revit add-ins folder, launch Revit, test on a sample model.
- **GPKG validation:** Use `ogrinfo` or `ogr2ogr` (GDAL command-line tools) to validate output files programmatically in CI or locally.
- **QGIS visual validation:** Open exported GPKGs in QGIS for visual spot checks after each phase.

### Build & Deploy

#### Building
```bash
# Build the solution
dotnet build src/RevitGeoExporter.sln

# Run core tests
dotnet test tests/RevitGeoExporter.Core.Tests/
```

#### Add-in Manifest

Create a `.addin` manifest file at:
```
%AppData%\Autodesk\Revit\Addins\2024\RevitGeoExporter.addin
```

Contents:
```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>RevitGeoExporter</Name>
    <Assembly>C:\path\to\repo\src\RevitGeoExporter\bin\Debug\net48\RevitGeoExporter.dll</Assembly>
    <FullClassName>RevitGeoExporter.App</FullClassName>
    <AddInId>GENERATE-A-GUID-HERE</AddInId>
    <VendorId>YourCompany</VendorId>
    <VendorDescription>Your Company Name</VendorDescription>
  </AddIn>
</RevitAddIns>
```

- **Assembly path:** Point directly to your build output folder so you don't need to copy DLLs after every rebuild.
- **AddInId:** Generate once with PowerShell (`[guid]::NewGuid()`) and keep it constant. Changing it makes Revit treat it as a different add-in.
- **FullClassName:** Must match your `IExternalApplication` implementation in `App.cs`.

#### Dependency DLLs

All NuGet dependency DLLs (`Microsoft.Data.Sqlite`, `ProjNet`, `SQLitePCLRaw`, etc.) must end up in the same folder as `RevitGeoExporter.dll`. The default build output handles this if NuGet references are configured correctly. If Revit throws `FileNotFoundException` at runtime for a dependency, check that the DLL is present in the build output folder.

Do **not** copy `RevitAPI.dll` or `RevitAPIUI.dll` — these are set to `Copy Local = false` and are loaded by Revit itself at runtime.

#### Development Cycle

1. Build the solution (`dotnet build` or build in Visual Studio).
2. Launch Revit 2024 — it reads the `.addin` file on startup and loads the DLL.
3. The "GeoExporter" ribbon tab and buttons should appear.
4. Test on a model.
5. To update after code changes: **close Revit**, rebuild, relaunch. Revit locks the DLL while running so hot-reload is not possible.

#### Security Warning

On first load Revit will show a security dialog asking whether to trust the add-in. Click **"Always Load"** to suppress it for future sessions. Alternatively, sign the assembly to avoid the prompt entirely (not necessary during development).

### Branching
- `main` — stable, tested.
- `phase/1-floor-export` — Phase 1 development.
- `phase/2-all-categories` — Phase 2 development.
- etc.
