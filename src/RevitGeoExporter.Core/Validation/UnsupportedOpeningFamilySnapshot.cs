using System;

namespace RevitGeoExporter.Core.Validation;

public sealed class UnsupportedOpeningFamilySnapshot
{
    public UnsupportedOpeningFamilySnapshot(string familyName, long elementId)
    {
        FamilyName = string.IsNullOrWhiteSpace(familyName)
            ? "<unknown-family>"
            : familyName.Trim();
        ElementId = elementId;
    }

    public string FamilyName { get; }

    public long ElementId { get; }
}
