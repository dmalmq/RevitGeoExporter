namespace RevitGeoExporter.Core.Schema;

public enum SchemaDerivedValue
{
    ExportId = 0,
    FeatureType = 1,
    Category = 2,
    Restrict = 3,
    Name = 4,
    AltName = 5,
    LevelId = 6,
    LevelName = 7,
    ViewName = 8,
    SourceElementId = 9,
    ElementId = 10,
    SourceLabel = 11,
    DisplayPoint = 12,
    Ordinal = 13,
    ElevationMeters = 14,
}

public enum SchemaSourceMetadataValue
{
    SourceDocumentKey = 0,
    SourceDocumentName = 1,
    IsLinkedSource = 2,
    SourceLinkInstanceId = 3,
    SourceLinkInstanceName = 4,
    HasPersistedExportId = 5,
}
