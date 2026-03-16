namespace RevitGeoExporter.Core.Validation;

public enum ValidationActionKind
{
    None = 0,
    ReviewExportSettings = 1,
    ResolveMappings = 2,
    RegenerateStableIds = 3,
    ReviewGeometry = 4,
    ReviewOpeningFamilies = 5,
    ReviewVerticalCirculation = 6,
    ReviewElementInRevit = 7,
}
