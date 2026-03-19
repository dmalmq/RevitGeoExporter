namespace RevitGeoExporter.Export;

public sealed class ExportPackageOptions
{
    public bool Enabled { get; set; }

    public bool IncludeLegendFile { get; set; } = true;

    public PackagingMode PackagingMode { get; set; } = PackagingMode.PerViewPerFeatureFiles;

    public bool ValidateAfterWrite { get; set; } = true;

    public bool GenerateQgisArtifacts { get; set; }

    public PostExportActionOptions PostExportActions { get; set; } = new();
}
