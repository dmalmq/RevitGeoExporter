using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Extractors;

internal enum VerticalCirculationVisibilitySourceKind
{
    ViewGraphics = 0,
    FootprintCutPlane = 1,
    ViewGeometry = 2,
    CutPlaneClip = 3,
    RectangleProjection = 4,
    ConservativeComposite = 5,
    RawGeometry = 6,
}

internal sealed class VerticalCirculationVisibilityResult
{
    public VerticalCirculationVisibilityResult(
        IReadOnlyList<Polygon2D> visiblePolygons,
        VerticalCirculationVisibilitySourceKind sourceKind,
        double area,
        int evidenceCount,
        int coveredEvidenceCount,
        double evidenceCoverageRatio,
        int candidateCount,
        bool maskApplied,
        string? warning,
        Geometry geometry,
        VerticalCirculationVisibilityEvidence evidence,
        double overCoverageArea,
        ExportPolygon? exportFeature = null)
    {
        VisiblePolygons = visiblePolygons ?? throw new ArgumentNullException(nameof(visiblePolygons));
        if (visiblePolygons.Count == 0)
        {
            throw new ArgumentException("At least one visible polygon is required.", nameof(visiblePolygons));
        }

        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        SourceKind = sourceKind;
        Area = area;
        EvidenceCount = Math.Max(0, evidenceCount);
        CoveredEvidenceCount = Math.Max(0, coveredEvidenceCount);
        EvidenceCoverageRatio = evidenceCoverageRatio;
        CandidateCount = Math.Max(1, candidateCount);
        MaskApplied = maskApplied;
        Warning = NormalizeWarning(warning);
        OverCoverageArea = Math.Max(0d, overCoverageArea);
        ExportFeature = exportFeature;
    }

    public IReadOnlyList<Polygon2D> VisiblePolygons { get; }

    public VerticalCirculationVisibilitySourceKind SourceKind { get; }

    public string Source => SourceKind switch
    {
        VerticalCirculationVisibilitySourceKind.ViewGraphics => "view-graphics",
        VerticalCirculationVisibilitySourceKind.FootprintCutPlane => "footprint-cut-plane",
        VerticalCirculationVisibilitySourceKind.ViewGeometry => "view-geometry",
        VerticalCirculationVisibilitySourceKind.CutPlaneClip => "cut-plane-clip",
        VerticalCirculationVisibilitySourceKind.RectangleProjection => "rectangle-projection",
        VerticalCirculationVisibilitySourceKind.ConservativeComposite => "conservative-composite",
        VerticalCirculationVisibilitySourceKind.RawGeometry => "raw-fallback",
        _ => "unknown",
    };

    public double Area { get; }

    public int EvidenceCount { get; }

    public int CoveredEvidenceCount { get; }

    public double EvidenceCoverageRatio { get; }

    public int CandidateCount { get; }

    public bool MaskApplied { get; }

    public string? Warning { get; }

    public ExportPolygon? ExportFeature { get; }

    internal Geometry Geometry { get; }

    internal VerticalCirculationVisibilityEvidence Evidence { get; }

    internal double OverCoverageArea { get; }

    internal bool HasEvidence => Evidence.HasEvidence;

    public VerticalCirculationVisibilityResult WithExportFeature(ExportPolygon exportFeature)
    {
        return new VerticalCirculationVisibilityResult(
            VisiblePolygons,
            SourceKind,
            Area,
            EvidenceCount,
            CoveredEvidenceCount,
            EvidenceCoverageRatio,
            CandidateCount,
            MaskApplied,
            Warning,
            Geometry,
            Evidence,
            OverCoverageArea,
            exportFeature);
    }

    public VerticalCirculationVisibilityResult WithMaskApplied(
        IReadOnlyList<Polygon2D> visiblePolygons,
        Geometry geometry,
        double area,
        int coveredEvidenceCount,
        double evidenceCoverageRatio,
        double overCoverageArea,
        string? warning)
    {
        return new VerticalCirculationVisibilityResult(
            visiblePolygons,
            SourceKind,
            area,
            EvidenceCount,
            coveredEvidenceCount,
            evidenceCoverageRatio,
            CandidateCount,
            maskApplied: true,
            CombineWarnings(Warning, warning),
            geometry,
            Evidence,
            overCoverageArea,
            ExportFeature);
    }

    public VerticalCirculationVisibilityResult WithWarning(string warning)
    {
        return new VerticalCirculationVisibilityResult(
            VisiblePolygons,
            SourceKind,
            Area,
            EvidenceCount,
            CoveredEvidenceCount,
            EvidenceCoverageRatio,
            CandidateCount,
            MaskApplied,
            CombineWarnings(Warning, warning),
            Geometry,
            Evidence,
            OverCoverageArea,
            ExportFeature);
    }

    public IReadOnlyDictionary<string, object?> BuildDebugAttributes()
    {
        Dictionary<string, object?> attributes = new(StringComparer.Ordinal)
        {
            ["stair_visibility_source"] = Source,
            ["stair_visibility_evidence_count"] = EvidenceCount,
            ["stair_visibility_candidate_count"] = CandidateCount,
            ["stair_visibility_mask_applied"] = MaskApplied,
            ["stair_visibility_warning"] = Warning,
        };

        return attributes;
    }

    private static string? NormalizeWarning(string? warning)
    {
        string trimmed = warning?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? CombineWarnings(string? first, string? second)
    {
        string? normalizedFirst = NormalizeWarning(first);
        string? normalizedSecond = NormalizeWarning(second);
        if (normalizedFirst == null)
        {
            return normalizedSecond;
        }

        if (normalizedSecond == null)
        {
            return normalizedFirst;
        }

        return string.Equals(normalizedFirst, normalizedSecond, StringComparison.Ordinal)
            ? normalizedFirst
            : $"{normalizedFirst} {normalizedSecond}";
    }
}

internal sealed class VerticalCirculationVisibilityEvidence
{
    public static VerticalCirculationVisibilityEvidence Empty { get; } = new(
        Array.Empty<Geometry>(),
        totalLength: 0d,
        bufferedGeometry: null);

    public VerticalCirculationVisibilityEvidence(
        IReadOnlyList<Geometry> lines,
        double totalLength,
        Geometry? bufferedGeometry)
    {
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        TotalLength = Math.Max(0d, totalLength);
        BufferedGeometry = bufferedGeometry;
    }

    public IReadOnlyList<Geometry> Lines { get; }

    public double TotalLength { get; }

    public Geometry? BufferedGeometry { get; }

    public bool HasEvidence => Lines.Count > 0 && TotalLength > 1e-9d;
}
