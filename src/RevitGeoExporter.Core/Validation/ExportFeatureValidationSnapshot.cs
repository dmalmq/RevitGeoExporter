using System;

namespace RevitGeoExporter.Core.Validation;

public sealed class ExportFeatureValidationSnapshot
{
    public ExportFeatureValidationSnapshot(
        string featureType,
        string? exportId,
        string? category,
        long? sourceElementId,
        bool hasGeometry,
        bool geometryValid,
        bool isUnassignedFloor = false,
        string? floorTypeName = null,
        bool isSnappedToOutline = true)
    {
        FeatureType = string.IsNullOrWhiteSpace(featureType)
            ? throw new ArgumentException("A feature type is required.", nameof(featureType))
            : featureType.Trim();
        ExportId = Normalize(exportId);
        Category = Normalize(category);
        SourceElementId = sourceElementId;
        HasGeometry = hasGeometry;
        GeometryValid = geometryValid;
        IsUnassignedFloor = isUnassignedFloor;
        FloorTypeName = Normalize(floorTypeName);
        IsSnappedToOutline = isSnappedToOutline;
    }

    public string FeatureType { get; }

    public string? ExportId { get; }

    public string? Category { get; }

    public long? SourceElementId { get; }

    public bool HasGeometry { get; }

    public bool GeometryValid { get; }

    public bool IsUnassignedFloor { get; }

    public string? FloorTypeName { get; }

    public bool IsSnappedToOutline { get; }

    private static string? Normalize(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }
}
