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
        string? assignmentSourceKind = null,
        string? assignmentMappingKey = null,
        string? assignmentParsedCandidate = null,
        string? assignmentParameterName = null,
        bool isUnassigned = false,
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
        SourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? null : sourceLabel.Trim();
        FillColorHex = fillColorHex ?? throw new ArgumentNullException(nameof(fillColorHex));
        StrokeColorHex = strokeColorHex ?? throw new ArgumentNullException(nameof(strokeColorHex));
        AssignmentSourceKind = string.IsNullOrWhiteSpace(assignmentSourceKind) ? null : assignmentSourceKind.Trim();
        AssignmentMappingKey = string.IsNullOrWhiteSpace(assignmentMappingKey) ? null : assignmentMappingKey.Trim();
        AssignmentParsedCandidate = string.IsNullOrWhiteSpace(assignmentParsedCandidate) ? null : assignmentParsedCandidate.Trim();
        AssignmentParameterName = string.IsNullOrWhiteSpace(assignmentParameterName) ? null : assignmentParameterName.Trim();
        IsUnassigned = isUnassigned;
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

    public string? AssignmentSourceKind { get; }

    public string? AssignmentMappingKey { get; }

    public string? AssignmentParsedCandidate { get; }

    public string? AssignmentParameterName { get; }

    public bool IsUnassigned { get; }

    public FloorCategoryResolutionSource? CategoryResolutionSource { get; }

    public bool HasWarning { get; }

    public bool UsesCategoryOverride => CategoryResolutionSource == FloorCategoryResolutionSource.Override;

    public bool SupportsCategoryAssignment =>
        FeatureType == ExportFeatureType.Unit &&
        !string.IsNullOrWhiteSpace(AssignmentMappingKey) &&
        (IsUnassigned || UsesCategoryOverride);

    public bool IsFloorDerived => string.Equals(AssignmentSourceKind, "floor", StringComparison.OrdinalIgnoreCase);

    public bool IsRoomDerived => string.Equals(AssignmentSourceKind, "room", StringComparison.OrdinalIgnoreCase);

    public string? FloorTypeName => AssignmentMappingKey;

    public string? ParsedZoneCandidate => AssignmentParsedCandidate;

    public bool IsUnassignedFloor => IsUnassigned;

    public bool UsesFloorCategoryOverride => UsesCategoryOverride;

    public bool SupportsFloorCategoryAssignment => SupportsCategoryAssignment;

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
                AssignmentMappingKey,
                AssignmentParsedCandidate,
                AssignmentParameterName,
                ExportId,
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
