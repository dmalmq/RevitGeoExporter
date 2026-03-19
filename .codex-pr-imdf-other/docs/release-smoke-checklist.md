# Release Smoke Checklist

## Revit Startup
- Install the latest EXE build on a clean or upgraded machine.
- Launch Revit 2024 and confirm the `GeoExporter` ribbon tab appears.
- Open a representative project with floor plans, stairs, escalators, elevators, and openings.

## Settings And Dialogs
- Open `Settings` and verify English and Japanese labels render correctly.
- Save settings, reopen the dialog, and confirm they persist.
- Corrupt the local settings file manually and confirm the app shows a warning and falls back safely.

## Preview
- Open `Export GeoPackage` and launch `Preview...`.
- Switch between multiple selected views and confirm previews load.
- Toggle `Units`, `Openings`, `Stairs`, `Escalators`, and `Elevators`.
- Stage floor-category assignments, confirm recoloring happens immediately, then close without saving and verify the changes are discarded.
- Stage floor-category assignments again, click `Save Assignments`, reopen preview, and verify the saved overrides reload.

## Export
- Export `unit`, `opening`, `detail`, and `level` for representative views.
- Confirm the progress window updates without UI glitches or re-entrancy behavior.
- Confirm exported GeoPackages open in QGIS.
- Verify elevator footprints and elevator-adjacent openings are present.
- Verify stair detail lines use the schematic fixed-spacing representation.

## Installer And Upgrade
- Install over an older version and confirm the add-in still launches.
- Confirm the version shown in dialogs matches the installed build.
- Uninstall and verify add-in files are removed.
