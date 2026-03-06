using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace RevitGeoExporter.Core.Assignments;

public sealed class FloorCategoryOverrideStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _rootDirectory;

    public FloorCategoryOverrideStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? GetDefaultRootDirectory()
            : rootDirectory!.Trim();
    }

    public IReadOnlyDictionary<string, string> Load(string projectKey)
    {
        string path = GetOverridesFilePath(projectKey);
        if (!File.Exists(path))
        {
            return EmptyOverrides();
        }

        try
        {
            string json = File.ReadAllText(path);
            FloorCategoryOverrideDocument? document =
                JsonConvert.DeserializeObject<FloorCategoryOverrideDocument>(json);
            return NormalizeOverrides(document?.Overrides);
        }
        catch
        {
            return EmptyOverrides();
        }
    }

    public void Save(string projectKey, IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        Dictionary<string, string> normalizedOverrides = NormalizeOverrides(overrides);
        string path = GetOverridesFilePath(projectKey);
        if (normalizedOverrides.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        FloorCategoryOverrideDocument document = new()
        {
            ProjectKey = NormalizeProjectKey(projectKey),
            Overrides = normalizedOverrides,
        };
        string json = JsonConvert.SerializeObject(document, JsonSettings);
        File.WriteAllText(path, json);
    }

    public void SetOverride(string projectKey, string floorTypeName, string category)
    {
        Dictionary<string, string> current = CopyOverrides(Load(projectKey));
        current[NormalizeFloorTypeName(floorTypeName)] = NormalizeCategory(category);
        Save(projectKey, current);
    }

    public void ClearOverride(string projectKey, string floorTypeName)
    {
        Dictionary<string, string> current = CopyOverrides(Load(projectKey));
        current.Remove(NormalizeFloorTypeName(floorTypeName));
        Save(projectKey, current);
    }

    private string GetOverridesFilePath(string projectKey)
    {
        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        string hashedProjectKey = ComputeSha256(normalizedProjectKey);
        return Path.Combine(_rootDirectory, $"{hashedProjectKey}.json");
    }

    private static Dictionary<string, string> NormalizeOverrides(
        IReadOnlyDictionary<string, string>? overrides)
    {
        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in overrides ?? EmptyOverrides())
        {
            string floorTypeName = NormalizeFloorTypeName(entry.Key);
            string category = NormalizeCategory(entry.Value);
            if (floorTypeName.Length == 0 || category.Length == 0)
            {
                continue;
            }

            normalized[floorTypeName] = category;
        }

        return normalized;
    }

    private static string NormalizeProjectKey(string? projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            throw new ArgumentException("Project key is required.", nameof(projectKey));
        }

        return projectKey!.Trim();
    }

    private static string NormalizeFloorTypeName(string? floorTypeName)
    {
        if (string.IsNullOrWhiteSpace(floorTypeName))
        {
            throw new ArgumentException("Floor type name is required.", nameof(floorTypeName));
        }

        return floorTypeName!.Trim();
    }

    private static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category is required.", nameof(category));
        }

        return category!.Trim();
    }

    private static string ComputeSha256(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(bytes);
        StringBuilder builder = new(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            builder.Append(hash[i].ToString("x2"));
        }

        return builder.ToString();
    }

    private static string GetDefaultRootDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RevitGeoExporter", "floor-category-overrides");
    }

    private static Dictionary<string, string> EmptyOverrides()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> CopyOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        Dictionary<string, string> copied = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in overrides)
        {
            copied[entry.Key] = entry.Value;
        }

        return copied;
    }

    private sealed class FloorCategoryOverrideDocument
    {
        public string? ProjectKey { get; set; }

        public Dictionary<string, string>? Overrides { get; set; }
    }
}
