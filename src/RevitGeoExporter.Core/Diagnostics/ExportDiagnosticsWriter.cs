using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsWriter
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    public string WriteJson(string outputDirectory, ExportDiagnosticsReport report)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        }

        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "export-diagnostics.json");
        string json = JsonConvert.SerializeObject(report, JsonSettings);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
