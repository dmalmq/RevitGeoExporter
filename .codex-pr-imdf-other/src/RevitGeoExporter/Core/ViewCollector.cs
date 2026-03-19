using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoExporter.Core;

public sealed class ViewCollector
{
    public IReadOnlyList<ViewPlan> GetExportablePlanViews(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(IsExportablePlanView)
            .Where(view => view.GenLevel != null)
            .OrderBy(view => view.GenLevel!.Elevation)
            .ThenBy(view => view.GenLevel!.Name, StringComparer.Ordinal)
            .ThenBy(view => view.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsExportablePlanView(ViewPlan view)
    {
        if (view.IsTemplate)
        {
            return false;
        }

        return view.ViewType == ViewType.FloorPlan ||
               view.ViewType == ViewType.CeilingPlan;
    }
}
