using System;

namespace RevitGeoExporter.Core.Validation;

public sealed class UnsupportedOpeningFamilySnapshot
{
    public UnsupportedOpeningFamilySnapshot(
        string familyName,
        long elementId,
        string? sourceDocumentKey = null,
        string? sourceDocumentName = null,
        bool canNavigateInRevit = true)
    {
        FamilyName = string.IsNullOrWhiteSpace(familyName)
            ? "<unknown-family>"
            : familyName.Trim();
        ElementId = elementId;
        SourceDocumentKey = string.IsNullOrWhiteSpace(sourceDocumentKey) ? null : sourceDocumentKey.Trim();
        SourceDocumentName = string.IsNullOrWhiteSpace(sourceDocumentName) ? null : sourceDocumentName.Trim();
        CanNavigateInRevit = canNavigateInRevit;
    }

    public string FamilyName { get; }

    public long ElementId { get; }

    public string? SourceDocumentKey { get; }

    public string? SourceDocumentName { get; }

    public bool CanNavigateInRevit { get; }
}
