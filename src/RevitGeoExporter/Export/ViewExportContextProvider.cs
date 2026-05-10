using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Extractors;

namespace RevitGeoExporter.Export;

public sealed class ViewExportContextProvider
{
    private const double MinUsefulClipExtentFeet = 3.28d; // 1 m

    private readonly Document _document;

    public ViewExportContextProvider(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public IReadOnlyList<ViewExportContext> BuildContexts(
        IReadOnlyList<ViewPlan> selectedViews,
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides = null,
        IReadOnlyList<string>? acceptedOpeningFamilies = null,
        LinkExportOptions? linkExportOptions = null,
        Temp3DViewScope? threeDViewScope = null)
    {
        if (selectedViews is null)
        {
            throw new ArgumentNullException(nameof(selectedViews));
        }

        if (zoneCatalog is null)
        {
            throw new ArgumentNullException(nameof(zoneCatalog));
        }

        bool includeLinks = linkExportOptions?.IncludeLinkedModels == true &&
                            (linkExportOptions.SelectedLinkInstanceIds?.Count ?? 0) > 0;
        IReadOnlyList<RevitLinkInstance> loadedLinkInstances = includeLinks
            ? GetLoadedLinkInstances()
            : Array.Empty<RevitLinkInstance>();
        HashSet<long> selectedLinkIds = includeLinks
            ? new HashSet<long>(linkExportOptions!.SelectedLinkInstanceIds ?? new List<long>())
            : new HashSet<long>();

        List<ViewExportContext> contexts = new(selectedViews.Count);
        foreach (ViewPlan? candidate in selectedViews)
        {
            if (candidate == null)
            {
                continue;
            }

            ViewPlan view = candidate;
            Level? level = view.GenLevel;
            if (level == null)
            {
                continue;
            }

            ClipRegion2D? clipRegion = TryGetViewClipRegion(view);
            View3D? geometryView = threeDViewScope?.GetGeometryView(view);
            if (clipRegion == null && geometryView != null)
            {
                clipRegion = TryGetSectionBoxClipRegion(geometryView);
            }

            contexts.Add(
                new ViewExportContext(
                    view,
                    level,
                    CollectFloorsInView(view.Id, clipRegion),
                    CollectHostOpeningsInView(view.Id, clipRegion),
                    CollectRoomsInView(view.Id, clipRegion),
                    CollectStairsInView(view.Id, clipRegion),
                    CollectFamilyUnitsInView(view.Id, zoneCatalog, familyCategoryOverrides, clipRegion),
                    CollectOpeningInstancesInView(view.Id, acceptedOpeningFamilies, clipRegion),
                    CollectUnsupportedOpeningInstancesInView(view.Id, acceptedOpeningFamilies, clipRegion),
                    CollectDetailCurvesInView(view.Id, clipRegion),
                    CollectLinkedSourcesInView(
                        view.Id,
                        zoneCatalog,
                        familyCategoryOverrides,
                        acceptedOpeningFamilies,
                        linkExportOptions,
                        loadedLinkInstances,
                        selectedLinkIds,
                        clipRegion),
                    geometryView));
        }

        return contexts;
    }

    public IReadOnlyList<RevitLinkInstance> GetLoadedLinkInstances()
    {
        return new FilteredElementCollector(_document)
            .OfClass(typeof(RevitLinkInstance))
            .WhereElementIsNotElementType()
            .Cast<RevitLinkInstance>()
            .Where(instance => instance.GetLinkDocument() != null)
            .OrderBy(instance => GetLinkDisplayName(instance), StringComparer.OrdinalIgnoreCase)
            .ThenBy(instance => instance.Id.Value)
            .ToList();
    }

    private List<Floor> CollectFloorsInView(ElementId viewId, ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Cast<Floor>()
            .Where(element => IsElementInClipRegion(element, clipRegion))
            .ToList();
    }

    private List<Opening> CollectHostOpeningsInView(ElementId viewId, ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(Opening))
            .WhereElementIsNotElementType()
            .Cast<Opening>()
            .Where(opening => opening.Host is Floor || opening.Host == null)
            .Where(element => IsElementInClipRegion(element, clipRegion))
            .ToList();
    }

    private List<Room> CollectRoomsInView(ElementId viewId, ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(room => room.Area > 0d)
            .Where(element => IsElementInClipRegion(element, clipRegion))
            .ToList();
    }

