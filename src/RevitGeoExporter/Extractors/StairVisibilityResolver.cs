using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Precision;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Extractors;

internal sealed class StairVisibilityResolver
{
    private const double MinPolygonAreaSquareMeters = 0.05d;
    private const double StairCutPlaneToleranceFeet = 0.10d;
    private const double EvidenceBufferDistanceMeters = 0.20d;
    private const double EvidenceCoverageBoundaryToleranceMeters = 0.02d;

    private static readonly GeometryFactory GeometryFactory =
        new(new PrecisionModel(1_000_000d));
    private static readonly HashSet<BuiltInCategory> StairVisiblePolygonCategories = new()
    {
        BuiltInCategory.OST_StairsOutlines,
        BuiltInCategory.OST_StairsCutMarks,
    };
    private static readonly HashSet<BuiltInCategory> StairEvidenceCategories = new()
    {
        BuiltInCategory.OST_StairsOutlines,
        BuiltInCategory.OST_StairsCutMarks,
        BuiltInCategory.OST_StairsRiserLines,
        BuiltInCategory.OST_StairsNosingLines,
    };

    private readonly Document _document;
    private readonly Func<XYZ, Point2D> _projectPoint;

    public StairVisibilityResolver(Document document, Func<XYZ, Point2D> projectPoint)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _projectPoint = projectPoint ?? throw new ArgumentNullException(nameof(projectPoint));
    }

    public bool TryExtractVisibleStairPolygons(
        Stairs stairs,
        ViewPlan? view,
        ICollection<string> warnings,
        out IReadOnlyList<Polygon2D> polygons)
    {
        polygons = Array.Empty<Polygon2D>();
        if (!TryResolveVisibleStair(stairs, view, warnings, out VerticalCirculationVisibilityResult? result))
        {
            return false;
        }

        polygons = result.VisiblePolygons;
        return polygons.Count > 0;
    }

    public bool TryResolveVisibleStair(
        Stairs stairs,
        ViewPlan? view,
        ICollection<string> warnings,
        out VerticalCirculationVisibilityResult result)
    {
        result = null!;
        if (stairs == null)
        {
            return false;
        }

        VerticalCirculationVisibilityEvidence evidence = ExtractEvidence(stairs, view);
        List<StairVisibilityCandidate> candidates = BuildCandidates(stairs, view, warnings, evidence);
        if (candidates.Count == 0)
        {
            return false;
        }

        StairVisibilityCandidate best = candidates
            .OrderByDescending(candidate => candidate, StairVisibilityCandidateComparer.ForEvidence(evidence.HasEvidence))
            .First();

        StairVisibilityCandidate? footprintCutPlane = candidates
            .FirstOrDefault(candidate => candidate.SourceKind == VerticalCirculationVisibilitySourceKind.FootprintCutPlane);
        if (footprintCutPlane != null &&
            best.SourceKind != VerticalCirculationVisibilitySourceKind.FootprintCutPlane &&
            best.Area > footprintCutPlane.Area + MinPolygonAreaSquareMeters)
        {
            StairVisibilityCandidate? clipped = TryClipCandidateToCutPlaneBound(best, footprintCutPlane, evidence);
            if (clipped != null)
            {
                best = clipped;
            }
        }

        string? disagreementWarning = BuildDisagreementWarning(stairs, candidates, best, evidence.HasEvidence);
        if (!string.IsNullOrWhiteSpace(disagreementWarning))
        {
            warnings.Add(disagreementWarning!);
        }

        result = new VerticalCirculationVisibilityResult(
            best.Polygons,
            best.SourceKind,
            best.Area,
            best.EvidenceCount,
            best.CoveredEvidenceCount,
            best.EvidenceCoverageRatio,
            candidates.Count,
            maskApplied: false,
            disagreementWarning,
            best.Geometry,
            evidence,
            best.OverCoverageArea);
        return true;
    }

    public VerticalCirculationVisibilityResult ApplyOcclusionMask(
        Stairs stairs,
        VerticalCirculationVisibilityResult baseResult,
        Geometry? stairOcclusionMask,
        ICollection<string> warnings)
    {
        if (baseResult == null)
        {
            throw new ArgumentNullException(nameof(baseResult));
        }

        if (stairOcclusionMask == null || stairOcclusionMask.IsEmpty)
        {
            return baseResult;
        }

        Geometry visible = SafeDifferenceOrOriginal(baseResult.Geometry, stairOcclusionMask);
        if (visible.IsEmpty)
        {
            return HandleCollapsedOcclusion(stairs, baseResult, warnings, "removed the entire stair footprint");
        }

        List<Polygon2D> visiblePolygons = ExtractPolygons(visible);
        if (visiblePolygons.Count == 0)
        {
            return HandleCollapsedOcclusion(stairs, baseResult, warnings, "produced no valid stair polygons");
        }

        StairVisibilityCandidate refined = CreateCandidate(
            baseResult.SourceKind,
            visiblePolygons,
            visible,
            baseResult.Evidence,
            "occlusion-refined");
        if (refined.Area < MinPolygonAreaSquareMeters)
        {
            return HandleCollapsedOcclusion(stairs, baseResult, warnings, "collapsed below the minimum visible stair area");
        }

        if (Math.Abs(refined.Area - baseResult.Area) <= 1e-6d)
        {
            return baseResult;
        }

        return baseResult.WithMaskApplied(
            visiblePolygons,
            visible,
            refined.Area,
            refined.CoveredEvidenceCount,
            refined.EvidenceCoverageRatio,
            refined.OverCoverageArea,
            null);
    }

    private List<StairVisibilityCandidate> BuildCandidates(
        Stairs stairs,
        ViewPlan? view,
        ICollection<string> warnings,
        VerticalCirculationVisibilityEvidence evidence)
    {
        List<StairVisibilityCandidate> candidates = new();
        if (view != null &&
            ReferenceEquals(view.Document, _document) &&
            TryExtractViewGraphicsPolygons(stairs, view, out List<Polygon2D> graphicsPolygons) &&
            TryCreateCandidate(VerticalCirculationVisibilitySourceKind.ViewGraphics, graphicsPolygons, evidence, out StairVisibilityCandidate? graphicsCandidate))
        {
            candidates.Add(graphicsCandidate);
        }

        if (TryExtractFootprintPolygons(stairs, view, warnings, out List<Polygon2D> footprintPolygons) &&
            TryCreateCandidate(VerticalCirculationVisibilitySourceKind.FootprintCutPlane, footprintPolygons, evidence, out StairVisibilityCandidate? footprintCandidate))
        {
            candidates.Add(footprintCandidate);
        }

        if (view != null &&
            ReferenceEquals(view.Document, _document) &&
            TryExtractElementPolygonsInView(stairs, view, out List<Polygon2D> viewPolygons) &&
            TryCreateCandidate(VerticalCirculationVisibilitySourceKind.ViewGeometry, viewPolygons, evidence, out StairVisibilityCandidate? viewCandidate))
        {
            candidates.Add(viewCandidate);
        }

        if (TryExtractElementPolygons(stairs, out List<Polygon2D> rawPolygons) &&
            TryCreateCandidate(VerticalCirculationVisibilitySourceKind.RawGeometry, rawPolygons, evidence, out StairVisibilityCandidate? rawCandidate))
        {
            candidates.Add(rawCandidate);
        }

        return candidates;
    }

    private bool TryExtractViewGraphicsPolygons(
        Stairs stairs,
        ViewPlan view,
        out List<Polygon2D> polygons)
    {
        polygons = new List<Polygon2D>();
        List<Geometry> linework = CollectDependentCurveLinework(stairs, view, StairVisiblePolygonCategories);
        if (linework.Count == 0)
        {
            return false;
        }

        Polygonizer polygonizer = new();
        polygonizer.Add(linework);
        var createdPolygons = polygonizer.GetPolygons();
        if (createdPolygons == null)
        {
            return false;
        }

        foreach (Geometry geometry in createdPolygons)
        {
            polygons.AddRange(ExtractPolygons(geometry));
        }

        return polygons.Count > 0;
    }

    private VerticalCirculationVisibilityEvidence ExtractEvidence(Stairs stairs, ViewPlan? view)
    {
        if (view == null || !ReferenceEquals(view.Document, _document))
        {
            return VerticalCirculationVisibilityEvidence.Empty;
        }

        List<Geometry> linework = CollectDependentCurveLinework(stairs, view, StairEvidenceCategories);
        if (linework.Count == 0)
        {
            return VerticalCirculationVisibilityEvidence.Empty;
        }

        double totalLength = linework.Sum(geometry => geometry.Length);
        Geometry? bufferedEvidence = null;
        if (totalLength > 1e-9d)
        {
            Geometry unioned = UnaryUnionOp.Union(linework);
            bufferedEvidence = unioned.IsEmpty ? null : unioned.Buffer(EvidenceBufferDistanceMeters).Buffer(0d);
        }

        return new VerticalCirculationVisibilityEvidence(linework, totalLength, bufferedEvidence);
    }

    private List<Geometry> CollectDependentCurveLinework(
        Stairs stairs,
        ViewPlan view,
        ISet<BuiltInCategory> supportedCategories)
    {
        ICollection<ElementId> dependentIds;
        try
        {
            dependentIds = stairs.GetDependentElements(null);
        }
        catch (Exception)
        {
            return new List<Geometry>();
        }

        if (dependentIds == null || dependentIds.Count == 0)
        {
            return new List<Geometry>();
        }

        List<Geometry> linework = new();
        foreach (ElementId dependentId in dependentIds)
        {
            Element? dependent = _document.GetElement(dependentId);
            if (dependent is not CurveElement curveElement)
            {
                continue;
            }

            if (curveElement.OwnerViewId != ElementId.InvalidElementId &&
                curveElement.OwnerViewId != view.Id)
            {
                continue;
            }

            if (!TryGetBuiltInCategory(curveElement, out BuiltInCategory category) ||
                !supportedCategories.Contains(category))
            {
                continue;
            }

            if (!TryCreateLineGeometry(curveElement.GeometryCurve, out Geometry? geometry) ||
                geometry == null ||
                geometry.IsEmpty)
            {
                continue;
            }

            linework.Add(geometry);
        }

        return linework;
    }

    private bool TryCreateCandidate(
        VerticalCirculationVisibilitySourceKind sourceKind,
        IReadOnlyList<Polygon2D> polygons,
        VerticalCirculationVisibilityEvidence evidence,
        out StairVisibilityCandidate candidate)
    {
        candidate = null!;
        if (polygons == null || polygons.Count == 0)
        {
            return false;
        }

        List<Geometry> polygonGeometries = new();
        for (int i = 0; i < polygons.Count; i++)
        {
            Geometry? polygonGeometry = ToNtsGeometry(polygons[i]);
            if (polygonGeometry != null && !polygonGeometry.IsEmpty)
            {
                AddPolygonGeometryParts(polygonGeometries, polygonGeometry);
            }
        }

        if (polygonGeometries.Count == 0)
        {
            return false;
        }

        Geometry unioned = UnaryUnionOp.Union(polygonGeometries).Buffer(0d);
        if (unioned.IsEmpty)
        {
            return false;
        }

        candidate = CreateCandidate(sourceKind, polygons, unioned, evidence, sourceKind.ToString());
        return true;
    }

    private StairVisibilityCandidate CreateCandidate(
        VerticalCirculationVisibilitySourceKind sourceKind,
        IReadOnlyList<Polygon2D> polygons,
        Geometry geometry,
        VerticalCirculationVisibilityEvidence evidence,
        string note)
    {
        int coveredEvidenceCount = 0;
        double coveredLength = 0d;
        if (evidence.HasEvidence)
        {
            Geometry candidateBoundaryBuffer = geometry.Buffer(EvidenceCoverageBoundaryToleranceMeters);
            for (int i = 0; i < evidence.Lines.Count; i++)
            {
                Geometry evidenceLine = evidence.Lines[i];
                double evidenceLength = evidenceLine.Length;
                if (evidenceLength <= 1e-9d)
                {
                    continue;
                }

                Geometry coveredGeometry = SafeOverlayOrEmpty(evidenceLine, candidateBoundaryBuffer, (a, b) => a.Intersection(b));
                double covered = Math.Min(evidenceLength, coveredGeometry.Length);
                coveredLength += covered;
                if (covered >= evidenceLength * 0.80d)
                {
                    coveredEvidenceCount++;
                }
            }
        }

        double evidenceCoverageRatio = evidence.HasEvidence && evidence.TotalLength > 1e-9d
            ? Math.Max(0d, Math.Min(1d, coveredLength / evidence.TotalLength))
            : 0d;
        double overlapArea = 0d;
        if (evidence.BufferedGeometry != null && !evidence.BufferedGeometry.IsEmpty)
        {
            overlapArea = SafeOverlayOrEmpty(geometry, evidence.BufferedGeometry, (a, b) => a.Intersection(b).Buffer(0d)).Area;
        }

        return new StairVisibilityCandidate(
            sourceKind,
            polygons,
            geometry,
            geometry.Area,
            evidence.Lines.Count,
            coveredEvidenceCount,
            evidenceCoverageRatio,
            Math.Max(0d, geometry.Area - overlapArea),
            GetPriority(sourceKind),
            note);
    }

    private static string? BuildDisagreementWarning(
        Stairs stairs,
        IReadOnlyList<StairVisibilityCandidate> candidates,
        StairVisibilityCandidate best,
        bool hasEvidence)
    {
        if (candidates.Count < 2)
        {
            return null;
        }

        StairVisibilityCandidate secondBest = candidates
            .Where(candidate => !ReferenceEquals(candidate, best))
            .OrderByDescending(candidate => candidate, StairVisibilityCandidateComparer.ForEvidence(hasEvidence))
            .First();

        if (secondBest.SourceKind == best.SourceKind)
        {
            return null;
        }

        bool coverageClose = Math.Abs(best.EvidenceCoverageRatio - secondBest.EvidenceCoverageRatio) <= 0.05d;
        bool areaDisagrees = secondBest.Area > best.Area * 1.25d || best.Area > secondBest.Area * 1.25d;
        if (hasEvidence && coverageClose && areaDisagrees)
        {
            return $"Stairs {stairs.Id.Value} visibility candidates disagreed; selected {best.Source} over {secondBest.Source} based on plan-view stair graphics.";
        }

        if (!hasEvidence && Math.Abs(best.Area - secondBest.Area) > MinPolygonAreaSquareMeters * 2d)
        {
            return $"Stairs {stairs.Id.Value} used {best.Source} because multiple stair visibility candidates disagreed and no decisive plan-view stair graphics were available.";
        }

        return null;
    }

    private StairVisibilityCandidate? TryClipCandidateToCutPlaneBound(
        StairVisibilityCandidate candidate,
        StairVisibilityCandidate cutPlaneBound,
        VerticalCirculationVisibilityEvidence evidence)
    {
        Geometry clipped = SafeOverlayOrEmpty(
            candidate.Geometry,
            cutPlaneBound.Geometry,
            (a, b) => a.Intersection(b).Buffer(0d));
        if (clipped.IsEmpty)
        {
            return null;
        }

        List<Polygon2D> clippedPolygons = ExtractPolygons(clipped);
        if (clippedPolygons.Count == 0)
        {
            return null;
        }

        StairVisibilityCandidate refined = CreateCandidate(
            candidate.SourceKind,
            clippedPolygons,
            clipped,
            evidence,
            "cut-plane-clipped");
        return refined.Area >= MinPolygonAreaSquareMeters ? refined : null;
    }

    private static int GetPriority(VerticalCirculationVisibilitySourceKind sourceKind)
    {
        return sourceKind switch
        {
            VerticalCirculationVisibilitySourceKind.ViewGraphics => 4,
            VerticalCirculationVisibilitySourceKind.FootprintCutPlane => 3,
            VerticalCirculationVisibilitySourceKind.ViewGeometry => 2,
            VerticalCirculationVisibilitySourceKind.RawGeometry => 1,
            _ => 0,
        };
    }

    private bool TryExtractFootprintPolygons(
        Stairs stairs,
        ViewPlan? view,
        ICollection<string> warnings,
        out List<Polygon2D> polygons)
    {
        polygons = new List<Polygon2D>();
        double cutElevationFeet = 0d;
        bool hasCutElevation =
            view != null &&
            ReferenceEquals(view.Document, _document) &&
            TryGetViewCutElevationFeet(view, out cutElevationFeet);
        double stairBaseElevationFeet = GetStairBaseElevationFeet(stairs);

        List<Geometry> geometries = new();
        foreach (ElementId runId in stairs.GetStairsRuns())
        {
            if (_document.GetElement(runId) is not StairsRun run)
            {
                continue;
            }

            try
            {
                CurveLoop runBoundary = run.GetFootprintBoundary();
                if (TryCreatePolygonFromCurveLoop(runBoundary, out Polygon2D runPolygon))
                {
                    IReadOnlyList<Polygon2D> visibleRunPolygons;
                    if (hasCutElevation)
                    {
                        if (!TryGetVisibleRunPolygons(run, runPolygon, cutElevationFeet, stairBaseElevationFeet, warnings, out visibleRunPolygons))
                        {
                            visibleRunPolygons = new[] { runPolygon };
                        }
                    }
                    else
                    {
                        visibleRunPolygons = new[] { runPolygon };
                    }

                    for (int i = 0; i < visibleRunPolygons.Count; i++)
                    {
                        Geometry? geometry = ToNtsGeometry(visibleRunPolygons[i]);
                        if (geometry != null && !geometry.IsEmpty)
                        {
                            AddPolygonGeometryParts(geometries, geometry);
                        }
                    }
                }
            }
            catch (Exception)
            {
                warnings.Add($"Stairs run {run.Id.Value} footprint boundary could not be read.");
            }
        }

        foreach (ElementId landingId in stairs.GetStairsLandings())
        {
            if (_document.GetElement(landingId) is not StairsLanding landing)
            {
                continue;
            }

            try
            {
                CurveLoop landingBoundary = landing.GetFootprintBoundary();
                if (TryCreatePolygonFromCurveLoop(landingBoundary, out Polygon2D landingPolygon))
                {
                    if (hasCutElevation &&
                        view != null &&
                        !IsElementVisibleAtOrBelowCutPlane(landing, view, cutElevationFeet))
                    {
                        continue;
                    }

                    Geometry? geometry = ToNtsGeometry(landingPolygon);
                    if (geometry != null && !geometry.IsEmpty)
                    {
                        AddPolygonGeometryParts(geometries, geometry);
                    }
                }
            }
            catch (Exception)
            {
                warnings.Add($"Stairs landing {landing.Id.Value} footprint boundary could not be read.");
            }
        }

        if (geometries.Count == 0)
        {
            return false;
        }

        Geometry unioned = UnaryUnionOp.Union(geometries);
        if (unioned.IsEmpty)
        {
            return false;
        }

        polygons = ExtractPolygons(unioned.Buffer(0d));
        return polygons.Count > 0;
    }

    private bool TryGetVisibleRunPolygons(
        StairsRun run,
        Polygon2D runPolygon,
        double cutElevationFeet,
        double stairBaseElevationFeet,
        ICollection<string> warnings,
        out IReadOnlyList<Polygon2D> polygons)
    {
        polygons = Array.Empty<Polygon2D>();

        double runBaseFeet = stairBaseElevationFeet + run.BaseElevation;
        double runTopFeet = stairBaseElevationFeet + run.TopElevation;
        double runRiseFeet = runTopFeet - runBaseFeet;
        if (runRiseFeet <= 1e-6d)
        {
            polygons = new[] { runPolygon };
            return true;
        }

        if (cutElevationFeet <= runBaseFeet + StairCutPlaneToleranceFeet)
        {
            return true;
        }

        if (cutElevationFeet >= runTopFeet - StairCutPlaneToleranceFeet)
        {
            polygons = new[] { runPolygon };
            return true;
        }

        double visibleFraction = (cutElevationFeet - runBaseFeet) / runRiseFeet;
        visibleFraction = Math.Max(0d, Math.Min(1d, visibleFraction));

        if (TryClipRunPolygonByVisibleFraction(run, runPolygon, visibleFraction, out List<Polygon2D> clippedPolygons) &&
            clippedPolygons.Count > 0)
        {
            polygons = clippedPolygons;
            return true;
        }

        warnings.Add($"Stairs run {run.Id.Value} visible clipping failed; full run footprint was used.");
        return false;
    }

    private bool TryClipRunPolygonByVisibleFraction(
        StairsRun run,
        Polygon2D runPolygon,
        double visibleFraction,
        out List<Polygon2D> clippedPolygons)
    {
        clippedPolygons = new List<Polygon2D>();

        CurveLoop pathLoop;
        try
        {
            pathLoop = run.GetStairsPath();
        }
        catch (Exception)
        {
            return false;
        }

        List<Point2D> pathPoints = ProjectCurveLoop(pathLoop, closeLoop: false);
        if (pathPoints.Count < 2)
        {
            return false;
        }

        if (!TryInterpolateOnPolyline(pathPoints, visibleFraction, out Point2D cutPoint, out Point2D tangent))
        {
            return false;
        }

        Geometry? runGeometry = ToNtsGeometry(runPolygon);
        if (runGeometry == null || runGeometry.IsEmpty)
        {
            return false;
        }

        double span = Math.Max(10d, GetPolylineLength(pathPoints) * 4d);
        Point2D normal = new(-tangent.Y, tangent.X);
        Point2D p1 = new(cutPoint.X + (normal.X * span), cutPoint.Y + (normal.Y * span));
        Point2D p2 = new(cutPoint.X - (normal.X * span), cutPoint.Y - (normal.Y * span));
        Point2D p3 = new(p2.X - (tangent.X * span), p2.Y - (tangent.Y * span));
        Point2D p4 = new(p1.X - (tangent.X * span), p1.Y - (tangent.Y * span));

        Polygon2D clipPolygon = new(new[] { p1, p2, p3, p4, p1 });
        Geometry? clipGeometry = ToNtsGeometry(clipPolygon);
        if (clipGeometry == null || clipGeometry.IsEmpty)
        {
            return false;
        }

        Geometry clipped;
        try
        {
            clipped = runGeometry.Intersection(clipGeometry).Buffer(0d);
        }
        catch (TopologyException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                Geometry reducedRun = reducer.Reduce(runGeometry);
                Geometry reducedClip = reducer.Reduce(clipGeometry);
                clipped = reducedRun.Intersection(reducedClip).Buffer(0d);
            }
            catch (TopologyException)
            {
                return false;
            }
        }

        if (clipped.IsEmpty)
        {
            return true;
        }

        clippedPolygons = ExtractPolygons(clipped);
        return clippedPolygons.Count > 0;
    }

    private double GetStairBaseElevationFeet(Stairs stairs)
    {
        try
        {
            Parameter? baseLevelParam = stairs.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            if (baseLevelParam != null &&
                _document.GetElement(baseLevelParam.AsElementId()) is Level baseLevel)
            {
                Parameter? baseOffsetParam = stairs.get_Parameter(BuiltInParameter.STAIRS_BASE_OFFSET);
                double baseOffset = baseOffsetParam?.AsDouble() ?? 0d;
                return baseLevel.Elevation + baseOffset;
            }
        }
        catch (Exception)
        {
            // Ignored - fall through to bounding box fallback.
        }

        try
        {
            BoundingBoxXYZ? box = stairs.get_BoundingBox(null);
            if (box != null)
            {
                return box.Min.Z;
            }
        }
        catch (Exception)
        {
            // Ignored.
        }

        return 0d;
    }

    private bool TryGetViewCutElevationFeet(ViewPlan view, out double cutElevationFeet)
    {
        cutElevationFeet = view.GenLevel?.Elevation ?? 0d;
        try
        {
            PlanViewRange viewRange = view.GetViewRange();
            ElementId levelId = viewRange.GetLevelId(PlanViewPlane.CutPlane);
            double offset = viewRange.GetOffset(PlanViewPlane.CutPlane);
            Level? cutLevel = _document.GetElement(levelId) as Level ?? view.GenLevel;
            if (cutLevel == null)
            {
                return false;
            }

            cutElevationFeet = cutLevel.Elevation + offset;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsElementVisibleAtOrBelowCutPlane(Element element, View view, double cutElevationFeet)
    {
        BoundingBoxXYZ? box = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
        if (box == null)
        {
            return true;
        }

        return box.Min.Z <= cutElevationFeet + StairCutPlaneToleranceFeet;
    }

    private bool TryExtractElementPolygonsInView(Element element, View view, out List<Polygon2D> polygons)
    {
        polygons = null!;
        if (element == null || view == null)
        {
            return false;
        }

        List<List<XYZ>> loops = ExtractLoopsFromSolidGeometry(
            element,
            includeNonVisibleObjects: false,
            view: view);

        if (loops.Count == 0)
        {
            loops = ExtractLoopsFromSolidGeometry(
                element,
                includeNonVisibleObjects: true,
                view: view);
        }

        if (loops.Count == 0)
        {
            return false;
        }

        return BuildPolygonsFromLoops(loops, out polygons);
    }

    private bool TryExtractElementPolygons(Element element, out List<Polygon2D> polygons)
    {
        polygons = null!;

        List<List<XYZ>> loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: false);
        if (loops.Count == 0)
        {
            loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: true);
        }

        if (loops.Count == 0)
        {
            return false;
        }

        return BuildPolygonsFromLoops(loops, out polygons);
    }

    private bool BuildPolygonsFromLoops(List<List<XYZ>> loops, out List<Polygon2D> polygons)
    {
        polygons = null!;

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

        polygons = ClassifyLoopsIntoPolygons(projectedLoops);
        return polygons.Count > 0;
    }

    private static List<List<XYZ>> ExtractLoopsFromSolidGeometry(
        Element element,
        bool includeNonVisibleObjects,
        View? view = null)
    {
        Options options = new()
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = includeNonVisibleObjects,
        };
        if (view != null)
        {
            options.View = view;
        }
        else
        {
            options.DetailLevel = ViewDetailLevel.Fine;
        }

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
            if (solid.Volume <= 0d)
            {
                continue;
            }

            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace planarFace)
                {
                    continue;
                }

                if (planarFace.FaceNormal.Z >= -0.9d)
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
                case Solid solid when solid.Volume > 0d:
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

    private bool TryCreatePolygonFromCurveLoop(CurveLoop loop, out Polygon2D polygon)
    {
        polygon = null!;
        if (loop == null)
        {
            return false;
        }

        List<Point2D> points = ProjectCurveLoop(loop, closeLoop: true);
        if (points.Count < 4)
        {
            return false;
        }

        polygon = new Polygon2D(points);
        return true;
    }

    private List<Point2D> ProjectLoop(IReadOnlyList<XYZ> loop)
    {
        List<Point2D> result = new(loop.Count);
        foreach (XYZ point in loop)
        {
            Point2D projected = _projectPoint(point);
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

    private List<Point2D> ProjectCurveLoop(CurveLoop loop, bool closeLoop)
    {
        List<Point2D> points = new();
        foreach (Curve curve in loop)
        {
            List<Point2D> curvePoints = ProjectCurve(curve);
            for (int i = 0; i < curvePoints.Count; i++)
            {
                Point2D point = curvePoints[i];
                if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], point))
                {
                    points.Add(point);
                }
            }
        }

        if (closeLoop && points.Count >= 3 && !IsSamePoint(points[0], points[points.Count - 1]))
        {
            points.Add(points[0]);
        }

        return points;
    }

    private List<Point2D> ProjectCurve(Curve curve)
    {
        IList<XYZ> sampled = curve.Tessellate();
        if (sampled.Count == 0)
        {
            sampled = new List<XYZ>
            {
                curve.GetEndPoint(0),
                curve.GetEndPoint(1),
            };
        }

        List<Point2D> points = new(sampled.Count);
        for (int i = 0; i < sampled.Count; i++)
        {
            Point2D projected = _projectPoint(sampled[i]);
            if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], projected))
            {
                points.Add(projected);
            }
        }

        return points;
    }

    private bool TryCreateLineGeometry(Curve? curve, out Geometry? geometry)
    {
        geometry = null;
        if (curve == null)
        {
            return false;
        }

        List<Point2D> points = ProjectCurve(curve);
        if (points.Count < 2)
        {
            return false;
        }

        Coordinate[] coordinates = points
            .Select(point => new Coordinate(point.X, point.Y))
            .ToArray();
        LineString lineString = GeometryFactory.CreateLineString(coordinates);
        if (lineString.IsEmpty || lineString.Length <= 1e-9d)
        {
            return false;
        }

        geometry = lineString;
        return true;
    }

    private static bool TryGetBuiltInCategory(Element element, out BuiltInCategory category)
    {
        category = default;
        Category? revitCategory = element.Category;
        if (revitCategory == null)
        {
            return false;
        }

        long categoryValue = revitCategory.Id.Value;
        if (categoryValue < int.MinValue || categoryValue > int.MaxValue)
        {
            return false;
        }

        int categoryId = (int)categoryValue;
        if (!Enum.IsDefined(typeof(BuiltInCategory), categoryId))
        {
            return false;
        }

        category = (BuiltInCategory)categoryId;
        return true;
    }

    private static Geometry? ToNtsGeometry(Polygon2D polygon)
    {
        if (!TryCreateLinearRing(polygon.ExteriorRing, out LinearRing? shell))
        {
            return null;
        }

        List<LinearRing> holes = new();
        for (int i = 0; i < polygon.InteriorRings.Count; i++)
        {
            if (TryCreateLinearRing(polygon.InteriorRings[i], out LinearRing? hole) && hole != null)
            {
                holes.Add(hole);
            }
        }

        Polygon created = GeometryFactory.CreatePolygon(shell, holes.ToArray());
        return created.IsValid ? created : created.Buffer(0d);
    }

    private static void AddPolygonGeometryParts(ICollection<Geometry> target, Geometry geometry)
    {
        if (geometry == null || geometry.IsEmpty)
        {
            return;
        }

        switch (geometry)
        {
            case Polygon polygon:
                target.Add(polygon);
                break;
            case MultiPolygon multiPolygon:
                for (int i = 0; i < multiPolygon.NumGeometries; i++)
                {
                    AddPolygonGeometryParts(target, multiPolygon.GetGeometryN(i));
                }

                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    AddPolygonGeometryParts(target, collection.GetGeometryN(i));
                }

                break;
        }
    }

    private static bool TryCreateLinearRing(IReadOnlyList<Point2D> ringPoints, out LinearRing? ring)
    {
        ring = null;
        if (ringPoints == null || ringPoints.Count < 4)
        {
            return false;
        }

        List<Coordinate> coords = new(ringPoints.Count + 1);
        for (int i = 0; i < ringPoints.Count; i++)
        {
            coords.Add(new Coordinate(ringPoints[i].X, ringPoints[i].Y));
        }

        Coordinate first = coords[0];
        Coordinate last = coords[coords.Count - 1];
        if (!first.Equals2D(last))
        {
            coords.Add(new Coordinate(first.X, first.Y));
        }

        if (coords.Count < 4)
        {
            return false;
        }

        ring = GeometryFactory.CreateLinearRing(coords.ToArray());
        return !ring.IsEmpty;
    }

    private static List<Polygon2D> ExtractPolygons(Geometry geometry)
    {
        List<Polygon2D> polygons = new();
        switch (geometry)
        {
            case Polygon polygon:
                AddPolygonIfValid(polygons, polygon);
                break;
            case MultiPolygon multiPolygon:
                for (int i = 0; i < multiPolygon.NumGeometries; i++)
                {
                    if (multiPolygon.GetGeometryN(i) is Polygon child)
                    {
                        AddPolygonIfValid(polygons, child);
                    }
                }

                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    polygons.AddRange(ExtractPolygons(collection.GetGeometryN(i)));
                }

                break;
        }

        return polygons;
    }

    private static void AddPolygonIfValid(ICollection<Polygon2D> target, Polygon polygon)
    {
        if (polygon.IsEmpty || polygon.Area < MinPolygonAreaSquareMeters)
        {
            return;
        }

        Polygon2D? converted = ToPolygon2D(polygon);
        if (converted != null)
        {
            target.Add(converted);
        }
    }

    private static Polygon2D? ToPolygon2D(Polygon polygon)
    {
        IReadOnlyList<Point2D>? exterior = ToPointList(polygon.ExteriorRing.Coordinates);
        if (exterior == null)
        {
            return null;
        }

        List<IReadOnlyList<Point2D>> interior = new();
        for (int i = 0; i < polygon.NumInteriorRings; i++)
        {
            IReadOnlyList<Point2D>? ring = ToPointList(polygon.GetInteriorRingN(i).Coordinates);
            if (ring != null)
            {
                interior.Add(ring);
            }
        }

        return new Polygon2D(exterior, interior);
    }

    private static IReadOnlyList<Point2D>? ToPointList(Coordinate[] coordinates)
    {
        if (coordinates == null || coordinates.Length < 4)
        {
            return null;
        }

        List<Point2D> points = new(coordinates.Length);
        for (int i = 0; i < coordinates.Length; i++)
        {
            points.Add(new Point2D(coordinates[i].X, coordinates[i].Y));
        }

        return points;
    }

    private static List<Polygon2D> ClassifyLoopsIntoPolygons(List<List<Point2D>> projectedLoops)
    {
        List<(List<Point2D> Points, Polygon NtsPolygon, double Area)> loopPolygons = new();
        foreach (List<Point2D> loop in projectedLoops)
        {
            if (!TryCreateLinearRing(loop, out LinearRing? ring) || ring == null)
            {
                continue;
            }

            Polygon ntsPoly = GeometryFactory.CreatePolygon(ring);
            if (ntsPoly.IsEmpty)
            {
                continue;
            }

            loopPolygons.Add((loop, ntsPoly, ntsPoly.Area));
        }

        if (loopPolygons.Count == 0)
        {
            return new List<Polygon2D>();
        }

        int count = loopPolygons.Count;
        bool[] isExterior = new bool[count];
        int[] parentIndex = new int[count];
        for (int i = 0; i < count; i++)
        {
            parentIndex[i] = -1;
        }

        for (int i = 0; i < count; i++)
        {
            NetTopologySuite.Geometries.Point interiorPoint = loopPolygons[i].NtsPolygon.InteriorPoint;
            bool contained = false;
            for (int j = 0; j < count; j++)
            {
                if (j == i)
                {
                    continue;
                }

                if (loopPolygons[j].Area > loopPolygons[i].Area &&
                    loopPolygons[j].NtsPolygon.Contains(interiorPoint))
                {
                    contained = true;
                    if (parentIndex[i] == -1 || loopPolygons[j].Area < loopPolygons[parentIndex[i]].Area)
                    {
                        parentIndex[i] = j;
                    }
                }
            }

            if (!contained)
            {
                isExterior[i] = true;
            }
        }

        if (!isExterior.Any(x => x))
        {
            int largestIndex = 0;
            for (int i = 1; i < count; i++)
            {
                if (loopPolygons[i].Area > loopPolygons[largestIndex].Area)
                {
                    largestIndex = i;
                }
            }

            isExterior[largestIndex] = true;
        }

        List<Polygon2D> result = new();
        for (int i = 0; i < count; i++)
        {
            if (!isExterior[i])
            {
                continue;
            }

            List<IReadOnlyList<Point2D>> holes = new();
            for (int j = 0; j < count; j++)
            {
                if (j == i || isExterior[j])
                {
                    continue;
                }

                if (parentIndex[j] == i)
                {
                    holes.Add(loopPolygons[j].Points);
                }
            }

            result.Add(new Polygon2D(loopPolygons[i].Points, holes));
        }

        return result;
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

    private static bool TryInterpolateOnPolyline(
        IReadOnlyList<Point2D> points,
        double fraction,
        out Point2D point,
        out Point2D tangent)
    {
        point = default;
        tangent = default;
        if (points == null || points.Count < 2)
        {
            return false;
        }

        double totalLength = GetPolylineLength(points);
        if (totalLength <= 1e-9d)
        {
            return false;
        }

        double clamped = Math.Max(0d, Math.Min(1d, fraction));
        double target = totalLength * clamped;
        double traversed = 0d;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Point2D a = points[i];
            Point2D b = points[i + 1];
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double segmentLength = Math.Sqrt((dx * dx) + (dy * dy));
            if (segmentLength <= 1e-9d)
            {
                continue;
            }

            double next = traversed + segmentLength;
            if (target <= next || i == points.Count - 2)
            {
                double local = Math.Max(0d, Math.Min(1d, (target - traversed) / segmentLength));
                point = new Point2D(a.X + (dx * local), a.Y + (dy * local));
                tangent = new Point2D(dx / segmentLength, dy / segmentLength);
                return true;
            }

            traversed = next;
        }

        return false;
    }

    private static double GetPolylineLength(IReadOnlyList<Point2D> points)
    {
        if (points == null || points.Count < 2)
        {
            return 0d;
        }

        double length = 0d;
        for (int i = 0; i < points.Count - 1; i++)
        {
            double dx = points[i + 1].X - points[i].X;
            double dy = points[i + 1].Y - points[i].Y;
            length += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return length;
    }

    private VerticalCirculationVisibilityResult HandleCollapsedOcclusion(
        Stairs stairs,
        VerticalCirculationVisibilityResult baseResult,
        ICollection<string> warnings,
        string collapseReason)
    {
        string keepWarning =
            $"Stairs {stairs.Id.Value} floor/opening occlusion {collapseReason}. Keeping the pre-mask stair footprint.";
        warnings.Add(keepWarning);
        return baseResult.WithWarning(keepWarning);
    }

    private static StairVisibilityCandidate CreateCandidate(VerticalCirculationVisibilityResult result)
    {
        return new StairVisibilityCandidate(
            result.SourceKind,
            result.VisiblePolygons,
            result.Geometry,
            result.Area,
            result.EvidenceCount,
            result.CoveredEvidenceCount,
            result.EvidenceCoverageRatio,
            result.OverCoverageArea,
            GetPriority(result.SourceKind),
            "selected");
    }

    private static Geometry SafeDifferenceOrOriginal(Geometry source, Geometry mask)
    {
        return SafeOperation(source, mask, (a, b) => a.Difference(b).Buffer(0d), source);
    }

    private static Geometry SafeOverlayOrEmpty(
        Geometry first,
        Geometry second,
        Func<Geometry, Geometry, Geometry> operation)
    {
        return SafeOperation(first, second, operation, GeometryFactory.CreateGeometryCollection(Array.Empty<Geometry>()));
    }

    private static Geometry SafeOperation(
        Geometry first,
        Geometry second,
        Func<Geometry, Geometry, Geometry> operation,
        Geometry fallback)
    {
        try
        {
            return operation(first, second);
        }
        catch (TopologyException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                Geometry reducedFirst = reducer.Reduce(first);
                Geometry reducedSecond = reducer.Reduce(second);
                return operation(reducedFirst, reducedSecond);
            }
            catch (TopologyException)
            {
                return fallback;
            }
        }
    }

    private sealed class StairVisibilityCandidate
    {
        public StairVisibilityCandidate(
            VerticalCirculationVisibilitySourceKind sourceKind,
            IReadOnlyList<Polygon2D> polygons,
            Geometry geometry,
            double area,
            int evidenceCount,
            int coveredEvidenceCount,
            double evidenceCoverageRatio,
            double overCoverageArea,
            int priority,
            string note)
        {
            SourceKind = sourceKind;
            Polygons = polygons;
            Geometry = geometry;
            Area = area;
            EvidenceCount = evidenceCount;
            CoveredEvidenceCount = coveredEvidenceCount;
            EvidenceCoverageRatio = evidenceCoverageRatio;
            OverCoverageArea = overCoverageArea;
            Priority = priority;
            Note = note;
        }

        public VerticalCirculationVisibilitySourceKind SourceKind { get; }

        public string Source => SourceKind switch
        {
            VerticalCirculationVisibilitySourceKind.ViewGraphics => "view-graphics",
            VerticalCirculationVisibilitySourceKind.FootprintCutPlane => "footprint-cut-plane",
            VerticalCirculationVisibilitySourceKind.ViewGeometry => "view-geometry",
            VerticalCirculationVisibilitySourceKind.CutPlaneClip => "cut-plane-clip",
            VerticalCirculationVisibilitySourceKind.RawGeometry => "raw-fallback",
            _ => "unknown",
        };

        public IReadOnlyList<Polygon2D> Polygons { get; }

        public Geometry Geometry { get; }

        public double Area { get; }

        public int EvidenceCount { get; }

        public int CoveredEvidenceCount { get; }

        public double EvidenceCoverageRatio { get; }

        public double OverCoverageArea { get; }

        public int Priority { get; }

        public string Note { get; }
    }

    private sealed class StairVisibilityCandidateComparer : IComparer<StairVisibilityCandidate>
    {
        private readonly bool _hasEvidence;

        private StairVisibilityCandidateComparer(bool hasEvidence)
        {
            _hasEvidence = hasEvidence;
        }

        public static StairVisibilityCandidateComparer ForEvidence(bool hasEvidence)
        {
            return new StairVisibilityCandidateComparer(hasEvidence);
        }

        public int Compare(StairVisibilityCandidate? left, StairVisibilityCandidate? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            if (_hasEvidence)
            {
                int coverage = CompareDescending(left.EvidenceCoverageRatio, right.EvidenceCoverageRatio, 0.01d);
                if (coverage != 0)
                {
                    return coverage;
                }

                int coveredCount = left.CoveredEvidenceCount.CompareTo(right.CoveredEvidenceCount);
                if (coveredCount != 0)
                {
                    return coveredCount;
                }

                int overCoverage = CompareAscending(left.OverCoverageArea, right.OverCoverageArea, 0.02d);
                if (overCoverage != 0)
                {
                    return overCoverage;
                }

                int area = CompareAscending(left.Area, right.Area, 0.02d);
                if (area != 0)
                {
                    return area;
                }
            }

            int priority = left.Priority.CompareTo(right.Priority);
            if (priority != 0)
            {
                return priority;
            }

            return CompareAscending(left.Area, right.Area, 0.02d);
        }

        private static int CompareDescending(double left, double right, double tolerance)
        {
            if (Math.Abs(left - right) <= tolerance)
            {
                return 0;
            }

            return left > right ? 1 : -1;
        }

        private static int CompareAscending(double left, double right, double tolerance)
        {
            if (Math.Abs(left - right) <= tolerance)
            {
                return 0;
            }

            return left < right ? 1 : -1;
        }
    }
}
