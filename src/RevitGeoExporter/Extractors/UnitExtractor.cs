using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Extractors;

public sealed class UnitExtractor
{
    private readonly Document _document;
    private readonly Transform _internalToSharedTransform;
    private readonly CrsTransformer _transformer;
    private readonly ZoneCatalog _zoneCatalog;
    private readonly SharedParameterManager _parameterManager;
    private readonly string _source;

    public UnitExtractor(
        Document document,
        ZoneCatalog zoneCatalog,
        SharedParameterManager parameterManager,
        string source)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _internalToSharedTransform =
            _document.ActiveProjectLocation?.GetTotalTransform() ?? Transform.Identity;
        _transformer = new CrsTransformer();
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _parameterManager = parameterManager ?? throw new ArgumentNullException(nameof(parameterManager));
        _source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
    }

    public bool TryCreateFloorUnit(
        Floor floor,
        string levelId,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (floor is null)
        {
            return false;
        }

        long elementId = floor.Id.Value;
        if (!TryExtractElementPolygon(floor, out Polygon2D polygon))
        {
            warnings.Add($"Floor {elementId} geometry could not be extracted.");
            return false;
        }

        string typeName = GetElementTypeName(floor);
        ZoneNameParseResult parsed = ZoneNameParser.Parse(typeName);
        string zoneName = parsed.PatternMatched ? parsed.ZoneName : typeName;
        if (!parsed.PatternMatched)
        {
            warnings.Add($"Floor {elementId} type '{typeName}' did not match expected pattern.");
        }

        bool foundZone = _zoneCatalog.TryGetZoneInfo(zoneName, out ZoneInfo zoneInfo);
        if (!foundZone)
        {
            warnings.Add(
                $"Floor {elementId} zone '{zoneName}' was not found in catalog. Default category/restriction applied.");
        }

        feature = CreateFeature(
            sourceElement: floor,
            polygon: polygon,
            levelId: levelId,
            zoneInfo: zoneInfo,
            warnings: warnings);
        return true;
    }

    public bool TryCreateStairsUnit(
        Stairs stairs,
        string levelId,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (stairs is null)
        {
            return false;
        }

        long elementId = stairs.Id.Value;
        if (!TryExtractElementPolygon(stairs, out Polygon2D polygon))
        {
            warnings.Add($"Stairs {elementId} geometry could not be extracted.");
            return false;
        }

        feature = CreateFeature(
            sourceElement: stairs,
            polygon: polygon,
            levelId: levelId,
            zoneInfo: _zoneCatalog.StairsDefault,
            warnings: warnings);
        return true;
    }

    public bool TryCreateFamilyUnit(
        FamilyInstance familyInstance,
        string levelId,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (familyInstance is null)
        {
            return false;
        }

        string familyName = GetFamilyName(familyInstance);
        if (!_zoneCatalog.TryGetFamilyInfo(familyName, out ZoneInfo zoneInfo))
        {
            return false;
        }

        long elementId = familyInstance.Id.Value;
        if (!TryExtractElementPolygon(familyInstance, out Polygon2D polygon))
        {
            warnings.Add($"Family instance {elementId} ({familyName}) geometry could not be extracted.");
            return false;
        }

        feature = CreateFeature(
            sourceElement: familyInstance,
            polygon: polygon,
            levelId: levelId,
            zoneInfo: zoneInfo,
            warnings: warnings);
        return true;
    }

    private ExportPolygon CreateFeature(
        Element sourceElement,
        Polygon2D polygon,
        string levelId,
        ZoneInfo zoneInfo,
        ICollection<string> warnings)
    {
        string id = _parameterManager.GetOrCreateElementId(sourceElement, warnings);
        string? name = _parameterManager.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfNameParameterName);
        string? altName = _parameterManager.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfAltNameParameterName);
        Point2D centroid = DisplayPointCalculator.CalculateCentroid(polygon);
        string displayPoint = DisplayPointCalculator.ToWktPoint(centroid);

        return new ExportPolygon(
            polygon,
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["category"] = zoneInfo.Category,
                ["restrict"] = zoneInfo.Restriction,
                ["name"] = name,
                ["alt_name"] = altName,
                ["level_id"] = levelId,
                ["source"] = _source,
                ["display_point"] = displayPoint,
            });
    }

    private bool TryExtractElementPolygon(Element element, out Polygon2D polygon)
    {
        polygon = null!;
        List<List<XYZ>> loops = element is Floor floor
            ? ExtractLoopsFromFloorBottomFaces(floor)
            : new List<List<XYZ>>();
        if (loops.Count == 0)
        {
            loops = ExtractLoopsFromSolidGeometry(element);
        }

        if (loops.Count == 0)
        {
            return false;
        }

        List<List<Point2D>> projectedLoops = new();
        foreach (List<XYZ> loop in loops)
        {
            List<Point2D> ring = ProjectLoop(loop);
            if (ring.Count >= 4)
            {
                projectedLoops.Add(ring);
            }
        }

        if (projectedLoops.Count == 0)
        {
            return false;
        }

        int exteriorIndex = GetExteriorLoopIndex(projectedLoops);
        IReadOnlyList<Point2D> exterior = projectedLoops[exteriorIndex];
        List<IReadOnlyList<Point2D>> holes = new();
        for (int i = 0; i < projectedLoops.Count; i++)
        {
            if (i != exteriorIndex)
            {
                holes.Add(projectedLoops[i]);
            }
        }

        polygon = new Polygon2D(exterior, holes);
        return true;
    }

    private static List<List<XYZ>> ExtractLoopsFromFloorBottomFaces(Floor floor)
    {
        List<List<XYZ>> loops = new();
        IList<Reference>? references = HostObjectUtils.GetBottomFaces(floor);
        if (references == null)
        {
            return loops;
        }

        foreach (Reference reference in references)
        {
            if (floor.GetGeometryObjectFromReference(reference) is Face face)
            {
                loops.AddRange(ExtractLoopsFromFace(face));
            }
        }

        return loops;
    }

    private static List<List<XYZ>> ExtractLoopsFromSolidGeometry(Element element)
    {
        Options options = new()
        {
            DetailLevel = ViewDetailLevel.Fine,
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
        };

        GeometryElement? geometry = element.get_Geometry(options);
        if (geometry == null)
        {
            return new List<List<XYZ>>();
        }

        List<Solid> solids = CollectSolids(geometry);
        if (solids.Count == 0)
        {
            return new List<List<XYZ>>();
        }

        PlanarFace? lowestFace = null;
        double lowestZ = double.MaxValue;
        foreach (Solid solid in solids)
        {
            if (solid.Volume <= 0)
            {
                continue;
            }

            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace planarFace)
                {
                    continue;
                }

                XYZ normal = planarFace.FaceNormal;
                if (normal.Z >= -0.9d)
                {
                    continue;
                }

                if (planarFace.Origin.Z < lowestZ)
                {
                    lowestZ = planarFace.Origin.Z;
                    lowestFace = planarFace;
                }
            }
        }

        return lowestFace == null ? new List<List<XYZ>>() : ExtractLoopsFromFace(lowestFace);
    }

    private static List<Solid> CollectSolids(GeometryElement geometry)
    {
        List<Solid> solids = new();
        foreach (GeometryObject geometryObject in geometry)
        {
            switch (geometryObject)
            {
                case Solid solid when solid.Volume > 0:
                    solids.Add(solid);
                    break;
                case GeometryInstance instance:
                    solids.AddRange(CollectSolids(instance.GetInstanceGeometry()));
                    break;
            }
        }

        return solids;
    }

    private static List<List<XYZ>> ExtractLoopsFromFace(Face face)
    {
        List<List<XYZ>> loops = new();
        foreach (EdgeArray edgeArray in face.EdgeLoops)
        {
            List<XYZ> loop = new();
            foreach (Edge edge in edgeArray)
            {
                IList<XYZ> tessellated = edge.AsCurve().Tessellate();
                foreach (XYZ point in tessellated)
                {
                    if (loop.Count == 0 || !IsSamePoint(loop[loop.Count - 1], point))
                    {
                        loop.Add(point);
                    }
                }
            }

            if (loop.Count < 3)
            {
                continue;
            }

            if (!IsSamePoint(loop[0], loop[loop.Count - 1]))
            {
                loop.Add(loop[0]);
            }

            loops.Add(loop);
        }

        return loops;
    }

    private List<Point2D> ProjectLoop(IReadOnlyList<XYZ> loop)
    {
        List<Point2D> result = new(loop.Count);
        foreach (XYZ point in loop)
        {
            XYZ sharedPoint = _internalToSharedTransform.OfPoint(point);
            Point2D projected = _transformer.TransformFromRevitFeet(
                sharedPoint.X,
                sharedPoint.Y,
                offsetXMeters: 0d,
                offsetYMeters: 0d,
                rotationDegrees: 0d);

            if (result.Count == 0 || !IsSamePoint(result[result.Count - 1], projected))
            {
                result.Add(projected);
            }
        }

        if (result.Count >= 3 && !IsSamePoint(result[0], result[result.Count - 1]))
        {
            result.Add(result[0]);
        }

        return result;
    }

    private static int GetExteriorLoopIndex(IReadOnlyList<IReadOnlyList<Point2D>> loops)
    {
        int index = 0;
        double maxArea = double.MinValue;
        for (int i = 0; i < loops.Count; i++)
        {
            double area = Math.Abs(GetSignedArea(loops[i]));
            if (area > maxArea)
            {
                maxArea = area;
                index = i;
            }
        }

        return index;
    }

    private static bool IsSamePoint(XYZ left, XYZ right)
    {
        return left.DistanceTo(right) <= 1e-8d;
    }

    private static bool IsSamePoint(Point2D left, Point2D right)
    {
        return Math.Abs(left.X - right.X) <= 1e-8d &&
               Math.Abs(left.Y - right.Y) <= 1e-8d;
    }

    private static double GetSignedArea(IReadOnlyList<Point2D> ring)
    {
        double sum = 0d;
        for (int i = 0; i < ring.Count - 1; i++)
        {
            Point2D current = ring[i];
            Point2D next = ring[i + 1];
            sum += (current.X * next.Y) - (next.X * current.Y);
        }

        return sum * 0.5d;
    }

    private string GetElementTypeName(Element element)
    {
        Element? typeElement = _document.GetElement(element.GetTypeId());
        string? name = (typeElement as ElementType)?.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name!.Trim();
        }

        return element.Name;
    }

    public static string GetFamilyName(FamilyInstance familyInstance)
    {
        string? familyName = familyInstance.Symbol?.FamilyName;
        if (string.IsNullOrWhiteSpace(familyName))
        {
            familyName = familyInstance.Symbol?.Family?.Name;
        }

        return string.IsNullOrWhiteSpace(familyName) ? "<unknown-family>" : familyName!.Trim();
    }
}
