using System;

namespace RevitGeoExporter.Export;

public sealed class ExportElementMetadata
{
    public ExportElementMetadata(
        string exportId,
        string sourceDocumentKey,
        string sourceDocumentName,
        bool hasPersistedId)
    {
        ExportId = string.IsNullOrWhiteSpace(exportId)
            ? throw new ArgumentException("An export ID is required.", nameof(exportId))
            : exportId.Trim();
        SourceDocumentKey = string.IsNullOrWhiteSpace(sourceDocumentKey)
            ? throw new ArgumentException("A source document key is required.", nameof(sourceDocumentKey))
            : sourceDocumentKey.Trim();
        SourceDocumentName = string.IsNullOrWhiteSpace(sourceDocumentName)
            ? throw new ArgumentException("A source document name is required.", nameof(sourceDocumentName))
            : sourceDocumentName.Trim();
        HasPersistedId = hasPersistedId;
    }

    public string ExportId { get; }

    public string SourceDocumentKey { get; }

    public string SourceDocumentName { get; }

    public bool HasPersistedId { get; }
}

public sealed class ExportLevelMetadata
{
    public ExportLevelMetadata(string exportId, bool hasPersistedId)
    {
        ExportId = string.IsNullOrWhiteSpace(exportId)
            ? throw new ArgumentException("An export ID is required.", nameof(exportId))
            : exportId.Trim();
        HasPersistedId = hasPersistedId;
    }

    public string ExportId { get; }

    public bool HasPersistedId { get; }
}
