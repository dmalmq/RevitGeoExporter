using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class PackageValidationResult
{
    public List<PackageValidationIssue> Issues { get; set; } = new();

    public bool HasErrors => Issues.Any(issue => issue.Severity == PackageValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == PackageValidationSeverity.Warning);
}

public sealed class PackageValidationIssue
{
    public PackageValidationSeverity Severity { get; set; } = PackageValidationSeverity.Error;

    public string ArtifactKey { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string? LayerName { get; set; }

    public string Message { get; set; } = string.Empty;
}

public enum PackageValidationSeverity
{
    Warning = 0,
    Error = 1,
}
