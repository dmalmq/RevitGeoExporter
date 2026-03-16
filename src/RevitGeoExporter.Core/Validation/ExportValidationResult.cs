using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Validation;

public sealed class ExportValidationResult
{
    public ExportValidationResult(IReadOnlyList<ValidationIssue> issues)
    {
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == ValidationSeverity.Warning);
}
