using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Core.Models;

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
        IReadOnlyList<string> availableSourceLabels,
        Bounds2D bounds,
        UnitSource unitSource,
        string roomCategoryParameterName)
    {
        ViewId = viewId;
        ViewName = viewName ?? throw new ArgumentNullException(nameof(viewName));
        LevelName = levelName ?? throw new ArgumentNullException(nameof(levelName));
        Features = features ?? throw new ArgumentNullException(nameof(features));
        UnassignedFloors = unassignedFloors ?? throw new ArgumentNullException(nameof(unassignedFloors));
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        AvailableSourceLabels = availableSourceLabels ?? throw new ArgumentNullException(nameof(availableSourceLabels));
        Bounds = bounds;
        UnitSource = unitSource;
        RoomCategoryParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName) ? "Name" : roomCategoryParameterName.Trim();
    }

    public long ViewId { get; }

    public string ViewName { get; }

    public string LevelName { get; }

    public IReadOnlyList<PreviewFeatureData> Features { get; }

    public IReadOnlyList<PreviewUnassignedFloorGroup> UnassignedFloors { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyList<string> AvailableSourceLabels { get; }

    public Bounds2D Bounds { get; }

    public UnitSource UnitSource { get; }

    public string RoomCategoryParameterName { get; }
}
