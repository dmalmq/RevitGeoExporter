using System;

namespace RevitGeoExporter.Export;

public sealed class LinkedModelSummary
{
    public LinkedModelSummary(long linkInstanceId, string linkInstanceName, string sourceDocumentKey, string sourceDocumentName)
    {
        LinkInstanceId = linkInstanceId;
        LinkInstanceName = string.IsNullOrWhiteSpace(linkInstanceName)
            ? $"Link {linkInstanceId}"
            : linkInstanceName.Trim();
        SourceDocumentKey = string.IsNullOrWhiteSpace(sourceDocumentKey)
            ? throw new ArgumentException("A source document key is required.", nameof(sourceDocumentKey))
            : sourceDocumentKey.Trim();
        SourceDocumentName = string.IsNullOrWhiteSpace(sourceDocumentName)
            ? throw new ArgumentException("A source document name is required.", nameof(sourceDocumentName))
            : sourceDocumentName.Trim();
    }

    public long LinkInstanceId { get; }

    public string LinkInstanceName { get; }

    public string SourceDocumentKey { get; }

    public string SourceDocumentName { get; }
}
