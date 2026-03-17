using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ChangeSummaryService
{
    public ExportChangeSummary Compare(
        ExportBaselineSnapshot? previousSnapshot,
        ExportDiagnosticsReport? previousReport,
        ExportDiagnosticsReport currentReport,
        ExportPackageManifest? previousManifest,
        ExportPackageManifest currentManifest,
        int changedViewCount,
        int reusedViewCount,
        int writtenArtifactCount,
        int reusedArtifactCount,
        int missingBaselineArtifactCount,
        string? fullRewriteReason)
    {
        if (currentReport == null)
        {
            throw new ArgumentNullException(nameof(currentReport));
        }

        if (currentManifest == null)
        {
            throw new ArgumentNullException(nameof(currentManifest));
        }

        ExportChangeSummary summary = new()
        {
            ChangedViewCount = changedViewCount,
            ReusedViewCount = reusedViewCount,
            WrittenArtifactCount = writtenArtifactCount,
            ReusedArtifactCount = reusedArtifactCount,
            BaselineStatus = previousSnapshot == null
                ? ExportBaselineStatus.Unavailable
                : string.IsNullOrWhiteSpace(fullRewriteReason)
                    ? ExportBaselineStatus.Loaded
                    : ExportBaselineStatus.ConfigurationChanged,
        };

        if (!string.IsNullOrWhiteSpace(fullRewriteReason))
        {
            summary.Lines.Add($"Full rewrite: {fullRewriteReason}");
        }

        if (missingBaselineArtifactCount > 0)
        {
            summary.Lines.Add($"Missing baseline artifacts forced rewrites: {missingBaselineArtifactCount}");
        }

        if (previousReport == null || previousManifest == null)
        {
            summary.Lines.Add("No previous export baseline was found. This export is now the baseline.");
            return summary;
        }

        AppendViewChanges(summary.Lines, previousReport, currentReport);
        AppendLayerCountChanges(summary.Lines, previousReport, currentReport);
        AppendWarningChanges(summary.Lines, previousReport, currentReport);
        AppendOverrideChanges(summary.Lines, previousReport, currentReport);
        AppendFileChanges(summary.Lines, previousManifest, currentManifest);

        if (summary.Lines.Count == 0)
        {
            summary.Lines.Add("No meaningful changes were detected compared with the previous export baseline.");
        }

        return summary;
    }

    private static void AppendViewChanges(
        ICollection<string> lines,
        ExportDiagnosticsReport previousReport,
        ExportDiagnosticsReport currentReport)
    {
        HashSet<string> previousViews = previousReport.Views.Select(v => v.ViewName).ToHashSet(StringComparer.Ordinal);
        HashSet<string> currentViews = currentReport.Views.Select(v => v.ViewName).ToHashSet(StringComparer.Ordinal);

        foreach (string added in currentViews.Except(previousViews, StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"+ View added: {added}");
        }

        foreach (string removed in previousViews.Except(currentViews, StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- View removed: {removed}");
        }
    }

    private static void AppendLayerCountChanges(
        ICollection<string> lines,
        ExportDiagnosticsReport previousReport,
        ExportDiagnosticsReport currentReport)
    {
        Dictionary<string, int> previous = FlattenLayerCounts(previousReport);
        Dictionary<string, int> current = FlattenLayerCounts(currentReport);
        foreach (string key in previous.Keys.Union(current.Keys).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            previous.TryGetValue(key, out int previousCount);
            current.TryGetValue(key, out int currentCount);
            if (previousCount == currentCount)
            {
                continue;
            }

            lines.Add($"~ Layer count changed: {key} {previousCount} -> {currentCount}");
        }
    }

    private static void AppendWarningChanges(
        ICollection<string> lines,
        ExportDiagnosticsReport previousReport,
        ExportDiagnosticsReport currentReport)
    {
        int previousWarnings = previousReport.ValidationIssues.Count + previousReport.ExportWarnings.Count;
        int currentWarnings = currentReport.ValidationIssues.Count + currentReport.ExportWarnings.Count;
        if (previousWarnings != currentWarnings)
        {
            lines.Add($"~ Warning count changed: {previousWarnings} -> {currentWarnings}");
        }
    }

    private static void AppendOverrideChanges(
        ICollection<string> lines,
        ExportDiagnosticsReport previousReport,
        ExportDiagnosticsReport currentReport)
    {
        HashSet<string> previousOverrides = previousReport.Views
            .SelectMany(v => v.AppliedFloorOverrides)
            .Select(v => $"{v.FloorTypeName}|{v.Category}")
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> currentOverrides = currentReport.Views
            .SelectMany(v => v.AppliedFloorOverrides)
            .Select(v => $"{v.FloorTypeName}|{v.Category}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (string added in currentOverrides.Except(previousOverrides, StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"+ Floor override added: {added.Replace('|', ' ')}");
        }

        foreach (string removed in previousOverrides.Except(currentOverrides, StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- Floor override removed: {removed.Replace('|', ' ')}");
        }
    }

    private static void AppendFileChanges(
        ICollection<string> lines,
        ExportPackageManifest previousManifest,
        ExportPackageManifest currentManifest)
    {
        HashSet<string> previousFiles = previousManifest.Files.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);
        HashSet<string> currentFiles = currentManifest.Files.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (string added in currentFiles.Except(previousFiles, StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"+ Output file added: {added}");
        }

        foreach (string removed in previousFiles.Except(currentFiles, StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- Output file removed: {removed}");
        }
    }

    private static Dictionary<string, int> FlattenLayerCounts(ExportDiagnosticsReport report)
    {
        return report.Views
            .SelectMany(view => view.Layers.Select(layer => new
            {
                Key = $"{view.ViewName}|{layer.FeatureType}|{layer.Category ?? "<none>"}",
                layer.Count,
            }))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.Count), StringComparer.Ordinal);
    }
}
