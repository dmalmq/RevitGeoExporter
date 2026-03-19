using System;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportLinkedModelInfo
{
    public long LinkInstanceId { get; set; }

    public string LinkInstanceName { get; set; } = string.Empty;

    public string SourceDocumentKey { get; set; } = string.Empty;

    public string SourceDocumentName { get; set; } = string.Empty;

    public static ExportLinkedModelInfo Create(
        long linkInstanceId,
        string? linkInstanceName,
        string? sourceDocumentKey,
        string? sourceDocumentName)
    {
        return new ExportLinkedModelInfo
        {
            LinkInstanceId = linkInstanceId,
            LinkInstanceName = Normalize(linkInstanceName),
            SourceDocumentKey = Normalize(sourceDocumentKey),
            SourceDocumentName = Normalize(sourceDocumentName),
        };
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
