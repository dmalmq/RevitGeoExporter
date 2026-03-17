namespace RevitGeoExporter.Core.Validation;

public enum ValidationCode
{
    InvalidTargetEpsg = 0,
    EmptyViewOutput = 1,
    UnassignedFloorCategory = 2,
    MissingStableId = 3,
    DuplicateStableId = 4,
    EmptyGeometry = 5,
    InvalidGeometry = 6,
    UnsupportedOpeningFamily = 7,
    UnsnappedOpening = 8,
    MissingVerticalCirculation = 9,
    NonStandardUnitCategory = 10,
    LinkedElementUsingFallbackId = 11,
    MissingSchemaMappedParameter = 12,
    SchemaTypeConversionFailed = 13,
    DuplicateSchemaField = 14,
    MissingName = 15,
}
