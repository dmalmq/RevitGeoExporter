using System.Collections.Generic;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportChangeSummary
{
    public ExportBaselineStatus BaselineStatus { get; set; } = ExportBaselineStatus.Unavailable;

    public int ChangedViewCount { get; set; }

    public int ReusedViewCount { get; set; }

    public int WrittenArtifactCount { get; set; }

    public int ReusedArtifactCount { get; set; }

    public List<string> Lines { get; set; } = new();

    public bool HasChanges => ChangedViewCount > 0 || WrittenArtifactCount > 0 || Lines.Count > 0;
}

public enum ExportBaselineStatus
{
    Unavailable = 0,
    Loaded = 1,
    ConfigurationChanged = 2,
}
