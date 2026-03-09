using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitGeoExporter.Export;

public sealed class ViewExportContext
{
    public ViewExportContext(
        ViewPlan view,
        Level level,
        IReadOnlyList<Floor> floors,
        IReadOnlyList<Stairs> stairs,
        IReadOnlyList<FamilyInstance> familyUnits,
        IReadOnlyList<FamilyInstance> openings,
        IReadOnlyList<CurveElement> detailCurves)
    {
        View = view ?? throw new ArgumentNullException(nameof(view));
        Level = level ?? throw new ArgumentNullException(nameof(level));
        Floors = floors ?? throw new ArgumentNullException(nameof(floors));
        Stairs = stairs ?? throw new ArgumentNullException(nameof(stairs));
        FamilyUnits = familyUnits ?? throw new ArgumentNullException(nameof(familyUnits));
        Openings = openings ?? throw new ArgumentNullException(nameof(openings));
        DetailCurves = detailCurves ?? throw new ArgumentNullException(nameof(detailCurves));
    }

    public ViewPlan View { get; }

    public Level Level { get; }

    public IReadOnlyList<Floor> Floors { get; }

    public IReadOnlyList<Stairs> Stairs { get; }

    public IReadOnlyList<FamilyInstance> FamilyUnits { get; }

    public IReadOnlyList<FamilyInstance> Openings { get; }

    public IReadOnlyList<CurveElement> DetailCurves { get; }
}
