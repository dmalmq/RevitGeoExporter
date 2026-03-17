using System;

namespace RevitGeoExporter.Core.Models;

public static class UnitExportSettingsResolver
{
    public static UnitGeometrySource ResolveGeometrySource(UnitSource legacyUnitSource, UnitGeometrySource geometrySource)
    {
        if (geometrySource != UnitGeometrySource.Unset)
        {
            return geometrySource;
        }

        return legacyUnitSource == UnitSource.Rooms
            ? UnitGeometrySource.Rooms
            : UnitGeometrySource.Floors;
    }

    public static UnitAttributeSource ResolveAttributeSource(
        UnitSource legacyUnitSource,
        UnitGeometrySource geometrySource,
        UnitAttributeSource attributeSource)
    {
        if (attributeSource != UnitAttributeSource.Unset)
        {
            return attributeSource;
        }

        return legacyUnitSource == UnitSource.Rooms
            ? UnitAttributeSource.Rooms
            : geometrySource == UnitGeometrySource.Rooms
                ? UnitAttributeSource.Rooms
                : UnitAttributeSource.Hybrid;
    }

    public static UnitSource ToLegacy(UnitGeometrySource geometrySource, UnitAttributeSource attributeSource)
    {
        return geometrySource == UnitGeometrySource.Rooms && attributeSource == UnitAttributeSource.Rooms
            ? UnitSource.Rooms
            : UnitSource.Floors;
    }

    public static bool UsesRoomCategoryAssignments(UnitAttributeSource attributeSource)
    {
        return attributeSource == UnitAttributeSource.Rooms || attributeSource == UnitAttributeSource.Hybrid;
    }

    public static string GetAttributeSourceLabel(UnitAttributeSource attributeSource)
    {
        return attributeSource switch
        {
            UnitAttributeSource.Floors => "floor",
            UnitAttributeSource.Rooms => "room",
            UnitAttributeSource.Hybrid => "hybrid",
            _ => "floor",
        };
    }
}
