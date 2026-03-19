using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Extractors;

namespace RevitGeoExporter.Export;

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

            contexts.Add(
                new ViewExportContext(
                    view,
                    level,
                    CollectFloorsInView(view.Id),
                    CollectRoomsInView(view.Id),
                    CollectStairsInView(view.Id),
                    CollectFamilyUnitsInView(view.Id, zoneCatalog, familyCategoryOverrides),
                    CollectOpeningInstancesInView(view.Id, acceptedOpeningFamilies),
                    CollectUnsupportedOpeningInstancesInView(view.Id, acceptedOpeningFamilies),
                    CollectDetailCurvesInView(view.Id),
                    CollectLinkedSourcesInView(view.Id, zoneCatalog, familyCategoryOverrides, acceptedOpeningFamilies, linkExportOptions)));
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

    private List<Floor> CollectFloorsInView(ElementId viewId)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Cast<Floor>()
            .ToList();
    }

    private List<Room> CollectRoomsInView(ElementId viewId)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(room => room.Area > 0d)
            .ToList();
    }

    private List<Stairs> CollectStairsInView(ElementId viewId)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(Stairs))
            .WhereElementIsNotElementType()
            .Cast<Stairs>()
            .ToList();
    }

    private List<FamilyInstance> CollectFamilyUnitsInView(
        ElementId viewId,
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides)
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
            .ToList();
    }

    private List<FamilyInstance> CollectOpeningInstancesInView(
        ElementId viewId,
        IReadOnlyList<string>? acceptedOpeningFamilies)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => OpeningFamilyClassifier.IsAcceptedOpening(instance, acceptedOpeningFamilies))
            .ToList();
    }

    private List<FamilyInstance> CollectUnsupportedOpeningInstancesInView(
        ElementId viewId,
        IReadOnlyList<string>? acceptedOpeningFamilies)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => IsUnsupportedOpening(instance, acceptedOpeningFamilies))
            .ToList();
    }

    private List<CurveElement> CollectDetailCurvesInView(ElementId viewId)
    {
        return new FilteredElementCollector(_document, viewId)
            .OfClass(typeof(CurveElement))
            .WhereElementIsNotElementType()
            .Cast<CurveElement>()
            .ToList();
    }

    private List<LinkedViewSourceContext> CollectLinkedSourcesInView(
        ElementId viewId,
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides,
        IReadOnlyList<string>? acceptedOpeningFamilies,
        LinkExportOptions? linkExportOptions)
    {
        if (linkExportOptions == null || !linkExportOptions.IncludeLinkedModels)
        {
            return new List<LinkedViewSourceContext>();
        }

        HashSet<long> selectedLinkIds = new(
            linkExportOptions.SelectedLinkInstanceIds ?? new List<long>());
        if (selectedLinkIds.Count == 0)
        {
            return new List<LinkedViewSourceContext>();
        }

        List<LinkedViewSourceContext> linkedSources = new();
        foreach (RevitLinkInstance linkInstance in GetLoadedLinkInstances())
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

            linkedSources.Add(
                new LinkedViewSourceContext(
                    linkInstance,
                    linkedDocument,
                    linkInstance.GetTotalTransform(),
                    DocumentProjectKeyBuilder.Create(linkedDocument),
                    DocumentProjectKeyBuilder.CreateDisplayName(linkedDocument),
                    CollectFloorsInLinkView(viewId, linkInstance.Id),
                    CollectRoomsInLinkView(viewId, linkInstance.Id),
                    CollectStairsInLinkView(viewId, linkInstance.Id),
                    CollectFamilyUnitsInLinkView(viewId, linkInstance.Id, zoneCatalog, familyCategoryOverrides),
                    CollectOpeningInstancesInLinkView(viewId, linkInstance.Id, acceptedOpeningFamilies),
                    CollectUnsupportedOpeningInstancesInLinkView(viewId, linkInstance.Id, acceptedOpeningFamilies),
                    CollectDetailCurvesInLinkView(viewId, linkInstance.Id)));
        }

        return linkedSources;
    }

    private List<Floor> CollectFloorsInLinkView(ElementId viewId, ElementId linkInstanceId)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Cast<Floor>()
            .ToList();
    }

    private List<Room> CollectRoomsInLinkView(ElementId viewId, ElementId linkInstanceId)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(room => room.Area > 0d)
            .ToList();
    }

    private List<Stairs> CollectStairsInLinkView(ElementId viewId, ElementId linkInstanceId)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(Stairs))
            .WhereElementIsNotElementType()
            .Cast<Stairs>()
            .ToList();
    }

    private List<FamilyInstance> CollectFamilyUnitsInLinkView(
        ElementId viewId,
        ElementId linkInstanceId,
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides)
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
            .ToList();
    }

    private List<FamilyInstance> CollectOpeningInstancesInLinkView(
        ElementId viewId,
        ElementId linkInstanceId,
        IReadOnlyList<string>? acceptedOpeningFamilies)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => OpeningFamilyClassifier.IsAcceptedOpening(instance, acceptedOpeningFamilies))
            .ToList();
    }

    private List<FamilyInstance> CollectUnsupportedOpeningInstancesInLinkView(
        ElementId viewId,
        ElementId linkInstanceId,
        IReadOnlyList<string>? acceptedOpeningFamilies)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => IsUnsupportedOpening(instance, acceptedOpeningFamilies))
            .ToList();
    }

    private List<CurveElement> CollectDetailCurvesInLinkView(ElementId viewId, ElementId linkInstanceId)
    {
        return new FilteredElementCollector(_document, viewId, linkInstanceId)
            .OfClass(typeof(CurveElement))
            .WhereElementIsNotElementType()
            .Cast<CurveElement>()
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
}
