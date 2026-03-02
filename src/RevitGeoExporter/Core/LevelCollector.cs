using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoExporter.Core;

public sealed class LevelCollector
{
    public IReadOnlyList<Level> GetAllLevels(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .ThenBy(level => level.Name, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<Level> GetLevelsFromFloorPlanSuffix(
        Document document,
        string floorPlanNameSuffix)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (string.IsNullOrWhiteSpace(floorPlanNameSuffix))
        {
            throw new ArgumentException("A floor plan suffix is required.", nameof(floorPlanNameSuffix));
        }

        IEnumerable<ViewPlan> matchingPlans = new FilteredElementCollector(document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(plan =>
                !plan.IsTemplate &&
                plan.ViewType == ViewType.FloorPlan &&
                plan.Name.EndsWith(floorPlanNameSuffix, StringComparison.OrdinalIgnoreCase));

        Dictionary<long, Level> levels = new();
        foreach (ViewPlan plan in matchingPlans)
        {
            Level? level = plan.GenLevel;
            if (level != null)
            {
                levels[level.Id.Value] = level;
            }
        }

        return levels.Values
            .OrderBy(level => level.Elevation)
            .ThenBy(level => level.Name, StringComparer.Ordinal)
            .ToList();
    }
}
