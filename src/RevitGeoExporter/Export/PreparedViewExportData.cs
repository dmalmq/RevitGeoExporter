using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Export;

public sealed class PreparedViewExportData
{
    public PreparedViewExportData(
        ViewPlan view,
        Level level,
        string levelId,
        ExportLayer? unitLayer,
        ExportLayer? detailLayer,
        ExportLayer? openingLayer,
        ExportLayer? levelLayer,
        IReadOnlyList<string> warnings)
    {
        View = view ?? throw new ArgumentNullException(nameof(view));
        Level = level ?? throw new ArgumentNullException(nameof(level));
        LevelId = levelId ?? throw new ArgumentNullException(nameof(levelId));
        UnitLayer = unitLayer;
        DetailLayer = detailLayer;
        OpeningLayer = openingLayer;
        LevelLayer = levelLayer;
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
    }

    public ViewPlan View { get; }

    public Level Level { get; }

    public string LevelId { get; }

    public ExportLayer? UnitLayer { get; }

    public ExportLayer? DetailLayer { get; }

    public ExportLayer? OpeningLayer { get; }

    public ExportLayer? LevelLayer { get; }

    public IReadOnlyList<string> Warnings { get; }
}
