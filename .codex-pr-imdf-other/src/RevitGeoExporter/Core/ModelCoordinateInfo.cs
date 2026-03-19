using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core;

public sealed class ModelCoordinateInfo
{
    public string DisplayLengthUnitLabel { get; init; } = "Unknown";

    public string ActiveProjectLocationName { get; init; } = string.Empty;

    public string SharedCoordinateSummary { get; init; } = string.Empty;

    public string SiteCoordinateSystemId { get; init; } = string.Empty;

    public string SiteCoordinateSystemDefinition { get; init; } = string.Empty;

    public int? ResolvedSourceEpsg { get; init; }

    public string ResolvedSourceLabel { get; init; } = string.Empty;

    public Point2D? SurveyPointSharedCoordinates { get; init; }

    public bool CanConvert => ResolvedSourceEpsg.HasValue || SiteCoordinateSystemDefinition.Length > 0;
}