using System;

namespace RevitGeoExporter.Export;

public sealed class PreviewUnassignedFloorGroup
{
    public PreviewUnassignedFloorGroup(
        string mappingKey,
        string? parsedCandidate,
        int unitCount,
        string sourceKind,
        string? parameterName)
    {
        if (string.IsNullOrWhiteSpace(mappingKey))
        {
            throw new ArgumentException("A mapping key is required.", nameof(mappingKey));
        }

        if (unitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCount));
        }

        MappingKey = mappingKey.Trim();
        ParsedCandidate = string.IsNullOrWhiteSpace(parsedCandidate) ? null : parsedCandidate.Trim();
        UnitCount = unitCount;
        SourceKind = string.IsNullOrWhiteSpace(sourceKind) ? "floor" : sourceKind.Trim();
        ParameterName = string.IsNullOrWhiteSpace(parameterName) ? null : parameterName.Trim();
    }

    public string MappingKey { get; }

    public string? ParsedCandidate { get; }

    public int UnitCount { get; }

    public string SourceKind { get; }

    public string? ParameterName { get; }

    public string FloorTypeName => MappingKey;

    public string? ParsedZoneCandidate => ParsedCandidate;
}
