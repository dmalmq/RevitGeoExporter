# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

RevitGeoExporter is a Revit add-in that exports floor / ceiling plan views to IMDF-style GeoPackage (or Shapefile) files. Default Revit target is **Revit 2024**, but the build is parameterized by Revit year so other versions can be built from the same source without forking.

The add-in registers a `GeoExporter` ribbon tab with `Export GeoPackage` and `Help` buttons (`src/RevitGeoExporter/App.cs`). The export entry point is `ExportGeoPackageCommand` → `ExportWorkflowCoordinator`.

## Commands

All builds run on Windows with .NET Framework 4.8 (TFM `net48`). PowerShell scripts assume `pwsh` (PS 7+).

```powershell
# Build the add-in for a specific Revit year (defaults to 2024)
dotnet build src/RevitGeoExporter/RevitGeoExporter.csproj -p:RevitYear=2024

# Override the Revit API DLL location (default: C:\Program Files\Autodesk\Revit <year>)
dotnet build src/RevitGeoExporter/RevitGeoExporter.csproj -p:RevitYear=2026 -p:RevitApiDir="C:\Program Files\Autodesk\Revit 2026"

# Run all tests (xUnit)
dotnet test RevitGeoExporter.sln

# Run a single test class or method
dotnet test --filter "FullyQualifiedName~ExportFingerprintBuilderTests"
dotnet test --filter "FullyQualifiedName=RevitGeoExporter.Core.Tests.Diagnostics.ExportFingerprintBuilderTests.ComputeLayerFingerprint_IsDeterministicAcrossFeatureOrder"

# Build the redistributable dist payload (no installer)
pwsh ./install/build-release.ps1 -RevitYear 2024

# Build the Inno Setup installer EXE (requires Inno Setup 6 / ISCC.exe)
pwsh ./install/build-installer.ps1 -RevitYear 2024

# Direct admin install from build output (dev convenience, no installer)
pwsh ./install/install.ps1 -RevitYear 2024
```

