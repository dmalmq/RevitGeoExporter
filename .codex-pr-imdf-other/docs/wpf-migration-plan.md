# WinForms to WPF Migration Plan (RevitGeoExporter)

This document outlines a low-risk migration path from the current WinForms UI to a modern WPF UI for the Revit add-in.

## Goals

- Modernize the add-in UI (layout, typography, spacing, theming, scalability).
- Preserve existing export logic and validation behavior.
- Avoid a "big bang" rewrite that risks breaking production workflows.
- Keep compatibility with Revit 2024 and `.NET Framework 4.8`.

## Current State (Repository)

- The add-in project already has both UI stacks enabled:
  - `<UseWindowsForms>true</UseWindowsForms>`
  - `<UseWPF>true</UseWPF>`
- Most user-facing dialogs are WinForms (`ExportDialog`, `ExportPreviewForm`, `SettingsHubForm`, validation/progress/result dialogs).
- UI/business logic are partially coupled in forms; extraction to services/view-models will reduce migration risk.

## Recommended Strategy

Use a **strangler pattern**:

1. Keep WinForms dialogs running while building new WPF windows in parallel.
2. Migrate one high-impact screen at a time.
3. Reuse existing core services (`ExportPreviewService`, validation/export pipelines) unchanged.
4. Feature-flag or switch command handlers to new windows only when each is stable.

This gives immediate visual improvements without blocking releases.

## UI Stack Recommendation

### Base framework
- WPF (.NET Framework 4.8) with MVVM.

### Styling library options
- **Option A: MahApps.Metro**
  - Easiest path to modern window chrome and controls.
  - Good default for enterprise desktop tools.
- **Option B: MaterialDesignInXamlToolkit**
  - More opinionated Material look.
  - Better if you want stronger visual identity.

For this add-in, **MahApps.Metro is the safer default** (lighter visual jump, fewer disruptive UX changes).

## Migration Phases

## Phase 0 - Foundation (1-2 days)

- Add a `UI.Wpf` folder (or new `RevitGeoExporter.UI.Wpf` project if you prefer stronger isolation).
- Establish shared design tokens:
  - Colors
  - Font sizes
  - Spacing scale
  - Corner radius
- Add a base theme dictionary (`Theme.xaml`) and common control styles.
- Add dialog hosting conventions for Revit:
  - Center on Revit window
  - Correct owner handle assignment
  - Top-most behavior only where required

**Deliverable:** a small WPF sample window launched from a debug-only command.

## Phase 1 - Migrate low-risk dialogs first (2-4 days)

Migrate simple dialogs that are mostly labels/buttons/inputs:

1. `ExportProgressForm`
2. `ExportResultForm`
3. `ExportValidationForm`

Why first:
- Limited interaction complexity.
- Quickly validates WPF hosting inside Revit.
- Delivers visible polish early.

**Deliverable:** command flow uses WPF versions for these dialogs while core export logic stays unchanged.

## Phase 2 - Migrate Settings hub (3-5 days)

Migrate `SettingsHubForm` and related settings dialogs to WPF with MVVM:

- Build `SettingsHubViewModel`.
- Move validation/state from control event handlers into VM/commands.
- Keep `SettingsBundle`, `ExportProfileStore`, and persistence logic unchanged.

**Deliverable:** full settings/profile management on WPF.

## Phase 3 - Migrate Export dialog (4-7 days)

Migrate `ExportDialog` next (primary daily workflow window):

- Rebuild view selection, feature toggles, EPSG/profile sections.
- Keep dialog result contract (`ExportDialogResult`) stable so downstream code does not change.
- Ensure keyboard navigation and default button behavior match existing UX.

**Deliverable:** export setup flow fully WPF with parity behavior.


## Phase 4 - Migrate Preview window (6-10 days)

`ExportPreviewForm` is the most complex screen.

Two implementation options:

- **Option 1 (lower risk):** host existing drawing logic in `WindowsFormsHost` temporarily.
  - Fastest path to ship modern surrounding UI while preserving rendering behavior.
- **Option 2 (cleaner long term):** reimplement canvas rendering in native WPF (`DrawingVisual` / `Canvas`) with retained-mode rendering.

Recommended approach:
- Start with Option 1 for immediate migration.
- Plan Option 2 as a follow-up performance/UX task.

**Deliverable:** preview workflow in WPF shell with tabs/panels matching modern style.

### Current progress

- Added an experimental WPF preview host window (`ExportPreviewWindow`) that embeds the existing WinForms preview form via `WindowsFormsHost`.
- Added command-level routing so WPF preview host is now the default path.
- Existing WinForms `ExportPreviewForm` remains available only via `REVIT_GEOEXPORTER_FORCE_LEGACY_WINFORMS_UI=1` while we work toward full native WPF preview parity.


## Phase 5 - Cleanup and WinForms retirement (2-3 days)

- Remove obsolete WinForms dialogs once WPF equivalents are production-stable.
- Keep only any WinForms controls intentionally hosted for compatibility.
- Update docs/screenshots/help pages.

**Deliverable:** WPF-first UI architecture with minimized WinForms surface.

### Current progress

- Switched command flow to WPF-first defaults for export and preview UI.
- Consolidated migration toggles to a single emergency fallback flag: `REVIT_GEOEXPORTER_FORCE_LEGACY_WINFORMS_UI=1`.
- Kept legacy WinForms dialogs in source temporarily for rollback safety while converging to native WPF parity.


## Architecture & Coding Guidelines

- Use MVVM for all new WPF screens.
- Keep model/export logic in existing core/services; avoid UI logic in code-behind.
- Introduce small adapter interfaces where forms previously referenced controls directly.
- Prefer async command patterns for long-running operations (export/progress).
- Keep localized strings in current resource system (`LocalizedTextProvider` + resx).

## Risk Register and Mitigations

1. **Revit window ownership/focus issues**
   - Mitigation: centralize WPF dialog show/owner helper using Revit main window handle.

2. **Preview rendering regressions**
   - Mitigation: ship WPF shell + hosted existing canvas first, then replace rendering incrementally.

3. **Behavior drift from WinForms flow**
   - Mitigation: define parity checklist per dialog (default values, shortcuts, validations, button outcomes).

4. **Timeline creep from full redesign**
   - Mitigation: separate visual refresh from feature redesign; maintain parity scope per phase.

## Acceptance Criteria (per migrated dialog)

- Functional parity with existing WinForms behavior.
- Localized strings still resolve correctly in both English/Japanese.
- Keyboard/tab navigation works.
- DPI scaling at 100% / 150% remains usable.
- No blocking regressions in export output.

## Suggested Execution Order (Practical)

1. Foundation + theme
2. Progress / result / validation dialogs
3. Settings hub
4. Export dialog
5. Preview shell (hosted old canvas)
6. Native WPF preview canvas (optional hardening phase)
7. WinForms cleanup

## Definition of Done (Program-level)

- All primary workflows (settings, preview, export) run through WPF windows.
- WinForms usage is either removed or intentionally isolated behind compatibility wrappers.
- Updated documentation and screenshots reflect the new interface.
- Team can add future UI features using MVVM and shared styles without touching WinForms.
