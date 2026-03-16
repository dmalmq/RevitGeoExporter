using System;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Assignments;

public sealed class ResolvedMappingCategory
{
    public ResolvedMappingCategory(
        string sourceKind,
        string mappingKey,
        string? parsedCandidate,
        string? parameterName,
        ZoneInfo zoneInfo,
        FloorCategoryResolutionSource resolutionSource,
        bool isUnassigned)
    {
        SourceKind = string.IsNullOrWhiteSpace(sourceKind) ? "unknown" : sourceKind.Trim();
        MappingKey = mappingKey ?? throw new ArgumentNullException(nameof(mappingKey));
        ParsedCandidate = parsedCandidate;
        ParameterName = string.IsNullOrWhiteSpace(parameterName) ? null : parameterName.Trim();
        ZoneInfo = zoneInfo ?? throw new ArgumentNullException(nameof(zoneInfo));
        ResolutionSource = resolutionSource;
        IsUnassigned = isUnassigned;
    }

    public string SourceKind { get; }

    public string MappingKey { get; }

    public string? ParsedCandidate { get; }

    public string? ParameterName { get; }

    public ZoneInfo ZoneInfo { get; }

    public FloorCategoryResolutionSource ResolutionSource { get; }

    public bool IsUnassigned { get; }
}