Notes:
- `RevitYear` MSBuild property also defines a preprocessor symbol `REVIT<year>` (e.g. `REVIT2024`) usable from `#if`.
- The `RevitAPI` / `RevitAPIUI` references are conditional on the DLLs existing at `$(RevitApiDir)`. Without Revit installed, the add-in project will compile against missing references and most code will fail — `RevitGeoExporter.Core` and most tests still build.
- After install/uninstall, **Revit must be restarted**. The add-in manifest goes to `C:\ProgramData\Autodesk\Revit\Addins\<year>\RevitGeoExporter.addin`; payload to `…\<year>\RevitGeoExporter\`.
- Emergency fallback to the legacy WinForms UI: set environment variable `REVIT_GEOEXPORTER_FORCE_LEGACY_WINFORMS_UI=1` before launching Revit. WPF is the default for the export dialog and preview window.

## Architecture

### Project split (this is the load-bearing rule)

- **`src/RevitGeoExporter.Core/`** — Revit-API-free library. Geometry (NetTopologySuite), GeoPackage writing (Microsoft.Data.Sqlite + custom WKB), Shapefile writing (NetTopologySuite.IO.ShapeFile), CRS handling (ProjNet), validation, diagnostics, schema profiles, assignment/override stores, preview math. Everything in here is unit-testable without a Revit install.
- **`src/RevitGeoExporter/`** — The add-in itself. Anything that touches `Autodesk.Revit.DB` / `Autodesk.Revit.UI` (extractors, ribbon, commands, dialogs, geometry harvesting from Revit) lives here. References Core.
- **`tests/RevitGeoExporter.Core.Tests/`** — xUnit. References *both* Core and the add-in project; tests largely target Core but a few sit in the `Export\` namespace and exercise add-in classes that don't actually require the Revit API at runtime.

**Always prefer adding new logic to `RevitGeoExporter.Core` and keeping the Revit project as a thin adapter.** This is how the codebase keeps the export pipeline testable.

### Export pipeline (top to bottom)

1. `App.OnStartup` registers the ribbon (`src/RevitGeoExporter/App.cs`).
2. `ExportGeoPackageCommand.Execute` collects exportable plan views (`ViewCollector`), loads settings (`SettingsBundle`), shows the export dialog (WPF `ExportDialogWpf`, or legacy `ExportDialog` under the env-var fallback), and hands the result to `ExportWorkflowCoordinator.RunExport`.
3. `ExportWorkflowCoordinator.RunExport` runs the readiness/validation/preview loop and calls into `FloorGeoPackageExporter`:
   - `PrepareExport` — builds `ViewExportContext`s (one per selected view, optionally including linked-model contexts) via `ViewExportContextProvider`, ensures shared parameters + stable IDs in a Revit transaction (`SharedParameterManager` / `StableIdGenerator`), then runs `FloorExportDataPreparer.PrepareViews` to produce per-view `ExportLayer`s (`unit`, `detail`, `opening`, `level`, `fixture`).
   - `WritePreparedExport` — plans output artifacts per `PackagingMode`, decides per-artifact reuse against a fingerprinted baseline (`ExportBaselineStore` + `ExportFingerprintBuilder`), applies CRS reprojection if `CoordinateExportMode.ConvertToTargetCrs` is selected, and writes via `GpkgWriter` (default) or `ShapefileWriter`.
4. `ExportPackageService` optionally bundles the artifacts and `ChangeSummaryService` diffs the new export against the stored baseline. `ExportDiagnosticsWriter` emits a JSON report.

### Extractors (Revit-API side)

Under `src/RevitGeoExporter/Extractors/`:
- `UnitExtractor` — converts floors / rooms / family instances into IMDF `unit` polygons with categories resolved by `FloorCategoryResolver` / `RoomCategoryResolver` against `ZoneCatalog` + per-project overrides.
- `OpeningExtractor` — door / opening family instances mapped via `OpeningFamilyClassifier` and `AcceptedOpeningFamilyStore`.
- `DetailExtractor` — stair tread/riser/nosing line work, including escalator and elevator handling.
- `LevelBoundaryBuilder` — computes the `level` polygon per view.
- `StairVisibilityResolver` — decides what part of a stair is visible at a plan view's cut plane. **Note (from memory):** the shaft-opening clip only fires when shaft `Opening` elements cross the cut plane; otherwise it falls back to the full stair footprint.
- `SectionBoxClipping` — helpers around `View3D.GetSectionBox()` Z-ranges. `Temp3DViewScope` (under `Export/`) creates throwaway 3D views with section boxes around each plan view (defaults: 1.2 m above floor, 0 m below) when `Use3DSectionBoxExport` is on.

### Packaging modes (`PackagingMode`)

- `PerViewPerFeatureFiles` — one file per (view × feature type). Default.
- `PerViewGeoPackage` — one GeoPackage per view, multiple layers inside.
- `PerLevelGeoPackage` — merge views sharing a level into a single GeoPackage.
- `PerBuildingGeoPackage` — one GeoPackage containing all views.

Shapefile output (`ExportFormat.Shapefile`) always writes one file per layer regardless of mode (Shapefile has no multi-layer container).

### Incremental export

`IncrementalExportMode.ChangedViewsOnly` reuses any artifact whose fingerprint matches the stored baseline. The baseline is keyed by **project + profile name** (`projectKey__profileName`) and persisted under `%APPDATA%\RevitGeoExporter\export-baselines\`. The fingerprint covers feature types, packaging mode, coordinate settings, schema profile, link options, and all category overrides — changing any of these forces a full rewrite.

### Coordinate handling

- `CoordinateExportMode.SharedCoordinates` — projects through Revit's `ProjectLocation` via `SharedCoordinateProjector` and writes the geometry as-is.
- `CoordinateExportMode.ConvertToTargetCrs` — additionally reprojects from a resolved source EPSG to the target EPSG using ProjNet via `CoordinateSystemCatalog` / `CrsTransformer`. The Japanese Plane Rectangular zone catalog (`JapanPlaneRectangular`) is the default target (EPSG 6677).

### Stable IDs

Each exported element gets a deterministic GUID stored as a Revit shared parameter (`SharedParameterManager`) so re-runs of the export keep the same IDs. The "Resolve issues" flow can regenerate IDs for selected elements inside a Revit transaction (`EnsureSharedParameters` / `RegenerateElementId`).

### Persistence layout

All user state lives under `%APPDATA%\RevitGeoExporter\`:

- `settings.json` — global export dialog settings.
- `profiles.json` — saved export profiles (global + per-project entries, project-keyed by `DocumentProjectKeyBuilder`).
- Per-project files for floor / room / family / opening overrides + mapping rules — keyed by the same project key.
- `export-baselines/<baselineKey>/` — incremental-export baselines.

All JSON loaders go through `JsonFileLoadHelper.Load` which returns a `LoadResult<T>` with warnings rather than throwing — corrupted files surface as user-visible warnings and fall back to defaults.

### UI stack

Mid-migration from WinForms to WPF (see `docs/wpf-migration-plan.md`):
- WPF is the default for the export dialog (`ExportDialogWpf`) and preview window (`ExportPreviewWindow`); the preview window currently embeds the legacy WinForms `ExportPreviewForm` via `WindowsFormsHost`.
- Settings hub, validation forms, batch export, progress, and result dialogs are still WinForms.
- Both `<UseWPF>` and `<UseWindowsForms>` are enabled in the add-in csproj.

### Localization

`UiLanguage` enum (English / Japanese) flows through `LocalizedTextProvider.Get`, `UiLanguageText.Get/Format/Select`. Help content is embedded HTML under `src/RevitGeoExporter/Resources/Help/{en,ja}/`, loaded by `HelpContentProvider` (falls back to English when the Japanese variant is missing).

## Conventions worth knowing

- `Directory.Build.props` enables nullable, disables implicit usings, and pins `LangVersion=latest`. Use file-scoped namespaces and `internal sealed class` where possible — matches the existing style.
- `src/RevitGeoExporter/Compatibility/IsExternalInit.cs` is the net48 shim that enables `init` setters and records — do not delete.
- Persistent JSON formats use Newtonsoft.Json with `Formatting.Indented`. Stores expose `LoadWithDiagnostics(...)` that returns warnings; prefer that over `Load(...)` when surfacing UI messages.
- Don't put Revit API types in `RevitGeoExporter.Core` — that boundary is what makes tests possible without a Revit install.
