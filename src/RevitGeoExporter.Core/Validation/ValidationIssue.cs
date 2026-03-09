using System;

namespace RevitGeoExporter.Core.Validation;

public sealed class ValidationIssue
{
    public ValidationIssue(
        ValidationSeverity severity,
        ValidationCode code,
        string message,
        string? viewName = null,
        string? levelName = null,
        string? featureType = null,
        string? category = null,
        long? sourceElementId = null)
    {
        Severity = severity;
        Code = code;
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("A message is required.", nameof(message))
            : message.Trim();
        ViewName = Normalize(viewName);
        LevelName = Normalize(levelName);
        FeatureType = Normalize(featureType);
        Category = Normalize(category);
        SourceElementId = sourceElementId;
    }

    public ValidationSeverity Severity { get; }

    public ValidationCode Code { get; }

    public string Message { get; }

    public string? ViewName { get; }

    public string? LevelName { get; }

    public string? FeatureType { get; }

    public string? Category { get; }

    public long? SourceElementId { get; }

    private static string? Normalize(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }
}
