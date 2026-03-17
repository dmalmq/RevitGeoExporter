namespace RevitGeoExporter.Core.Validation;

public enum ValidationPolicyTarget
{
    MissingNames = 0,
    UnmappedCategories = 1,
    DuplicateStableIds = 2,
    LinkedFallbackIds = 3,
    UnsupportedOpeningFamilies = 4,
    GeoreferenceWarnings = 5,
    UnsnappedOpenings = 6,
}
