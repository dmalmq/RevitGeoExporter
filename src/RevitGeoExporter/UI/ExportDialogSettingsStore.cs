using System;
using System.IO;
using Newtonsoft.Json;

namespace RevitGeoExporter.UI;

public sealed class ExportDialogSettingsStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    public ExportDialogSettings Load()
    {
        string path = GetSettingsFilePath();
        if (!File.Exists(path))
        {
            return new ExportDialogSettings();
        }

        try
        {
            string json = File.ReadAllText(path);
            ExportDialogSettings? settings = JsonConvert.DeserializeObject<ExportDialogSettings>(json);
            return settings ?? new ExportDialogSettings();
        }
        catch
        {
            return new ExportDialogSettings();
        }
    }

    public void Save(ExportDialogSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        string path = GetSettingsFilePath();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonConvert.SerializeObject(settings, JsonSettings);
        File.WriteAllText(path, json);
    }

    private static string GetSettingsFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RevitGeoExporter", "settings.json");
    }
}
