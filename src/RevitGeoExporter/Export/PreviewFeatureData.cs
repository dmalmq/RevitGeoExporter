using System;
using System.Linq;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Export;

public sealed class PreviewFeatureData
{
    public PreviewFeatureData(
        ExportFeatureType featureType,
        IExportFeature feature,
        long? sourceElementId,
        string? exportId,
        string? category,
        string? restriction,
        string? name,
        string? sourceLabel,
        string fillColorHex,
        string strokeColorHex,
        bool isFloorDerived = false,
        string? floorTypeName = null,
        string? parsedZoneCandidate = null,
        bool isUnassignedFloor = false,
        FloorCategoryResolutionSource? categoryResolutionSource = null,
        bool hasWarning = false)
    {
        FeatureType = featureType;
        Feature = feature ?? throw new ArgumentNullException(nameof(feature));
        SourceElementId = sourceElementId;
        ExportId = exportId;
        Category = category;
        Restriction = restriction;
        Name = name;
        SourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? null : sourceLabel!.Trim();
        FillColorHex = fillColorHex ?? throw new ArgumentNullException(nameof(fillColorHex));
        StrokeColorHex = strokeColorHex ?? throw new ArgumentNullException(nameof(strokeColorHex));
        IsFloorDerived = isFloorDerived;
        FloorTypeName = string.IsNullOrWhiteSpace(floorTypeName) ? null : floorTypeName!.Trim();
        ParsedZoneCandidate = string.IsNullOrWhiteSpace(parsedZoneCandidate) ? null : parsedZoneCandidate!.Trim();
        IsUnassignedFloor = isUnassignedFloor;
        CategoryResolutionSource = categoryResolutionSource;
        HasWarning = hasWarning;
    }

    public ExportFeatureType FeatureType { get; }

    public IExportFeature Feature { get; }

    public long? SourceElementId { get; }

    public string? ExportId { get; }

    public string? Category { get; }

    public string? Restriction { get; }

    public string? Name { get; }

    public string? SourceLabel { get; }

    public string FillColorHex { get; }

    public string StrokeColorHex { get; }

    public bool IsFloorDerived { get; }

    public string? FloorTypeName { get; }

    public string? ParsedZoneCandidate { get; }

    public bool IsUnassignedFloor { get; }

    public FloorCategoryResolutionSource? CategoryResolutionSource { get; }

    public bool HasWarning { get; }

    public bool UsesFloorCategoryOverride => CategoryResolutionSource == FloorCategoryResolutionSource.Override;

    public bool SupportsFloorCategoryAssignment => IsFloorDerived && (IsUnassignedFloor || UsesFloorCategoryOverride);

    public string SearchText =>
        string.Join(
            " ",
            new[]
            {
                FeatureType.ToString(),
                Category,
                Restriction,
                Name,
                SourceLabel,
                FloorTypeName,
                ParsedZoneCandidate,
                ExportId,
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
