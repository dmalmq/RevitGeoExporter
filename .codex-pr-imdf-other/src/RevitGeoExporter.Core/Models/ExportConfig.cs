namespace RevitGeoExporter.Core.Models;

public sealed class ExportConfig
{
    public int TargetEpsg { get; set; } = 6677;

    public string OutputDirectory { get; set; } = string.Empty;

    public string ZonePrefix { get; set; } = ZoneNameParser.DefaultPrefix;

    public string ZoneSuffix { get; set; } = ZoneNameParser.DefaultSuffix;
}
