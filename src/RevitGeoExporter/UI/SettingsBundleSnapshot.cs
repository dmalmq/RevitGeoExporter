using System.Collections.Generic;
using RevitGeoExporter.Core.Assignments;

namespace RevitGeoExporter.UI;

public sealed class SettingsBundleSnapshot
{
    public ExportDialogSettings GlobalSettings { get; set; } = new();

    public IReadOnlyList<ExportProfile> Profiles { get; set; } = new List<ExportProfile>();

    public ProjectMappingRules ProjectMappings { get; set; } = ProjectMappingRules.Empty;

    public IReadOnlyList<SettingsStatusEntry> StatusEntries { get; set; } = new List<SettingsStatusEntry>();
}
