using System;
using System.IO;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.UI;

public sealed class ExportDialogSettingsStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _settingsFilePath;

    public ExportDialogSettingsStore(string? settingsFilePath = null)
    {
        _settingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? GetDefaultSettingsFilePath()
            : settingsFilePath!.Trim();
    }

    public ExportDialogSettings Load()
    {
        return LoadWithDiagnostics().Value;
    }

    public LoadResult<ExportDialogSettings> LoadWithDiagnostics()
    {
        return JsonFileLoadHelper.Load(
            _settingsFilePath,
            createDefaultValue: () => new ExportDialogSettings(),
            deserialize: json => JsonConvert.DeserializeObject<ExportDialogSettings>(json),
            documentLabel: "Export dialog settings");
    }

    public void Save(ExportDialogSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonConvert.SerializeObject(settings, JsonSettings);
        File.WriteAllText(_settingsFilePath, json);
    }

    private static string GetDefaultSettingsFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RevitGeoExporter", "settings.json");
    }
}
