using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Core.Assignments;

public sealed class AcceptedOpeningFamilyStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _rootDirectory;

    public AcceptedOpeningFamilyStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? GetDefaultRootDirectory()
            : rootDirectory!.Trim();
    }

    public IReadOnlyList<string> Load(string projectKey)
    {
        return LoadWithDiagnostics(projectKey).Value;
    }

    public LoadResult<IReadOnlyList<string>> LoadWithDiagnostics(string projectKey)
    {
        string path = GetFilePath(projectKey);
        LoadResult<List<string>> result = JsonFileLoadHelper.Load(
            path,
            createDefaultValue: () => new List<string>(),
            deserialize: json =>
            {
                AcceptedOpeningFamilyDocument? document =
                    JsonConvert.DeserializeObject<AcceptedOpeningFamilyDocument>(json);
                return NormalizeFamilies(document?.Families);
            },
            documentLabel: "Accepted opening families");
        return new LoadResult<IReadOnlyList<string>>(result.Value, result.Warnings);
    }

    public void Save(string projectKey, IReadOnlyList<string> families)
    {
        if (families is null)
        {
            throw new ArgumentNullException(nameof(families));
        }

        List<string> normalizedFamilies = NormalizeFamilies(families);
        string path = GetFilePath(projectKey);
        if (normalizedFamilies.Count == 0)
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

        AcceptedOpeningFamilyDocument document = new()
        {
            ProjectKey = NormalizeProjectKey(projectKey),
            Families = normalizedFamilies,
        };
        string json = JsonConvert.SerializeObject(document, JsonSettings);
        File.WriteAllText(path, json);
    }

    private string GetFilePath(string projectKey)
    {
        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        string hashedProjectKey = ComputeSha256(normalizedProjectKey);
        return Path.Combine(_rootDirectory, $"{hashedProjectKey}.json");
    }

    private static List<string> NormalizeFamilies(IEnumerable<string>? families)
    {
        return (families ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeProjectKey(string? projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            throw new ArgumentException("Project key is required.", nameof(projectKey));
        }

        return projectKey!.Trim();
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
        return Path.Combine(appData, "RevitGeoExporter", "accepted-opening-families");
    }

    private sealed class AcceptedOpeningFamilyDocument
    {
        public string? ProjectKey { get; set; }

        public List<string>? Families { get; set; }
    }
}
