namespace RevitGeoExporter.Export;

public enum PackagingMode
{
    PerViewPerFeatureFiles = 0,
    PerViewGeoPackage = 1,
    PerLevelGeoPackage = 2,
    PerBuildingGeoPackage = 3,
}
