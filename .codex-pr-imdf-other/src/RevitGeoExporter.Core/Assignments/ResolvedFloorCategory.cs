using System;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Assignments;

public sealed class ResolvedFloorCategory
{
    public ResolvedFloorCategory(
        string floorTypeName,
        string? parsedZoneCandidate,
        ZoneInfo zoneInfo,
        FloorCategoryResolutionSource resolutionSource,
        bool isUnassigned)
    {
        FloorTypeName = floorTypeName ?? throw new ArgumentNullException(nameof(floorTypeName));
        ParsedZoneCandidate = parsedZoneCandidate;
        ZoneInfo = zoneInfo ?? throw new ArgumentNullException(nameof(zoneInfo));
        ResolutionSource = resolutionSource;
        IsUnassigned = isUnassigned;
    }

    public string FloorTypeName { get; }

    public string? ParsedZoneCandidate { get; }

    public ZoneInfo ZoneInfo { get; }

    public FloorCategoryResolutionSource ResolutionSource { get; }

    public bool IsUnassigned { get; }
}
