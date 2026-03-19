using System;

namespace RevitGeoExporter.UI;

public sealed class LinkSelectionItem
{
    public LinkSelectionItem(long linkInstanceId, string displayName, string? sourceDocumentName = null)
    {
        LinkInstanceId = linkInstanceId;
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? $"Link {linkInstanceId}"
            : displayName.Trim();
        SourceDocumentName = string.IsNullOrWhiteSpace(sourceDocumentName) ? null : sourceDocumentName.Trim();
    }

    public long LinkInstanceId { get; }

    public string DisplayName { get; }

    public string? SourceDocumentName { get; }

    public override string ToString() => DisplayName;
}
