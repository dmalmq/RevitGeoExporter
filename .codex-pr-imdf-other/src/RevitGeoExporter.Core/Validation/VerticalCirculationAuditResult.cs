namespace RevitGeoExporter.Core.Validation;

public sealed class VerticalCirculationAuditResult
{
    public VerticalCirculationAuditResult(
        string category,
        string featureType,
        int sourceCount,
        int outputCount)
    {
        Category = category;
        FeatureType = featureType;
        SourceCount = sourceCount;
        OutputCount = outputCount;
    }

    public string Category { get; }

    public string FeatureType { get; }

    public int SourceCount { get; }

    public int OutputCount { get; }

    public int MissingCount => SourceCount > OutputCount ? SourceCount - OutputCount : 0;

    public bool HasMissingOutput => MissingCount > 0;
}
