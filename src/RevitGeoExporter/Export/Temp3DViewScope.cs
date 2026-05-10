using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;

namespace RevitGeoExporter.Export;

public sealed class Temp3DViewScope : IDisposable
{
    public const double DefaultAboveFloorMeters = 1.2;
    public const double DefaultBelowFloorMeters = 0.0;
    private const double LargeXyHalfExtentFeet = 32_808.0;
    private const double MinUsefulXyExtentFeet = 3.28; // 1 m
    private const double SectionBoxXyMarginFeet = 16.4; // 5 m

    public readonly struct SectionBoxDiagnostic
    {
        public SectionBoxDiagnostic(
            ElementId planViewId,
            string planViewName,
            string source,
            double minXFeet,
            double minYFeet,
            double maxXFeet,
            double maxYFeet,
            double zMinFeet,
            double zMaxFeet,
            double levelElevationFeet,
            double levelProjectElevationFeet,
            double? floorTopZFeet,
            double rotationDegrees,
            string zSource)
        {
            PlanViewId = planViewId;
            PlanViewName = planViewName;
            Source = source;
            MinXFeet = minXFeet;
            MinYFeet = minYFeet;
            MaxXFeet = maxXFeet;
            MaxYFeet = maxYFeet;
            ZMinFeet = zMinFeet;
            ZMaxFeet = zMaxFeet;
            LevelElevationFeet = levelElevationFeet;
            LevelProjectElevationFeet = levelProjectElevationFeet;
            FloorTopZFeet = floorTopZFeet;
            RotationDegrees = rotationDegrees;
            ZSource = zSource;
        }

        public ElementId PlanViewId { get; }
        public string PlanViewName { get; }
        public string Source { get; }
        public double MinXFeet { get; }
        public double MinYFeet { get; }
        public double MaxXFeet { get; }
        public double MaxYFeet { get; }
        public double ZMinFeet { get; }
        public double ZMaxFeet { get; }
        public double LevelElevationFeet { get; }
        public double LevelProjectElevationFeet { get; }
        public double? FloorTopZFeet { get; }
        public double RotationDegrees { get; }
        public string ZSource { get; }
    }

    private readonly Document _document;
    private readonly Dictionary<ElementId, View3D> _viewsByPlanId = new();
    private readonly List<SectionBoxDiagnostic> _diagnostics = new();
    private readonly bool _keepViewsForInspection;
    private bool _disposed;

