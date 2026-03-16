using System;

namespace RevitGeoExporter.Export;

public sealed class ExportProgressUpdate
{
    public ExportProgressUpdate(int completedSteps, int totalSteps, string statusText)
    {
        CompletedSteps = Math.Max(0, completedSteps);
        TotalSteps = Math.Max(1, totalSteps);
        StatusText = statusText ?? string.Empty;
    }

    public int CompletedSteps { get; }

    public int TotalSteps { get; }

    public string StatusText { get; }
}
