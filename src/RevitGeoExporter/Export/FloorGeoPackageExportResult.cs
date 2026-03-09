using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Diagnostics;

namespace RevitGeoExporter.Export;

public sealed class FloorGeoPackageExportResult
{
    private readonly List<ViewExportResult> _viewResults = new();
    private readonly List<string> _warnings = new();

    public IReadOnlyList<ViewExportResult> ViewResults => _viewResults;

    public IReadOnlyList<string> Warnings => _warnings;

    public string? DiagnosticsReportPath { get; private set; }

    public string? PackageDirectoryPath { get; private set; }

    public string? PackageManifestPath { get; private set; }

    public ExportChangeSummary? ChangeSummary { get; private set; }

    public void AddViewResult(ViewExportResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        _viewResults.Add(result);
    }

    public void AddWarnings(IEnumerable<string> warnings)
    {
        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        _warnings.AddRange(warnings);
    }

    public void SetDiagnosticsReportPath(string? diagnosticsReportPath)
    {
        DiagnosticsReportPath = string.IsNullOrWhiteSpace(diagnosticsReportPath)
            ? null
            : diagnosticsReportPath!.Trim();
    }

    public void SetPackagePaths(string? packageDirectoryPath, string? packageManifestPath)
    {
        PackageDirectoryPath = string.IsNullOrWhiteSpace(packageDirectoryPath) ? null : packageDirectoryPath!.Trim();
        PackageManifestPath = string.IsNullOrWhiteSpace(packageManifestPath) ? null : packageManifestPath!.Trim();
    }

    public void SetChangeSummary(ExportChangeSummary? changeSummary)
    {
        ChangeSummary = changeSummary;
    }
}

public sealed class ViewExportResult
{
    public ViewExportResult(
        string viewName,
        string levelName,
        string featureType,
        string outputFilePath,
        int featureCount)
    {
        ViewName = viewName ?? throw new ArgumentNullException(nameof(viewName));
        LevelName = levelName ?? throw new ArgumentNullException(nameof(levelName));
        FeatureType = featureType ?? throw new ArgumentNullException(nameof(featureType));
        OutputFilePath = outputFilePath ?? throw new ArgumentNullException(nameof(outputFilePath));
        FeatureCount = featureCount;
    }

    public string ViewName { get; }

    public string LevelName { get; }

    public string FeatureType { get; }

    public string OutputFilePath { get; }

    public int FeatureCount { get; }
}