    public Temp3DViewScope(
        Document document,
        IReadOnlyList<ViewPlan> planViews,
        double aboveFloorMeters = DefaultAboveFloorMeters,
        double belowFloorMeters = DefaultBelowFloorMeters,
        bool keepViewsForInspection = false)
    {
        _keepViewsForInspection = keepViewsForInspection;
        _document = document ?? throw new ArgumentNullException(nameof(document));
        if (planViews == null)
        {
            throw new ArgumentNullException(nameof(planViews));
        }

        if (aboveFloorMeters <= 0d || double.IsNaN(aboveFloorMeters) || double.IsInfinity(aboveFloorMeters))
        {
            aboveFloorMeters = DefaultAboveFloorMeters;
        }

        if (double.IsNaN(belowFloorMeters) || double.IsInfinity(belowFloorMeters))
        {
            belowFloorMeters = DefaultBelowFloorMeters;
        }

        ElementId? viewFamilyTypeId = TryFindThreeDimensionalViewFamilyType();
        if (viewFamilyTypeId == null)
        {
            return;
        }

        double aboveFloorFeet = UnitUtils.ConvertToInternalUnits(
            aboveFloorMeters, UnitTypeId.Meters);
        double belowFloorFeet = UnitUtils.ConvertToInternalUnits(
            belowFloorMeters, UnitTypeId.Meters);

        // Cache project-wide bounds candidates ONCE so all temp views share them.
        BoundingBoxXYZ? scopeBoxUnion = TryComputeScopeBoxUnion(_document);
        BoundingBoxXYZ? buildingBounds = TryComputeBuildingCategoryBounds(_document);
        BoundingBoxXYZ? modelBounds = TryComputeModelXyBounds(_document);

        using Transaction transaction = new(_document, "RevitGeoExporter — create temp 3D export views");
        transaction.Start();
        try
        {
            foreach (ViewPlan? planView in planViews)
            {
                if (planView == null)
                {
                    continue;
                }

                Level? level = planView.GenLevel;
                if (level == null)
                {
                    continue;
                }

                View3D? view3D = TryCreateBoxedView(planView, level, viewFamilyTypeId, aboveFloorFeet, belowFloorFeet, scopeBoxUnion, buildingBounds, modelBounds);
                if (view3D != null)
                {
                    _viewsByPlanId[planView.Id] = view3D;
                }
            }

            transaction.Commit();
        }
        catch
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
            {
                transaction.RollBack();
            }
            _viewsByPlanId.Clear();
            _diagnostics.Clear();
            throw;
        }
    }

    public int CreatedViewCount => _viewsByPlanId.Count;

    public IReadOnlyList<SectionBoxDiagnostic> Diagnostics => _diagnostics;

    public View3D? GetGeometryView(ViewPlan planView)
    {
        if (planView == null)
        {
            return null;
        }

        return _viewsByPlanId.TryGetValue(planView.Id, out View3D? view) ? view : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_viewsByPlanId.Count == 0)
        {
            return;
        }

        if (_keepViewsForInspection)
        {
            _viewsByPlanId.Clear();
            return;
        }

        try
        {
            using Transaction transaction = new(_document, "RevitGeoExporter — delete temp 3D export views");
            transaction.Start();
            foreach (View3D view in _viewsByPlanId.Values)
            {
                try
                {
                    if (view != null && view.IsValidObject)
                    {
                        _document.Delete(view.Id);
                    }
                }
                catch
                {
                }
            }
            transaction.Commit();
        }
        catch
        {
        }
        finally
        {
            _viewsByPlanId.Clear();
        }
    }

    private View3D? TryCreateBoxedView(
        ViewPlan planView,
        Level level,
        ElementId viewFamilyTypeId,
        double aboveFloorFeet,
        double belowFloorFeet,
        BoundingBoxXYZ? scopeBoxUnion,
        BoundingBoxXYZ? buildingBounds,
        BoundingBoxXYZ? modelBounds)
    {
        View3D view3D;
        try
        {
            view3D = View3D.CreateIsometric(_document, viewFamilyTypeId);
        }
        catch (Exception)
        {
            return null;
        }

        try
        {
            view3D.Name = $"__RGE_TEMP_3D_{planView.Id.Value}_{Guid.NewGuid():N}";
        }
        catch (Exception)
        {
        }

        try
        {
            if (view3D.ViewTemplateId != ElementId.InvalidElementId)
            {
                view3D.ViewTemplateId = ElementId.InvalidElementId;
            }
        }
        catch (Exception)
        {
        }

        double? floorTopZ = TryGetLevelFloorTopZ(_document, level);
        double zReference = floorTopZ ?? level.Elevation;
        string zSource = floorTopZ.HasValue ? "floor_top" : "level_elevation";

        BoundingBoxXYZ sectionBox = BuildSectionBox(planView, zReference, aboveFloorFeet, belowFloorFeet, scopeBoxUnion, buildingBounds, modelBounds, out string source);
        view3D.SetSectionBox(sectionBox);
        view3D.IsSectionBoxActive = true;

        _diagnostics.Add(new SectionBoxDiagnostic(
            planView.Id,
            planView.Name ?? string.Empty,
            source,
            sectionBox.Min.X,
            sectionBox.Min.Y,
            sectionBox.Max.X,
            sectionBox.Max.Y,
            sectionBox.Min.Z,
            sectionBox.Max.Z,
            level.Elevation,
            level.ProjectElevation,
            floorTopZ,
            GetSectionBoxRotationDegrees(sectionBox),
            zSource));

        try
        {
            view3D.DetailLevel = ViewDetailLevel.Fine;
        }
        catch (Exception)
        {
        }

        try
        {
            view3D.DisplayStyle = DisplayStyle.ShadingWithEdges;
        }
        catch (Exception)
        {
        }

        return view3D;
    }

    private static BoundingBoxXYZ BuildSectionBox(
        ViewPlan planView,
        double zReferenceFeet,
        double aboveFloorFeet,
        double belowFloorFeet,
        BoundingBoxXYZ? scopeBoxUnion,
        BoundingBoxXYZ? buildingBounds,
        BoundingBoxXYZ? modelBounds,
        out string source)
    {
        double zMin = zReferenceFeet + belowFloorFeet;
        double zMax = zReferenceFeet + aboveFloorFeet;

        Element? scopeBox = TryGetScopeBoxElement(planView);
        if (scopeBox != null)
        {
            BoundingBoxXYZ? explicitBox = TryBuildScopeBoxGeometrySectionBox(scopeBox, zMin, zMax);
            if (explicitBox != null)
            {
                source = "scope_box_geometry";
                return explicitBox;
            }

            BoundingBoxXYZ? scopeBoxBbox = TryGetElementBounds(scopeBox);
            if (scopeBoxBbox != null)
            {
                BoundingBoxXYZ? boundsBox = TryBuildExplicitScopeBoxSectionBox(scopeBoxBbox, zMin, zMax);
                if (boundsBox != null)
                {
                    source = "scope_box_bounds";
                    return boundsBox;
                }
            }
        }

        double minX, minY, maxX, maxY;

        if (HasUsefulXyExtent(scopeBoxUnion))
        {
            minX = scopeBoxUnion!.Min.X - SectionBoxXyMarginFeet;
            minY = scopeBoxUnion.Min.Y - SectionBoxXyMarginFeet;
            maxX = scopeBoxUnion.Max.X + SectionBoxXyMarginFeet;
            maxY = scopeBoxUnion.Max.Y + SectionBoxXyMarginFeet;
            source = "scope_box_union";
        }
        else if (HasUsefulXyExtent(buildingBounds))
        {
            minX = buildingBounds!.Min.X - SectionBoxXyMarginFeet;
            minY = buildingBounds.Min.Y - SectionBoxXyMarginFeet;
            maxX = buildingBounds.Max.X + SectionBoxXyMarginFeet;
            maxY = buildingBounds.Max.Y + SectionBoxXyMarginFeet;
            source = "building_categories";
        }
        else if (HasUsefulXyExtent(modelBounds))
        {
            minX = modelBounds!.Min.X - SectionBoxXyMarginFeet;
            minY = modelBounds.Min.Y - SectionBoxXyMarginFeet;
            maxX = modelBounds.Max.X + SectionBoxXyMarginFeet;
            maxY = modelBounds.Max.Y + SectionBoxXyMarginFeet;
            source = "model_bounds";
        }
        else
        {
            minX = -LargeXyHalfExtentFeet;
            minY = -LargeXyHalfExtentFeet;
            maxX = LargeXyHalfExtentFeet;
            maxY = LargeXyHalfExtentFeet;
            source = "fallback_huge";
        }

        BoundingBoxXYZ box = new()
        {
            Min = new XYZ(minX, minY, zMin),
            Max = new XYZ(maxX, maxY, zMax),
        };
        return box;
    }

    private static BoundingBoxXYZ? TryBuildScopeBoxGeometrySectionBox(
        Element scopeBox,
        double zMin,
        double zMax)
    {
        IReadOnlyList<ScopeBoxEdge3D> edges = TryGetScopeBoxGeometryEdges(scopeBox);
        if (!ScopeBoxFootprintBuilder.TryBuild(edges, MinUsefulXyExtentFeet, out ScopeBoxFootprint footprint))
        {
            return null;
        }

        Transform transform = Transform.Identity;
        transform.BasisX = new XYZ(footprint.XBasis.X, footprint.XBasis.Y, 0d);
        transform.BasisY = new XYZ(footprint.YBasis.X, footprint.YBasis.Y, 0d);
        transform.BasisZ = XYZ.BasisZ;
        transform.Origin = new XYZ(footprint.Origin.X, footprint.Origin.Y, 0d);

        return new BoundingBoxXYZ
        {
            Transform = transform,
            Min = new XYZ(footprint.MinX, footprint.MinY, zMin),
            Max = new XYZ(footprint.MaxX, footprint.MaxY, zMax),
        };
    }

    internal static IReadOnlyList<ScopeBoxEdge3D> TryGetScopeBoxGeometryEdges(Element scopeBox)
    {
        Options options = new()
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = true,
            DetailLevel = ViewDetailLevel.Fine,
        };

        GeometryElement? geometry;
        try
        {
            geometry = scopeBox.get_Geometry(options);
        }
        catch (Exception)
        {
            return Array.Empty<ScopeBoxEdge3D>();
        }

        if (geometry == null)
        {
            return Array.Empty<ScopeBoxEdge3D>();
        }

        List<ScopeBoxEdge3D> edges = new();
        CollectScopeBoxGeometryEdges(geometry, edges);
        return edges;
    }

    private static void CollectScopeBoxGeometryEdges(GeometryElement geometry, List<ScopeBoxEdge3D> edges)
    {
        foreach (GeometryObject geometryObject in geometry)
        {
            switch (geometryObject)
            {
                case Curve curve:
                    TryAddScopeBoxCurveEdge(curve, edges);
                    break;
                case GeometryInstance instance:
                    GeometryElement? instanceGeometry;
                    try
                    {
                        instanceGeometry = instance.GetInstanceGeometry();
                    }
                    catch (Exception)
                    {
                        instanceGeometry = null;
                    }

                    if (instanceGeometry != null)
                    {
                        CollectScopeBoxGeometryEdges(instanceGeometry, edges);
                    }
                    break;
            }
        }
    }

    private static void TryAddScopeBoxCurveEdge(Curve curve, List<ScopeBoxEdge3D> edges)
    {
        XYZ start;
        XYZ end;
        try
        {
            start = curve.GetEndPoint(0);
            end = curve.GetEndPoint(1);
        }
        catch (Exception)
        {
            return;
        }

        if (start.DistanceTo(end) < 1e-9d)
        {
            return;
        }

        edges.Add(new ScopeBoxEdge3D(
            start.X,
            start.Y,
            start.Z,
            end.X,
            end.Y,
            end.Z));
    }

    private static BoundingBoxXYZ? TryBuildExplicitScopeBoxSectionBox(
        BoundingBoxXYZ scopeBoxBbox,
        double zMin,
        double zMax)
    {
        Transform t = scopeBoxBbox.Transform ?? Transform.Identity;
        double minX = scopeBoxBbox.Min.X;
        double minY = scopeBoxBbox.Min.Y;
        double minZ = scopeBoxBbox.Min.Z;
        double maxX = scopeBoxBbox.Max.X;
        double maxY = scopeBoxBbox.Max.Y;
        double maxZ = scopeBoxBbox.Max.Z;

        // Get all 8 corners in world space.
        XYZ c000 = t.OfPoint(new XYZ(minX, minY, minZ));
        XYZ c100 = t.OfPoint(new XYZ(maxX, minY, minZ));
        XYZ c110 = t.OfPoint(new XYZ(maxX, maxY, minZ));
        XYZ c010 = t.OfPoint(new XYZ(minX, maxY, minZ));
        XYZ c001 = t.OfPoint(new XYZ(minX, minY, maxZ));
        XYZ c101 = t.OfPoint(new XYZ(maxX, minY, maxZ));
        XYZ c111 = t.OfPoint(new XYZ(maxX, maxY, maxZ));
        XYZ c011 = t.OfPoint(new XYZ(minX, maxY, maxZ));

        XYZ[] corners = { c000, c100, c110, c010, c001, c101, c111, c011 };

        // Compute centroid of all corners.
        double cx = 0d, cy = 0d, cz = 0d;
        foreach (XYZ corner in corners)
        {
            cx += corner.X;
            cy += corner.Y;
            cz += corner.Z;
        }

        XYZ origin = new(cx / 8.0, cy / 8.0, cz / 8.0);

        // Reconstruct orthonormal basis from bottom-face edges.
        XYZ edge0 = c100 - c000;
        XYZ edge1 = c010 - c000;

        double len0 = edge0.GetLength();
        double len1 = edge1.GetLength();
        if (len0 < 1e-6 || len1 < 1e-6)
        {
            return null;
        }

        XYZ basisX = edge0 / len0;
        XYZ basisZ = basisX.CrossProduct(edge1);
        double lenZ = basisZ.GetLength();
        if (lenZ < 1e-12)
        {
            return null;
        }

        basisZ /= lenZ;
        XYZ basisY = basisZ.CrossProduct(basisX);

        // Project all corners onto the new basis to find local extents.
        double localMinX = double.MaxValue;
        double localMinY = double.MaxValue;
        double localMinZ = double.MaxValue;
        double localMaxX = double.MinValue;
        double localMaxY = double.MinValue;
        double localMaxZ = double.MinValue;

        foreach (XYZ corner in corners)
        {
            XYZ diff = corner - origin;
            double lx = diff.DotProduct(basisX);
            double ly = diff.DotProduct(basisY);
            double lz = diff.DotProduct(basisZ);
            if (lx < localMinX) localMinX = lx;
            if (ly < localMinY) localMinY = ly;
            if (lz < localMinZ) localMinZ = lz;
            if (lx > localMaxX) localMaxX = lx;
            if (ly > localMaxY) localMaxY = ly;
            if (lz > localMaxZ) localMaxZ = lz;
        }

        double halfX = Math.Max(Math.Abs(localMinX), Math.Abs(localMaxX));
        double halfY = Math.Max(Math.Abs(localMinY), Math.Abs(localMaxY));
        double halfZ = Math.Max(Math.Abs(localMinZ), Math.Abs(localMaxZ));

        if (halfX < MinUsefulXyExtentFeet * 0.5 || halfY < MinUsefulXyExtentFeet * 0.5)
        {
            return null;
        }

        Transform explicitTransform = Transform.Identity;
        explicitTransform.BasisX = basisX;
        explicitTransform.BasisY = basisY;
        explicitTransform.BasisZ = basisZ;
        explicitTransform.Origin = origin;

        return new BoundingBoxXYZ
        {
            Transform = explicitTransform,
            Min = new XYZ(-halfX, -halfY, zMin - origin.Z),
            Max = new XYZ(halfX, halfY, zMax - origin.Z),
        };
    }

    internal static Element? TryGetScopeBoxElement(ViewPlan planView)
    {
        Parameter? scopeBoxParam;
        try
        {
            scopeBoxParam = planView.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
        }
        catch (Exception)
        {
            return null;
        }

        if (scopeBoxParam == null)
        {
            return null;
        }

        ElementId scopeBoxId;
        try
        {
            scopeBoxId = scopeBoxParam.AsElementId();
        }
        catch (Exception)
        {
            return null;
        }

        if (scopeBoxId == null || scopeBoxId == ElementId.InvalidElementId)
        {
            return null;
        }

        Element? scopeBox;
        try
        {
            scopeBox = planView.Document?.GetElement(scopeBoxId);
        }
        catch (Exception)
        {
            return null;
        }

        if (scopeBox == null)
        {
            return null;
        }

        return scopeBox;
    }

    private static BoundingBoxXYZ? TryGetElementBounds(Element element)
    {
        BoundingBoxXYZ? bbox;
        try
        {
            bbox = element.get_BoundingBox(null);
        }
        catch (Exception)
        {
            return null;
        }

        return bbox;
    }

    private static double GetSectionBoxRotationDegrees(BoundingBoxXYZ sectionBox)
    {
        Transform transform = sectionBox.Transform ?? Transform.Identity;
        XYZ basisX = transform.BasisX;
        double xyLength = Math.Sqrt((basisX.X * basisX.X) + (basisX.Y * basisX.Y));
        if (xyLength < 1e-9d)
        {
            return 0d;
        }

        return Math.Atan2(basisX.Y / xyLength, basisX.X / xyLength) * 180d / Math.PI;
    }

    private static double? TryGetLevelFloorTopZ(Document document, Level level)
    {
        if (level == null)
        {
            return null;
        }

        FilteredElementCollector collector;
        try
        {
            collector = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType();
        }
        catch (Exception)
        {
            return null;
        }

        double bestZ = double.NaN;
        bool found = false;

        foreach (Element floor in collector)
        {
            if (floor.LevelId != level.Id)
            {
                continue;
            }

            BoundingBoxXYZ? bbox;
            try
            {
                bbox = floor.get_BoundingBox(null);
            }
            catch (Exception)
            {
                continue;
            }

            if (bbox == null)
            {
                continue;
            }

            Transform t = bbox.Transform ?? Transform.Identity;
            double topLocal = bbox.Max.Z;
            XYZ worldTop = t.OfPoint(new XYZ(bbox.Min.X, bbox.Min.Y, topLocal));
            // For an axis-aligned floor with identity transform this is just bbox.Max.Z;
            // for any rotated transform, the highest world-Z corner is what we want.
            double worldZ = worldTop.Z;
            for (int i = 1; i < 8; i++)
            {
                XYZ corner = t.OfPoint(new XYZ(
                    (i & 1) != 0 ? bbox.Max.X : bbox.Min.X,
                    (i & 2) != 0 ? bbox.Max.Y : bbox.Min.Y,
                    (i & 4) != 0 ? bbox.Max.Z : bbox.Min.Z));
                if (corner.Z > worldZ) worldZ = corner.Z;
            }

            if (!found || worldZ < bestZ)
            {
                bestZ = worldZ;
                found = true;
            }
        }

        return found ? (double?)bestZ : null;
    }

    private static bool HasUsefulXyExtent(BoundingBoxXYZ? bbox)
    {
        if (bbox == null) return false;
        return (bbox.Max.X - bbox.Min.X) >= MinUsefulXyExtentFeet &&
               (bbox.Max.Y - bbox.Min.Y) >= MinUsefulXyExtentFeet;
    }

    private static BoundingBoxXYZ? TryComputeScopeBoxUnion(Document document)
    {
        FilteredElementCollector collector;
        try
        {
            collector = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType();
        }
        catch (Exception)
        {
            return null;
        }

        return ComputeUnionWorldBounds(collector);
    }

    private static BoundingBoxXYZ? TryComputeBuildingCategoryBounds(Document document)
    {
        var categories = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Columns,
        };

        FilteredElementCollector collector;
        try
        {
            collector = new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(categories));
        }
        catch (Exception)
        {
            return null;
        }

        return ComputeUnionWorldBounds(collector);
    }

    private static BoundingBoxXYZ? ComputeUnionWorldBounds(IEnumerable<Element> elements)
    {
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double minZ = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        double maxZ = double.MinValue;
        bool found = false;

        foreach (Element element in elements)
        {
            BoundingBoxXYZ? bbox;
            try
            {
                bbox = element.get_BoundingBox(null);
            }
            catch (Exception)
            {
                continue;
            }

            if (bbox == null)
            {
                continue;
            }

            Transform t = bbox.Transform ?? Transform.Identity;
            XYZ[] corners =
            {
                t.OfPoint(new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z)),
                t.OfPoint(new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z)),
                t.OfPoint(new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z)),
                t.OfPoint(new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z)),
                t.OfPoint(new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z)),
                t.OfPoint(new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z)),
                t.OfPoint(new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z)),
                t.OfPoint(new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z)),
            };

            foreach (XYZ corner in corners)
            {
                if (corner.X < minX) minX = corner.X;
                if (corner.Y < minY) minY = corner.Y;
                if (corner.Z < minZ) minZ = corner.Z;
                if (corner.X > maxX) maxX = corner.X;
                if (corner.Y > maxY) maxY = corner.Y;
                if (corner.Z > maxZ) maxZ = corner.Z;
            }
            found = true;
        }

        if (!found || maxX <= minX || maxY <= minY)
        {
            return null;
        }

        return new BoundingBoxXYZ
        {
            Min = new XYZ(minX, minY, minZ),
            Max = new XYZ(maxX, maxY, maxZ),
        };
    }

    private static BoundingBoxXYZ? TryComputeModelXyBounds(Document document)
    {
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double minZ = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        double maxZ = double.MinValue;
        bool found = false;

        FilteredElementCollector collector;
        try
        {
            collector = new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent();
        }
        catch (Exception)
        {
            return null;
        }

        foreach (Element element in collector)
        {
            BoundingBoxXYZ? bbox;
            try
            {
                bbox = element.get_BoundingBox(null);
            }
            catch (Exception)
            {
                continue;
            }

            if (bbox == null)
            {
                continue;
            }

            if (bbox.Min.X < minX) minX = bbox.Min.X;
            if (bbox.Min.Y < minY) minY = bbox.Min.Y;
            if (bbox.Min.Z < minZ) minZ = bbox.Min.Z;
            if (bbox.Max.X > maxX) maxX = bbox.Max.X;
            if (bbox.Max.Y > maxY) maxY = bbox.Max.Y;
            if (bbox.Max.Z > maxZ) maxZ = bbox.Max.Z;
            found = true;
        }

        if (!found || maxX <= minX || maxY <= minY)
        {
            return null;
        }

        return new BoundingBoxXYZ
        {
            Min = new XYZ(minX, minY, minZ),
            Max = new XYZ(maxX, maxY, maxZ),
        };
    }

    private ElementId? TryFindThreeDimensionalViewFamilyType()
    {
        ViewFamilyType? viewFamilyType = new FilteredElementCollector(_document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

        return viewFamilyType?.Id;
    }
}
