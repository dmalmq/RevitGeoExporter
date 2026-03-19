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
        long? sourceElementId = null,
        long? owningViewId = null,
        string? sourceDocumentKey = null,
        ValidationActionKind actionKind = ValidationActionKind.None,
        string? recommendedAction = null,
        bool? canNavigateInRevit = null)
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
        OwningViewId = owningViewId;
        SourceDocumentKey = Normalize(sourceDocumentKey);
        ActionKind = actionKind;
        RecommendedAction = Normalize(recommendedAction);
        CanNavigateInRevit = canNavigateInRevit ?? (sourceElementId.HasValue || owningViewId.HasValue);
    }

    public ValidationSeverity Severity { get; }

    public ValidationCode Code { get; }

    public string Message { get; }

    public string? ViewName { get; }

    public string? LevelName { get; }

    public string? FeatureType { get; }

    public string? Category { get; }

    public long? SourceElementId { get; }

    public long? OwningViewId { get; }

    public string? SourceDocumentKey { get; }

    public ValidationActionKind ActionKind { get; }

    public string? RecommendedAction { get; }

    public bool CanNavigateInRevit { get; }

    public ValidationIssue WithSeverity(ValidationSeverity severity)
    {
        return new ValidationIssue(
            severity,
            Code,
            Message,
            ViewName,
            LevelName,
            FeatureType,
            Category,
            SourceElementId,
            OwningViewId,
            SourceDocumentKey,
            ActionKind,
            RecommendedAction,
            CanNavigateInRevit);
    }

    private static string? Normalize(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }
}
