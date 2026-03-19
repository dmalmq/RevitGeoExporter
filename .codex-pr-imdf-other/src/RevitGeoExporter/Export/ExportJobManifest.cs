using System.Collections.Generic;

namespace RevitGeoExporter.Export;

public sealed class ExportJobManifest
{
    public List<ExportJobManifestItem> Jobs { get; set; } = new();
}

public sealed class ExportJobManifestItem
{
    public string ProfileName { get; set; } = string.Empty;

    public string? OutputDirectoryOverride { get; set; }
}
