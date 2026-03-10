using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Core.Assignments;

public sealed class MappingRuleStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _rootDirectory;
    private readonly string _legacyBaseDirectory;

    public MappingRuleStore(string? rootDirectory = null, string? legacyBaseDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? GetDefaultRootDirectory()
            : rootDirectory!.Trim();
        _legacyBaseDirectory = string.IsNullOrWhiteSpace(legacyBaseDirectory)
            ? GetDefaultBaseDirectory()
            : legacyBaseDirectory!.Trim();
    }

    public ProjectMappingRules Load(string projectKey)
    {
        return LoadWithDiagnostics(projectKey).Value;
    }

    public LoadResult<ProjectMappingRules> LoadWithDiagnostics(string projectKey)
    {
        string path = GetFilePath(projectKey);
        if (File.Exists(path))
        {
            return JsonFileLoadHelper.Load(
                path,
                createDefaultValue: () => ProjectMappingRules.Empty,
                deserialize: json =>
                {
                    MappingRuleDocument? document = JsonConvert.DeserializeObject<MappingRuleDocument>(json);
                    return ProjectMappingRules.FromRules(document?.Rules);
                },
                documentLabel: "Project mapping rules");
        }

        return LoadLegacyProjectRules(projectKey);
    }

    public void Save(string projectKey, ProjectMappingRules rules)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        string path = GetFilePath(projectKey);
        if (rules.Rules.Count == 0)
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

        string json = JsonConvert.SerializeObject(
            new MappingRuleDocument
            {
                ProjectKey = NormalizeProjectKey(projectKey),
                Rules = rules.Rules.ToList(),
            },
            JsonSettings);
        File.WriteAllText(path, json);
    }

    public void ExportToFile(string projectKey, ProjectMappingRules rules, string exportPath)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        if (string.IsNullOrWhiteSpace(exportPath))
        {
            throw new ArgumentException("An export path is required.", nameof(exportPath));
        }

        string? directory = Path.GetDirectoryName(exportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonConvert.SerializeObject(
            new MappingRuleDocument
            {
                ProjectKey = NormalizeProjectKey(projectKey),
                Rules = rules.Rules.ToList(),
            },
            JsonSettings);
        File.WriteAllText(exportPath, json);
    }

    public LoadResult<ProjectMappingRules> ImportFromFile(string importPath)
    {
        return JsonFileLoadHelper.Load(
            importPath,
            createDefaultValue: () => ProjectMappingRules.Empty,
            deserialize: json =>
            {
                MappingRuleDocument? document = JsonConvert.DeserializeObject<MappingRuleDocument>(json);
                return ProjectMappingRules.FromRules(document?.Rules);
            },
            documentLabel: "Imported project mapping rules");
    }

    private LoadResult<ProjectMappingRules> LoadLegacyProjectRules(string projectKey)
    {
        List<string> warnings = new();
        Dictionary<string, string> floorOverrides = LoadLegacyMappingDictionary(
            Path.Combine(GetLegacyRootDirectory("floor-category-overrides"), GetHashedFileName(projectKey)),
            "Floor category overrides",
            MappingRuleType.FloorCategory,
            warnings);
        Dictionary<string, string> familyOverrides = LoadLegacyMappingDictionary(
            Path.Combine(GetLegacyRootDirectory("family-category-overrides"), GetHashedFileName(projectKey)),
            "Family category overrides",
            MappingRuleType.FamilyCategory,
            warnings);
        List<string> acceptedOpenings = LoadLegacyAcceptedFamilies(
            Path.Combine(GetLegacyRootDirectory("accepted-opening-families"), GetHashedFileName(projectKey)),
            warnings);

        return new LoadResult<ProjectMappingRules>(
            ProjectMappingRules.Create(floorOverrides, null, familyOverrides, acceptedOpenings),
            warnings);
    }

    private string GetFilePath(string projectKey)
    {
        return Path.Combine(_rootDirectory, GetHashedFileName(projectKey));
    }

    private static Dictionary<string, string> LoadLegacyMappingDictionary(
        string path,
        string documentLabel,
        MappingRuleType ruleType,
        ICollection<string> warnings)
    {
        LoadResult<LegacyMappingDocument> result = JsonFileLoadHelper.Load(
            path,
            createDefaultValue: () => new LegacyMappingDocument(),
            deserialize: json => JsonConvert.DeserializeObject<LegacyMappingDocument>(json),
            documentLabel: documentLabel);
        foreach (string warning in result.Warnings)
        {
            warnings.Add(warning);
        }

        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in result.Value.Overrides ?? new Dictionary<string, string>())
        {
            string key = MappingRule.NormalizeMatchValue(entry.Key);
            string? value = MappingRule.NormalizeResolvedValue(ruleType, entry.Value);
            if (key.Length == 0 || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[key] = value!;
        }

        return normalized;
    }

    private static List<string> LoadLegacyAcceptedFamilies(string path, ICollection<string> warnings)
    {
        LoadResult<LegacyOpeningDocument> result = JsonFileLoadHelper.Load(
            path,
            createDefaultValue: () => new LegacyOpeningDocument(),
            deserialize: json => JsonConvert.DeserializeObject<LegacyOpeningDocument>(json),
            documentLabel: "Accepted opening families");
        foreach (string warning in result.Warnings)
        {
            warnings.Add(warning);
        }

        return (result.Value.Families ?? new List<string>())
            .Select(MappingRule.NormalizeMatchValue)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetDefaultRootDirectory()
    {
        return Path.Combine(GetDefaultBaseDirectory(), "mapping-rules");
    }

    private string GetLegacyRootDirectory(string folderName)
    {
        return Path.Combine(_legacyBaseDirectory, folderName);
    }

    private static string GetDefaultBaseDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RevitGeoExporter");
    }

    private static string GetHashedFileName(string projectKey)
    {
        return $"{ComputeSha256(NormalizeProjectKey(projectKey))}.json";
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

    private sealed class MappingRuleDocument
    {
        public string? ProjectKey { get; set; }

        public List<MappingRule>? Rules { get; set; }
    }

    private sealed class LegacyMappingDocument
    {
        public string? ProjectKey { get; set; }

        public Dictionary<string, string>? Overrides { get; set; }
    }

    private sealed class LegacyOpeningDocument
    {
        public string? ProjectKey { get; set; }

        public List<string>? Families { get; set; }
    }
}

