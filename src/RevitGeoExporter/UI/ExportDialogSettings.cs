using System.Collections.Generic;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

public sealed class ExportDialogSettings
{
    public string OutputDirectory { get; set; } = string.Empty;

    public int TargetEpsg { get; set; } = ProjectInfo.DefaultTargetEpsg;

    public ExportFeatureType FeatureTypes { get; set; } = ExportFeatureType.All;

    public bool SplitUnitsByWalls { get; set; } = true;

    public List<long> SelectedViewIds { get; set; } = new();

    public UiLanguage UiLanguage { get; set; } = UiLanguage.English;
}
