using System;

namespace RevitGeoExporter.Core.Validation;

public sealed class ValidationMappingSuggestion
{
    public ValidationMappingSuggestion(
        string sourceKind,
        string mappingKey,
        int occurrenceCount,
        string? parameterName = null,
        string? parsedCandidate = null,
        string? suggestedCategory = null)
    {
        SourceKind = string.IsNullOrWhiteSpace(sourceKind)
            ? throw new ArgumentException("A source kind is required.", nameof(sourceKind))
            : sourceKind.Trim();
        MappingKey = string.IsNullOrWhiteSpace(mappingKey)
            ? throw new ArgumentException("A mapping key is required.", nameof(mappingKey))
            : mappingKey.Trim();
        OccurrenceCount = Math.Max(0, occurrenceCount);
        ParameterName = Normalize(parameterName);
        ParsedCandidate = Normalize(parsedCandidate);
        SuggestedCategory = Normalize(suggestedCategory);
    }

    public string SourceKind { get; }

    public string MappingKey { get; }

    public int OccurrenceCount { get; }

    public string? ParameterName { get; }

    public string? ParsedCandidate { get; }

    public string? SuggestedCategory { get; }

    private static string? Normalize(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }
}
