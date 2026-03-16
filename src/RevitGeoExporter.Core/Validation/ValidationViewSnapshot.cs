using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Validation;

public sealed class ValidationViewSnapshot
{
    public ValidationViewSnapshot(
        long viewId,
        string viewName,
        string levelName,
        IReadOnlyList<ExportFeatureValidationSnapshot> features,
        IReadOnlyList<UnsupportedOpeningFamilySnapshot>? unsupportedOpenings = null,
        int sourceStairsCount = 0,
        int sourceEscalatorCount = 0,
        int sourceElevatorCount = 0)
    {
        ViewId = viewId;
        ViewName = string.IsNullOrWhiteSpace(viewName)
            ? throw new ArgumentException("A view name is required.", nameof(viewName))
            : viewName.Trim();
        LevelName = string.IsNullOrWhiteSpace(levelName)
            ? throw new ArgumentException("A level name is required.", nameof(levelName))
            : levelName.Trim();
        Features = features ?? throw new ArgumentNullException(nameof(features));
        UnsupportedOpenings = unsupportedOpenings ?? Array.Empty<UnsupportedOpeningFamilySnapshot>();
        SourceStairsCount = sourceStairsCount;
        SourceEscalatorCount = sourceEscalatorCount;
        SourceElevatorCount = sourceElevatorCount;
    }

    public long ViewId { get; }

    public string ViewName { get; }

    public string LevelName { get; }

    public IReadOnlyList<ExportFeatureValidationSnapshot> Features { get; }

    public IReadOnlyList<UnsupportedOpeningFamilySnapshot> UnsupportedOpenings { get; }

    public int SourceStairsCount { get; }

    public int SourceEscalatorCount { get; }

    public int SourceElevatorCount { get; }
}
