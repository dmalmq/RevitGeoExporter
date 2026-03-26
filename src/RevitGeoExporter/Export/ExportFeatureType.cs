using System;

namespace RevitGeoExporter.Export;

[Flags]
public enum ExportFeatureType
{
    None = 0,
    Unit = 1 << 0,
    Detail = 1 << 1,
    Opening = 1 << 2,
    Level = 1 << 3,
    Fixture = 1 << 4,
    All = Unit | Detail | Opening | Level | Fixture,
}
