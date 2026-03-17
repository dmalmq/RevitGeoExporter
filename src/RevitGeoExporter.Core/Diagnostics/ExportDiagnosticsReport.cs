using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsReport
{
    public string SourceModelName { get; set; } = string.Empty;

    public string SourceDocumentKey { get; set; } = string.Empty;

    public int TargetEpsg { get; set; }

    public int? SourceEpsg { get; set; }

    public string? SourceCoordinateSystemId { get; set; }

    public string? SourceCoordinateSystemDefinition { get; set; }

    public string? ProfileName { get; set; }

    public string SchemaProfileName { get; set; } = string.Empty;

    public string ValidationPolicyProfileName { get; set; } = string.Empty;

    public string OperatorName { get; set; } = string.Empty;

    public string CoordinateMode { get; set; } = string.Empty;

    public string PackagingMode { get; set; } = string.Empty;

    public DateTimeOffset ExportedAtUtc { get; set; }

    public long DurationMilliseconds { get; set; }

    public List<ExportDiagnosticsViewReport> Views { get; set; } = new();

    public List<ValidationIssue> ValidationIssues { get; set; } = new();

    public List<string> ExportWarnings { get; set; } = new();

    public List<ExportLinkedModelInfo> IncludedLinks { get; set; } = new();

    public List<ExportDiagnosticsOutputFile> OutputFiles { get; set; } = new();

    public PackageValidationResult? PackageValidationResult { get; set; }
}