    private List<Stairs> CollectStairsInView(ElementId viewId, ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(Stairs))
            .WhereElementIsNotElementType()
            .Cast<Stairs>()
            .Where(element => IsElementInClipRegion(element, clipRegion))
            .ToList();
    }

    private List<FamilyInstance> CollectFamilyUnitsInView(
        ElementId viewId,
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides,
        ClipRegion2D? clipRegion)
    {
        IReadOnlyDictionary<string, string> overrides = familyCategoryOverrides ??
            new Dictionary<string, string>(StringComparer.Ordinal);
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance =>
            {
                string familyName = UnitExtractor.GetFamilyName(instance);
                return zoneCatalog.TryGetFamilyInfo(familyName, out _) ||
                       overrides.ContainsKey(familyName);
            })
            .Where(element => IsElementInClipRegion(element, clipRegion))
            .ToList();
    }

    private List<FamilyInstance> CollectOpeningInstancesInView(
        ElementId viewId,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => OpeningFamilyClassifier.IsAcceptedOpening(instance, acceptedOpeningFamilies))
            .Where(element => IsElementInClipRegion(element, clipRegion))
            .ToList();
    }

    private List<FamilyInstance> CollectUnsupportedOpeningInstancesInView(
        ElementId viewId,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => IsUnsupportedOpening(instance, acceptedOpeningFamilies))
            .Where(element => IsElementInClipRegion(element, clipRegion))
            .ToList();
    }

    private List<CurveElement> CollectDetailCurvesInView(ElementId viewId, ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(CurveElement))
            .WhereElementIsNotElementType()
            .Cast<CurveElement>()
            .Where(element => IsElementInClipRegion(element, clipRegion))
            .ToList();
    }

    private List<LinkedViewSourceContext> CollectLinkedSourcesInView(
        ElementId viewId,
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        LinkExportOptions? linkExportOptions,
        IReadOnlyList<RevitLinkInstance> loadedLinkInstances,
        ISet<long> selectedLinkIds,
        ClipRegion2D? clipRegion)
    {
        if (linkExportOptions == null || !linkExportOptions.IncludeLinkedModels)
        {
            return new List<LinkedViewSourceContext>();
        }

        if (selectedLinkIds.Count == 0)
        {
            return new List<LinkedViewSourceContext>();
        }

        List<LinkedViewSourceContext> linkedSources = new();
        foreach (RevitLinkInstance linkInstance in loadedLinkInstances)
        {
            if (!selectedLinkIds.Contains(linkInstance.Id.Value))
            {
                continue;
            }

            Document? linkedDocument = linkInstance.GetLinkDocument();
            if (linkedDocument == null)
            {
                continue;
            }

            Transform linkTransform = linkInstance.GetTotalTransform();

            linkedSources.Add(
                new LinkedViewSourceContext(
                    linkInstance,
                    linkedDocument,
                    linkTransform,
                    DocumentProjectKeyBuilder.Create(linkedDocument),
                    DocumentProjectKeyBuilder.CreateDisplayName(linkedDocument),
                    CollectFloorsInLinkView(viewId, linkInstance.Id, linkTransform, clipRegion),
                    CollectRoomsInLinkView(viewId, linkInstance.Id, linkTransform, clipRegion),
                    CollectStairsInLinkView(viewId, linkInstance.Id, linkTransform, clipRegion),
                    CollectFamilyUnitsInLinkView(viewId, linkInstance.Id, zoneCatalog, familyCategoryOverrides, linkTransform, clipRegion),
                    CollectOpeningInstancesInLinkView(viewId, linkInstance.Id, acceptedOpeningFamilies, linkTransform, clipRegion),
                    CollectUnsupportedOpeningInstancesInLinkView(viewId, linkInstance.Id, acceptedOpeningFamilies, linkTransform, clipRegion),
                    CollectDetailCurvesInLinkView(viewId, linkInstance.Id, linkTransform, clipRegion)));
        }

        return linkedSources;
    }

    private List<Floor> CollectFloorsInLinkView(
        ElementId viewId, ElementId linkInstanceId, Transform linkTransform, ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Cast<Floor>()
            .Where(element => IsLinkedElementInClipRegion(element, linkTransform, clipRegion))
            .ToList();
    }

    private List<Room> CollectRoomsInLinkView(
        ElementId viewId, ElementId linkInstanceId, Transform linkTransform, ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(room => room.Area > 0d)
            .Where(element => IsLinkedElementInClipRegion(element, linkTransform, clipRegion))
            .ToList();
    }

    private List<Stairs> CollectStairsInLinkView(
        ElementId viewId, ElementId linkInstanceId, Transform linkTransform, ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(Stairs))
            .WhereElementIsNotElementType()
            .Cast<Stairs>()
            .Where(element => IsLinkedElementInClipRegion(element, linkTransform, clipRegion))
            .ToList();
    }

    private List<FamilyInstance> CollectFamilyUnitsInLinkView(
        ElementId viewId,
        ElementId linkInstanceId,
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides,
        Transform linkTransform,
        ClipRegion2D? clipRegion)
    {
        IReadOnlyDictionary<string, string> overrides = familyCategoryOverrides ??
            new Dictionary<string, string>(StringComparer.Ordinal);
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance =>
            {
                string familyName = UnitExtractor.GetFamilyName(instance);
                return zoneCatalog.TryGetFamilyInfo(familyName, out _) ||
                       overrides.ContainsKey(familyName);
            })
            .Where(element => IsLinkedElementInClipRegion(element, linkTransform, clipRegion))
            .ToList();
    }

    private List<FamilyInstance> CollectOpeningInstancesInLinkView(
        ElementId viewId,
        ElementId linkInstanceId,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        Transform linkTransform,
        ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => OpeningFamilyClassifier.IsAcceptedOpening(instance, acceptedOpeningFamilies))
            .Where(element => IsLinkedElementInClipRegion(element, linkTransform, clipRegion))
            .ToList();
    }

    private List<FamilyInstance> CollectUnsupportedOpeningInstancesInLinkView(
        ElementId viewId,
        ElementId linkInstanceId,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        Transform linkTransform,
        ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => IsUnsupportedOpening(instance, acceptedOpeningFamilies))
            .Where(element => IsLinkedElementInClipRegion(element, linkTransform, clipRegion))
            .ToList();
    }

    private List<CurveElement> CollectDetailCurvesInLinkView(
        ElementId viewId, ElementId linkInstanceId, Transform linkTransform, ClipRegion2D? clipRegion)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(CurveElement))
            .WhereElementIsNotElementType()
            .Cast<CurveElement>()
            .Where(element => IsLinkedElementInClipRegion(element, linkTransform, clipRegion))
            .ToList();
    }

    private static string GetLinkDisplayName(RevitLinkInstance linkInstance)
    {
        if (linkInstance == null)
        {
            return string.Empty;
        }

        string name = linkInstance.Name?.Trim() ?? string.Empty;
        if (name.Length > 0)
        {
            return name;
        }

        Document? linkedDocument = linkInstance.GetLinkDocument();
        return linkedDocument == null
            ? $"Link {linkInstance.Id.Value}"
            : DocumentProjectKeyBuilder.CreateDisplayName(linkedDocument);
    }

    private static bool IsUnsupportedOpening(FamilyInstance instance, IReadOnlyList<string>? acceptedOpeningFamilies)
    {
        if (instance == null)
        {
            return false;
        }

        Category? category = instance.Category;
        if (category == null)
        {
            return false;
        }

        BuiltInCategory categoryId = (BuiltInCategory)(int)category.Id.Value;
        bool isDoorOrWindow = categoryId == BuiltInCategory.OST_Doors || categoryId == BuiltInCategory.OST_Windows;
        return isDoorOrWindow && !OpeningFamilyClassifier.IsAcceptedOpening(instance, acceptedOpeningFamilies);
    }

    private static ClipRegion2D? TryGetSectionBoxClipRegion(View3D view3D)
    {
        if (view3D == null || !view3D.IsSectionBoxActive)
        {
            return null;
        }

        BoundingBoxXYZ? box;
        try
        {
            box = view3D.GetSectionBox();
        }
        catch (Exception)
        {
            return null;
        }

        if (box == null)
        {
            return null;
        }

        Transform t = box.Transform ?? Transform.Identity;
        XYZ[] corners =
        {
            t.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Max.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Max.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Max.Z)),
            t.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Max.Z)),
        };

        List<Point2D> xyPoints = corners
            .Select(c => new Point2D(c.X, c.Y))
            .Distinct()
            .ToList();

        if (xyPoints.Count < 3)
        {
            return null;
        }

        List<Point2D> hull = ComputeConvexHull(xyPoints);
        if (hull.Count < 3)
        {
            return null;
        }

        if (!ClipRegion2D.TryCreate(hull, out ClipRegion2D region))
        {
            return null;
        }

        return region.IsEmpty ? null : region;
    }

    private static List<Point2D> ComputeConvexHull(List<Point2D> points)
    {
        if (points.Count <= 3)
        {
            return points.ToList();
        }

        List<Point2D> sorted = points
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToList();

        List<Point2D> lower = new();
        foreach (Point2D p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(p);
        }

        List<Point2D> upper = new();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            Point2D p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross(Point2D o, Point2D a, Point2D b)
    {
        return ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
    }

    internal static ClipRegion2D? TryGetViewClipRegion(ViewPlan view)
    {
        if (view == null)
        {
            return null;
        }

        Element? scopeBox = Temp3DViewScope.TryGetScopeBoxElement(view);
        if (scopeBox != null &&
            ScopeBoxFootprintBuilder.TryBuild(
                Temp3DViewScope.TryGetScopeBoxGeometryEdges(scopeBox),
                MinUsefulClipExtentFeet,
                out ScopeBoxFootprint footprint))
        {
            ClipRegion2D scopeRegion = ClipRegion2D.FromFootprint(footprint);
            if (!scopeRegion.IsEmpty)
            {
                return scopeRegion;
            }
        }

        return TryGetViewCropBoxClipRegion(view);
    }

    private static ClipRegion2D? TryGetViewCropBoxClipRegion(ViewPlan view)
    {
        if (view == null || !view.CropBoxActive)
        {
            return null;
        }

        BoundingBoxXYZ cropBox = view.CropBox;
        Transform transform = cropBox.Transform ?? Transform.Identity;

        XYZ min = cropBox.Min;
        XYZ max = cropBox.Max;

        if (ClipRegion2D.TryCreate(
                new[]
                {
                    ToPoint2D(transform.OfPoint(new XYZ(min.X, min.Y, min.Z))),
                    ToPoint2D(transform.OfPoint(new XYZ(max.X, min.Y, min.Z))),
                    ToPoint2D(transform.OfPoint(new XYZ(max.X, max.Y, min.Z))),
                    ToPoint2D(transform.OfPoint(new XYZ(min.X, max.Y, min.Z))),
                },
                out ClipRegion2D region))
        {
            return region;
        }

        return null;
    }

    private static bool IsElementInClipRegion(Element element, ClipRegion2D? clipRegion)
    {
        if (clipRegion == null)
        {
            return true;
        }

        BoundingBoxXYZ? box = GetElementModelBounds(element);
        if (box == null)
        {
            return true;
        }

        if (!TryGetBoundingBoxXyBounds(
                box,
                finalTransform: null,
                out double minX,
                out double minY,
                out double maxX,
                out double maxY))
        {
            return true;
        }

        return clipRegion.Value.IntersectsBounds(minX, minY, maxX, maxY);
    }

    private static bool IsLinkedElementInClipRegion(
        Element element, Transform linkTransform, ClipRegion2D? clipRegion)
    {
        if (clipRegion == null)
        {
            return true;
        }

        BoundingBoxXYZ? localBox = GetElementModelBounds(element);
        if (localBox == null)
        {
            return true;
        }

        if (!TryGetBoundingBoxXyBounds(
                localBox,
                linkTransform,
                out double hostMinX,
                out double hostMinY,
                out double hostMaxX,
                out double hostMaxY))
        {
            return true;
        }

        return clipRegion.Value.IntersectsBounds(hostMinX, hostMinY, hostMaxX, hostMaxY);
    }

    private static BoundingBoxXYZ? GetElementModelBounds(Element element)
    {
        if (element == null)
        {
            return null;
        }

        try
        {
            return element.get_BoundingBox(null);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryGetBoundingBoxXyBounds(
        BoundingBoxXYZ box,
        Transform? finalTransform,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = double.MaxValue;
        minY = double.MaxValue;
        maxX = double.MinValue;
        maxY = double.MinValue;
        if (box == null)
        {
            return false;
        }

        Transform boxTransform = box.Transform ?? Transform.Identity;
        XYZ[] corners =
        {
            new(box.Min.X, box.Min.Y, box.Min.Z),
            new(box.Max.X, box.Min.Y, box.Min.Z),
            new(box.Max.X, box.Max.Y, box.Min.Z),
            new(box.Min.X, box.Max.Y, box.Min.Z),
            new(box.Min.X, box.Min.Y, box.Max.Z),
            new(box.Max.X, box.Min.Y, box.Max.Z),
            new(box.Max.X, box.Max.Y, box.Max.Z),
            new(box.Min.X, box.Max.Y, box.Max.Z),
        };

        foreach (XYZ localCorner in corners)
        {
            XYZ corner = boxTransform.OfPoint(localCorner);
            if (finalTransform != null)
            {
                corner = finalTransform.OfPoint(corner);
            }

            if (corner.X < minX) minX = corner.X;
            if (corner.Y < minY) minY = corner.Y;
            if (corner.X > maxX) maxX = corner.X;
            if (corner.Y > maxY) maxY = corner.Y;
        }

        return maxX >= minX && maxY >= minY;
    }

    private static Point2D ToPoint2D(XYZ point)
    {
        return new Point2D(point.X, point.Y);
    }
}
