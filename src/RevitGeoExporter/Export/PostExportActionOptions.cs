namespace RevitGeoExporter.Export;

public sealed class PostExportActionOptions
{
    public bool OpenOutputFolder { get; set; }

    public bool LaunchQgis { get; set; }

    public PostExportActionOptions Clone()
    {
        return new PostExportActionOptions
        {
            OpenOutputFolder = OpenOutputFolder,
            LaunchQgis = LaunchQgis,
        };
    }
}
