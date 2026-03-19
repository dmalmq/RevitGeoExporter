using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportBaselineStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _rootDirectory;

    public ExportBaselineStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitGeoExporter", "export-baselines")
            : rootDirectory!.Trim();
    }

    public ExportBaselineLoadResult Load(string baselineKey)
    {
        string sanitized = SanitizeKey(baselineKey);
        string diagnosticsPath = Path.Combine(_rootDirectory, $"{sanitized}-diagnostics.json");
        string manifestPath = Path.Combine(_rootDirectory, $"{sanitized}-manifest.json");
        string snapshotPath = Path.Combine(_rootDirectory, $"{sanitized}-snapshot.json");
        LoadResult<ExportDiagnosticsReport> report = JsonFileLoadHelper.Load(
            diagnosticsPath,
            () => new ExportDiagnosticsReport(),
            json => JsonConvert.DeserializeObject<ExportDiagnosticsReport>(json),
            "Export baseline diagnostics");
        LoadResult<ExportPackageManifest> manifest = JsonFileLoadHelper.Load(
            manifestPath,
            () => new ExportPackageManifest(),
            json => JsonConvert.DeserializeObject<ExportPackageManifest>(json),
            "Export baseline manifest");
        LoadResult<ExportBaselineSnapshot> snapshot = JsonFileLoadHelper.Load(
            snapshotPath,
            () => new ExportBaselineSnapshot(),
            json => JsonConvert.DeserializeObject<ExportBaselineSnapshot>(json),
            "Export baseline snapshot");

        bool hasReport = File.Exists(diagnosticsPath) && report.Value.OutputFiles.Count > 0;
        bool hasManifest = File.Exists(manifestPath) && manifest.Value.Files.Count > 0;
        bool hasSnapshot = File.Exists(snapshotPath) && snapshot.Value.Views.Count > 0;
        return new ExportBaselineLoadResult
        {
            Report = hasReport ? report.Value : null,
            Manifest = hasManifest ? manifest.Value : null,
            Snapshot = hasSnapshot ? snapshot.Value : null,
            Warnings = report.Warnings.Concat(manifest.Warnings).Concat(snapshot.Warnings).ToList(),
        };
    }

    public void Save(string baselineKey, ExportDiagnosticsReport report, ExportPackageManifest manifest, ExportBaselineSnapshot snapshot)
    {
        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        Directory.CreateDirectory(_rootDirectory);
        string sanitized = SanitizeKey(baselineKey);
        string diagnosticsPath = Path.Combine(_rootDirectory, $"{sanitized}-diagnostics.json");
        string manifestPath = Path.Combine(_rootDirectory, $"{sanitized}-manifest.json");
        string snapshotPath = Path.Combine(_rootDirectory, $"{sanitized}-snapshot.json");
        File.WriteAllText(diagnosticsPath, JsonConvert.SerializeObject(report, JsonSettings), new UTF8Encoding(false));
        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, JsonSettings), new UTF8Encoding(false));
        File.WriteAllText(snapshotPath, JsonConvert.SerializeObject(snapshot, JsonSettings), new UTF8Encoding(false));
    }

    private static string SanitizeKey(string baselineKey)
    {
        if (string.IsNullOrWhiteSpace(baselineKey))
        {
            return "default";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string value = baselineKey.Trim();
        for (int i = 0; i < invalid.Length; i++)
        {
            value = value.Replace(invalid[i], '_');
        }

        return value;
    }
}
