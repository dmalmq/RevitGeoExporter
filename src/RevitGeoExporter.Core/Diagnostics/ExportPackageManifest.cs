using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportPackageManifest
{
    public string SourceModelName { get; set; } = string.Empty;

    public string PackageDirectory { get; set; } = string.Empty;

    public int TargetEpsg { get; set; }

    public DateTimeOffset ExportedAtUtc { get; set; }

    public List<ExportLinkedModelInfo> IncludedLinks { get; set; } = new();

    public List<ExportPackageManifestFile> Files { get; set; } = new();
}

public sealed class ExportPackageManifestFile
{
    public string RelativePath { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;
}
