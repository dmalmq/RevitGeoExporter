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
        bool isUnassigned = false,
        string? assignmentMappingKey = null,
        string? assignmentSourceKind = null,
        string? assignmentParameterName = null,
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
        IsUnassigned = isUnassigned;
        AssignmentMappingKey = Normalize(assignmentMappingKey);
        AssignmentSourceKind = Normalize(assignmentSourceKind);
        AssignmentParameterName = Normalize(assignmentParameterName);
        IsSnappedToOutline = isSnappedToOutline;
    }

    public string FeatureType { get; }

    public string? ExportId { get; }

    public string? Category { get; }

    public long? SourceElementId { get; }

    public bool HasGeometry { get; }

    public bool GeometryValid { get; }

    public bool IsUnassigned { get; }

    public string? AssignmentMappingKey { get; }

    public string? AssignmentSourceKind { get; }

    public string? AssignmentParameterName { get; }

    public bool IsSnappedToOutline { get; }

    private static string? Normalize(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }
}
