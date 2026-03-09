using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsReport
{
    public string SourceModelName { get; set; } = string.Empty;

    public int TargetEpsg { get; set; }

    public string? ProfileName { get; set; }

    public DateTimeOffset ExportedAtUtc { get; set; }

    public long DurationMilliseconds { get; set; }

    public List<ExportDiagnosticsViewReport> Views { get; set; } = new();

    public List<ValidationIssue> ValidationIssues { get; set; } = new();

    public List<string> ExportWarnings { get; set; } = new();

    public List<ExportDiagnosticsOutputFile> OutputFiles { get; set; } = new();
}
