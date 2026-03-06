using System;

namespace RevitGeoExporter.Export;

public sealed class PreviewUnassignedFloorGroup
{
    public PreviewUnassignedFloorGroup(
        string floorTypeName,
        string? parsedZoneCandidate,
        int unitCount)
    {
        if (string.IsNullOrWhiteSpace(floorTypeName))
        {
            throw new ArgumentException("Floor type name is required.", nameof(floorTypeName));
        }

        if (unitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCount));
        }

        FloorTypeName = floorTypeName.Trim();
        ParsedZoneCandidate = string.IsNullOrWhiteSpace(parsedZoneCandidate) ? null : parsedZoneCandidate!.Trim();
        UnitCount = unitCount;
    }

    public string FloorTypeName { get; }

    public string? ParsedZoneCandidate { get; }

    public int UnitCount { get; }
}
