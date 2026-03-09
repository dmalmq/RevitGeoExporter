using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Core.Assignments;

public sealed class FamilyCategoryOverrideStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _rootDirectory;

    public FamilyCategoryOverrideStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? GetDefaultRootDirectory()
            : rootDirectory!.Trim();
    }

    public IReadOnlyDictionary<string, string> Load(string projectKey)
    {
        return LoadWithDiagnostics(projectKey).Value;
    }

    public LoadResult<IReadOnlyDictionary<string, string>> LoadWithDiagnostics(string projectKey)
    {
        string path = GetOverridesFilePath(projectKey);
        LoadResult<Dictionary<string, string>> result = JsonFileLoadHelper.Load(
            path,
            createDefaultValue: EmptyOverrides,
            deserialize: json =>
            {
                FamilyCategoryOverrideDocument? document =
                    JsonConvert.DeserializeObject<FamilyCategoryOverrideDocument>(json);
                return NormalizeOverrides(document?.Overrides);
            },
            documentLabel: "Family category overrides");
        return new LoadResult<IReadOnlyDictionary<string, string>>(result.Value, result.Warnings);
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

        FamilyCategoryOverrideDocument document = new()
        {
            ProjectKey = NormalizeProjectKey(projectKey),
            Overrides = normalizedOverrides,
        };
        string json = JsonConvert.SerializeObject(document, JsonSettings);
        File.WriteAllText(path, json);
    }

    private string GetOverridesFilePath(string projectKey)
    {
        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        string hashedProjectKey = ComputeSha256(normalizedProjectKey);
        return Path.Combine(_rootDirectory, $"{hashedProjectKey}.json");
    }

    private static Dictionary<string, string> NormalizeOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in overrides ?? EmptyOverrides())
        {
            string familyName = NormalizeFamilyName(entry.Key);
            string category = NormalizeCategory(entry.Value);
            if (familyName.Length == 0 || category.Length == 0)
            {
                continue;
            }

            normalized[familyName] = category;
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

    private static string NormalizeFamilyName(string? familyName)
    {
        return string.IsNullOrWhiteSpace(familyName) ? string.Empty : familyName!.Trim();
    }

    private static string NormalizeCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? string.Empty : category!.Trim();
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
        return Path.Combine(appData, "RevitGeoExporter", "family-category-overrides");
    }

    private static Dictionary<string, string> EmptyOverrides()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private sealed class FamilyCategoryOverrideDocument
    {
        public string? ProjectKey { get; set; }

        public Dictionary<string, string>? Overrides { get; set; }
    }
}
