using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Preview;

namespace RevitGeoExporter.Export;

public sealed class PreviewViewData
{
    public PreviewViewData(
        long viewId,
        string viewName,
        string levelName,
        IReadOnlyList<PreviewFeatureData> features,
        IReadOnlyList<PreviewUnassignedFloorGroup> unassignedFloors,
        IReadOnlyList<string> warnings,
        Bounds2D bounds)
    {
        ViewId = viewId;
        ViewName = viewName ?? throw new ArgumentNullException(nameof(viewName));
        LevelName = levelName ?? throw new ArgumentNullException(nameof(levelName));
        Features = features ?? throw new ArgumentNullException(nameof(features));
        UnassignedFloors = unassignedFloors ?? throw new ArgumentNullException(nameof(unassignedFloors));
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        Bounds = bounds;
    }

    public long ViewId { get; }

    public string ViewName { get; }

    public string LevelName { get; }

    public IReadOnlyList<PreviewFeatureData> Features { get; }

    public IReadOnlyList<PreviewUnassignedFloorGroup> UnassignedFloors { get; }

    public IReadOnlyList<string> Warnings { get; }

    public Bounds2D Bounds { get; }
}
