using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Core.Utilities;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.UI;

public sealed class ExportProfileStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _settingsFilePath;

    public ExportProfileStore(string? settingsFilePath = null)
    {
        _settingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? GetDefaultSettingsFilePath()
            : settingsFilePath!.Trim();
    }

    public LoadResult<IReadOnlyList<ExportProfile>> LoadWithDiagnostics(string projectKey)
    {
        LoadResult<ExportProfileDocument> result = JsonFileLoadHelper.Load(
            _settingsFilePath,
            createDefaultValue: () => new ExportProfileDocument(),
            deserialize: json => JsonConvert.DeserializeObject<ExportProfileDocument>(json),
            documentLabel: "Export profiles");
        List<ExportProfile> profiles = NormalizeProfiles(result.Value, projectKey);
        return new LoadResult<IReadOnlyList<ExportProfile>>(profiles, result.Warnings);
    }

    public void SaveProfile(string projectKey, ExportProfile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        ExportProfileDocument document = LoadDocument();
        ExportProfile normalizedProfile = NormalizeProfile(profile);
        if (normalizedProfile.Scope == ExportProfileScope.Project)
        {
            List<ExportProfile> profiles = GetProjectProfiles(document, projectKey);
            UpsertProfile(profiles, normalizedProfile);
        }
        else
        {
            UpsertProfile(document.GlobalProfiles, normalizedProfile);
        }

        SaveDocument(document);
    }

    public void DeleteProfile(string projectKey, ExportProfile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        ExportProfileDocument document = LoadDocument();
        string normalizedName = NormalizeName(profile.Name);
        if (profile.Scope == ExportProfileScope.Project)
        {
            List<ExportProfile> profiles = GetProjectProfiles(document, projectKey);
            profiles.RemoveAll(candidate => string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            document.GlobalProfiles.RemoveAll(candidate => string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        }

        SaveDocument(document);
    }

    public void RenameProfile(string projectKey, ExportProfile profile, string newName)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        string normalizedNewName = NormalizeName(newName);
        if (normalizedNewName.Length == 0)
        {
            throw new ArgumentException("A new profile name is required.", nameof(newName));
        }

        ExportProfileDocument document = LoadDocument();
        List<ExportProfile> profiles = profile.Scope == ExportProfileScope.Project
            ? GetProjectProfiles(document, projectKey)
            : document.GlobalProfiles;

        int existingIndex = profiles.FindIndex(candidate =>
            string.Equals(candidate.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
        {
            throw new InvalidOperationException($"Profile '{profile.Name}' could not be found.");
        }

        ExportProfile renamed = NormalizeProfile(profiles[existingIndex]);
        renamed.Name = normalizedNewName;

        profiles.RemoveAt(existingIndex);
        UpsertProfile(profiles, renamed);
        SaveDocument(document);
    }

    public void ReplaceProfiles(string projectKey, ExportProfileScope scope, IEnumerable<ExportProfile> profiles)
    {
        ExportProfileDocument document = LoadDocument();
        List<ExportProfile> normalizedProfiles = (profiles ?? Array.Empty<ExportProfile>())
            .Where(profile => profile != null)
            .Select(NormalizeProfile)
            .Where(profile => profile.Name.Length > 0 && profile.Scope == scope)
            .ToList();

        if (scope == ExportProfileScope.Project)
        {
            List<ExportProfile> projectProfiles = GetProjectProfiles(document, projectKey);
            projectProfiles.Clear();
            projectProfiles.AddRange(normalizedProfiles);
        }
        else
        {
            document.GlobalProfiles.Clear();
            document.GlobalProfiles.AddRange(normalizedProfiles);
        }

        SaveDocument(document);
    }

    private ExportProfileDocument LoadDocument()
    {
        return JsonFileLoadHelper.Load(
            _settingsFilePath,
            createDefaultValue: () => new ExportProfileDocument(),
            deserialize: json => JsonConvert.DeserializeObject<ExportProfileDocument>(json),
            documentLabel: "Export profiles").Value;
    }

    private void SaveDocument(ExportProfileDocument document)
    {
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonConvert.SerializeObject(document, JsonSettings);
        File.WriteAllText(_settingsFilePath, json);
    }

    private static List<ExportProfile> NormalizeProfiles(ExportProfileDocument document, string projectKey)
    {
        List<ExportProfile> profiles = new();
        profiles.AddRange((document.GlobalProfiles ?? new List<ExportProfile>())
            .Select(NormalizeProfile)
            .Where(profile => profile.Name.Length > 0));

        if (document.ProjectProfiles != null &&
            document.ProjectProfiles.TryGetValue(projectKey, out List<ExportProfile>? projectProfiles))
        {
            profiles.AddRange(projectProfiles
                .Select(NormalizeProfile)
                .Where(profile => profile.Name.Length > 0));
        }

        return profiles
            .OrderBy(profile => profile.Scope)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExportProfile NormalizeProfile(ExportProfile profile)
    {
        return new ExportProfile
        {
            Name = NormalizeName(profile.Name),
            Scope = profile.Scope,
            OutputDirectory = profile.OutputDirectory?.Trim() ?? string.Empty,
            TargetEpsg = profile.TargetEpsg > 0 ? profile.TargetEpsg : ProjectInfo.DefaultTargetEpsg,
            FeatureTypes = profile.FeatureTypes == RevitGeoExporter.Export.ExportFeatureType.None
                ? RevitGeoExporter.Export.ExportFeatureType.All
                : profile.FeatureTypes,
            SelectedViewIds = (profile.SelectedViewIds ?? new List<long>()).Distinct().OrderBy(id => id).ToList(),
            IncrementalExportMode = profile.IncrementalExportMode,
            GenerateDiagnosticsReport = profile.GenerateDiagnosticsReport,
            GeneratePackageOutput = profile.GeneratePackageOutput,
            IncludePackageLegend = profile.IncludePackageLegend,
            PackagingMode = profile.PackagingMode,
            ValidateAfterWrite = profile.ValidateAfterWrite,
            GenerateQgisArtifacts = profile.GenerateQgisArtifacts,
            PostExportActions = profile.PostExportActions?.Clone() ?? new RevitGeoExporter.Export.PostExportActionOptions(),
            GeometryRepairOptions = profile.GeometryRepairOptions?.Clone() ?? new RevitGeoExporter.Core.Geometry.GeometryRepairOptions(),
            UiLanguage = profile.UiLanguage,
            CoordinateMode = profile.CoordinateMode,
            UnitSource = profile.UnitSource,
            UnitGeometrySource = profile.UnitGeometrySource,
            UnitAttributeSource = profile.UnitAttributeSource,
            RoomCategoryParameterName = string.IsNullOrWhiteSpace(profile.RoomCategoryParameterName) ? "Name" : profile.RoomCategoryParameterName.Trim(),
            LinkExportOptions = profile.LinkExportOptions?.Clone() ?? new RevitGeoExporter.Export.LinkExportOptions(),
            SchemaProfiles = SchemaProfile.NormalizeProfiles(profile.SchemaProfiles).Select(schema => schema.Clone()).ToList(),
            ActiveSchemaProfileName = SchemaProfile.ResolveActiveName(profile.SchemaProfiles, profile.ActiveSchemaProfileName),
            ValidationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(profile.ValidationPolicyProfiles).Select(policy => policy.Clone()).ToList(),
            ActiveValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(profile.ValidationPolicyProfiles, profile.ActiveValidationPolicyProfileName),
        };
    }

    private static void UpsertProfile(List<ExportProfile> profiles, ExportProfile profile)
    {
        int index = profiles.FindIndex(candidate => string.Equals(candidate.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            profiles.Add(profile);
        }
    }

    private static List<ExportProfile> GetProjectProfiles(ExportProfileDocument document, string projectKey)
    {
        document.ProjectProfiles ??= new Dictionary<string, List<ExportProfile>>(StringComparer.Ordinal);
        if (!document.ProjectProfiles.TryGetValue(projectKey, out List<ExportProfile>? profiles))
        {
            profiles = new List<ExportProfile>();
            document.ProjectProfiles[projectKey] = profiles;
        }

        return profiles;
    }

    private static string NormalizeName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name!.Trim();
    }

    private static string GetDefaultSettingsFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RevitGeoExporter", "profiles.json");
    }

    private sealed class ExportProfileDocument
    {
        public List<ExportProfile> GlobalProfiles { get; set; } = new();

        public Dictionary<string, List<ExportProfile>>? ProjectProfiles { get; set; }
    }
}
