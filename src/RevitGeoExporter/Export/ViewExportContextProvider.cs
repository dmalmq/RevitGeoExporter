using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Extractors;

namespace RevitGeoExporter.Export;

internal readonly struct CropBoundsXY
{
    public CropBoundsXY(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
}

public sealed class ViewExportContextProvider
{
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
        LinkExportOptions? linkExportOptions = null)
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

            CropBoundsXY? cropBounds = TryGetViewCropBoundsXY(view);

            contexts.Add(
                new ViewExportContext(
                    view,
                    level,
                    CollectFloorsInView(view.Id, cropBounds),
                    CollectHostOpeningsInView(view.Id, cropBounds),
                    CollectRoomsInView(view.Id, cropBounds),
                    CollectStairsInView(view.Id, cropBounds),
                    CollectFamilyUnitsInView(view.Id, zoneCatalog, familyCategoryOverrides, cropBounds),
                    CollectOpeningInstancesInView(view.Id, acceptedOpeningFamilies, cropBounds),
                    CollectUnsupportedOpeningInstancesInView(view.Id, acceptedOpeningFamilies, cropBounds),
                    CollectDetailCurvesInView(view.Id, cropBounds),
                    CollectLinkedSourcesInView(
                        view.Id,
                        zoneCatalog,
                        familyCategoryOverrides,
                        acceptedOpeningFamilies,
                        linkExportOptions,
                        loadedLinkInstances,
                        selectedLinkIds,
                        cropBounds)));
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

    private List<Floor> CollectFloorsInView(ElementId viewId, CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Cast<Floor>()
            .Where(element => IsElementInCropRegion(element, cropBounds))
            .ToList();
    }

    private List<Opening> CollectHostOpeningsInView(ElementId viewId, CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(Opening))
            .WhereElementIsNotElementType()
            .Cast<Opening>()
            .Where(opening => opening.Host is Floor || opening.Host == null)
            .Where(element => IsElementInCropRegion(element, cropBounds))
            .ToList();
    }

    private List<Room> CollectRoomsInView(ElementId viewId, CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(room => room.Area > 0d)
            .Where(element => IsElementInCropRegion(element, cropBounds))
            .ToList();
    }

    private List<Stairs> CollectStairsInView(ElementId viewId, CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(Stairs))
            .WhereElementIsNotElementType()
            .Cast<Stairs>()
            .Where(element => IsElementInCropRegion(element, cropBounds))
            .ToList();
    }

    private List<FamilyInstance> CollectFamilyUnitsInView(
        ElementId viewId,
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides,
        CropBoundsXY? cropBounds)
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
            .Where(element => IsElementInCropRegion(element, cropBounds))
            .ToList();
    }

    private List<FamilyInstance> CollectOpeningInstancesInView(
        ElementId viewId,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => OpeningFamilyClassifier.IsAcceptedOpening(instance, acceptedOpeningFamilies))
            .Where(element => IsElementInCropRegion(element, cropBounds))
            .ToList();
    }

    private List<FamilyInstance> CollectUnsupportedOpeningInstancesInView(
        ElementId viewId,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => IsUnsupportedOpening(instance, acceptedOpeningFamilies))
            .Where(element => IsElementInCropRegion(element, cropBounds))
            .ToList();
    }

    private List<CurveElement> CollectDetailCurvesInView(ElementId viewId, CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(CurveElement))
            .WhereElementIsNotElementType()
            .Cast<CurveElement>()
            .Where(element => IsElementInCropRegion(element, cropBounds))
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
        CropBoundsXY? cropBounds)
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
                    CollectFloorsInLinkView(viewId, linkInstance.Id, linkTransform, cropBounds),
                    CollectRoomsInLinkView(viewId, linkInstance.Id, linkTransform, cropBounds),
                    CollectStairsInLinkView(viewId, linkInstance.Id, linkTransform, cropBounds),
                    CollectFamilyUnitsInLinkView(viewId, linkInstance.Id, zoneCatalog, familyCategoryOverrides, linkTransform, cropBounds),
                    CollectOpeningInstancesInLinkView(viewId, linkInstance.Id, acceptedOpeningFamilies, linkTransform, cropBounds),
                    CollectUnsupportedOpeningInstancesInLinkView(viewId, linkInstance.Id, acceptedOpeningFamilies, linkTransform, cropBounds),
                    CollectDetailCurvesInLinkView(viewId, linkInstance.Id, linkTransform, cropBounds)));
        }

        return linkedSources;
    }

    private List<Floor> CollectFloorsInLinkView(
        ElementId viewId, ElementId linkInstanceId, Transform linkTransform, CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Cast<Floor>()
            .Where(element => IsLinkedElementInCropRegion(element, linkTransform, cropBounds))
            .ToList();
    }

    private List<Room> CollectRoomsInLinkView(
        ElementId viewId, ElementId linkInstanceId, Transform linkTransform, CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(room => room.Area > 0d)
            .Where(element => IsLinkedElementInCropRegion(element, linkTransform, cropBounds))
            .ToList();
    }

    private List<Stairs> CollectStairsInLinkView(
        ElementId viewId, ElementId linkInstanceId, Transform linkTransform, CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(Stairs))
            .WhereElementIsNotElementType()
            .Cast<Stairs>()
            .Where(element => IsLinkedElementInCropRegion(element, linkTransform, cropBounds))
            .ToList();
    }

    private List<FamilyInstance> CollectFamilyUnitsInLinkView(
        ElementId viewId,
        ElementId linkInstanceId,
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides,
        Transform linkTransform,
        CropBoundsXY? cropBounds)
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
            .Where(element => IsLinkedElementInCropRegion(element, linkTransform, cropBounds))
            .ToList();
    }

    private List<FamilyInstance> CollectOpeningInstancesInLinkView(
        ElementId viewId,
        ElementId linkInstanceId,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        Transform linkTransform,
        CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => OpeningFamilyClassifier.IsAcceptedOpening(instance, acceptedOpeningFamilies))
            .Where(element => IsLinkedElementInCropRegion(element, linkTransform, cropBounds))
            .ToList();
    }

    private List<FamilyInstance> CollectUnsupportedOpeningInstancesInLinkView(
        ElementId viewId,
        ElementId linkInstanceId,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        Transform linkTransform,
        CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => IsUnsupportedOpening(instance, acceptedOpeningFamilies))
            .Where(element => IsLinkedElementInCropRegion(element, linkTransform, cropBounds))
            .ToList();
    }

    private List<CurveElement> CollectDetailCurvesInLinkView(
        ElementId viewId, ElementId linkInstanceId, Transform linkTransform, CropBoundsXY? cropBounds)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(CurveElement))
            .WhereElementIsNotElementType()
            .Cast<CurveElement>()
            .Where(element => IsLinkedElementInCropRegion(element, linkTransform, cropBounds))
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

    private static CropBoundsXY? TryGetViewCropBoundsXY(ViewPlan view)
    {
        if (view == null || !view.CropBoxActive)
        {
            return null;
        }

        BoundingBoxXYZ cropBox = view.CropBox;
        Transform transform = cropBox.Transform;

        XYZ min = cropBox.Min;
        XYZ max = cropBox.Max;

        XYZ[] corners = new[]
        {
            transform.OfPoint(min),
            transform.OfPoint(new XYZ(max.X, min.Y, min.Z)),
            transform.OfPoint(new XYZ(max.X, max.Y, min.Z)),
            transform.OfPoint(new XYZ(min.X, max.Y, min.Z)),
            transform.OfPoint(new XYZ(min.X, min.Y, max.Z)),
            transform.OfPoint(new XYZ(max.X, min.Y, max.Z)),
            transform.OfPoint(max),
            transform.OfPoint(new XYZ(min.X, max.Y, max.Z)),
        };

        double minX = corners.Min(c => c.X);
        double minY = corners.Min(c => c.Y);
        double maxX = corners.Max(c => c.X);
        double maxY = corners.Max(c => c.Y);

        return new CropBoundsXY(minX, minY, maxX, maxY);
    }

    private static bool IsElementInCropRegion(Element element, CropBoundsXY? cropBounds)
    {
        if (cropBounds == null)
        {
            return true;
        }

        BoundingBoxXYZ? box = GetElementModelBounds(element);
        if (box == null)
        {
            return true;
        }

        return BoundingBoxesOverlapXY(
            box.Min.X, box.Min.Y, box.Max.X, box.Max.Y,
            cropBounds.Value.MinX, cropBounds.Value.MinY,
            cropBounds.Value.MaxX, cropBounds.Value.MaxY);
    }

    private static bool IsLinkedElementInCropRegion(
        Element element, Transform linkTransform, CropBoundsXY? cropBounds)
    {
        if (cropBounds == null)
        {
            return true;
        }

        BoundingBoxXYZ? localBox = GetElementModelBounds(element);
        if (localBox == null)
        {
            return true;
        }

        XYZ[] corners = new[]
        {
            linkTransform.OfPoint(localBox.Min),
            linkTransform.OfPoint(new XYZ(localBox.Max.X, localBox.Min.Y, localBox.Min.Z)),
            linkTransform.OfPoint(new XYZ(localBox.Max.X, localBox.Max.Y, localBox.Min.Z)),
            linkTransform.OfPoint(new XYZ(localBox.Min.X, localBox.Max.Y, localBox.Min.Z)),
            linkTransform.OfPoint(new XYZ(localBox.Min.X, localBox.Min.Y, localBox.Max.Z)),
            linkTransform.OfPoint(new XYZ(localBox.Max.X, localBox.Min.Y, localBox.Max.Z)),
            linkTransform.OfPoint(localBox.Max),
            linkTransform.OfPoint(new XYZ(localBox.Min.X, localBox.Max.Y, localBox.Max.Z)),
        };

        double hostMinX = corners.Min(c => c.X);
        double hostMinY = corners.Min(c => c.Y);
        double hostMaxX = corners.Max(c => c.X);
        double hostMaxY = corners.Max(c => c.Y);

        return BoundingBoxesOverlapXY(
            hostMinX, hostMinY, hostMaxX, hostMaxY,
            cropBounds.Value.MinX, cropBounds.Value.MinY,
            cropBounds.Value.MaxX, cropBounds.Value.MaxY);
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

    private static bool BoundingBoxesOverlapXY(
        double minX1, double minY1, double maxX1, double maxY1,
        double minX2, double minY2, double maxX2, double maxY2)
    {
        return minX1 <= maxX2 &&
               maxX1 >= minX2 &&
               minY1 <= maxY2 &&
               maxY1 >= minY2;
    }
}
