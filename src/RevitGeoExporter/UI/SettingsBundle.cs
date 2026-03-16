using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Assignments;

namespace RevitGeoExporter.UI;

public sealed class SettingsBundle
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _projectKey;
    private readonly ExportDialogSettingsStore _settingsStore;
    private readonly ExportProfileStore _profileStore;
    private readonly MappingRuleStore _mappingRuleStore;

    public SettingsBundle(
        string projectKey,
        ExportDialogSettingsStore? settingsStore = null,
        ExportProfileStore? profileStore = null,
        MappingRuleStore? mappingRuleStore = null)
    {
        _projectKey = string.IsNullOrWhiteSpace(projectKey)
            ? throw new ArgumentException("A project key is required.", nameof(projectKey))
            : projectKey.Trim();
        _settingsStore = settingsStore ?? new ExportDialogSettingsStore();
        _profileStore = profileStore ?? new ExportProfileStore();
        _mappingRuleStore = mappingRuleStore ?? new MappingRuleStore();
    }

    public SettingsBundleSnapshot Load()
    {
        var settingsLoad = _settingsStore.LoadWithDiagnostics();
        var profilesLoad = _profileStore.LoadWithDiagnostics(_projectKey);
        var mappingsLoad = _mappingRuleStore.LoadWithDiagnostics(_projectKey);

        return new SettingsBundleSnapshot
        {
            GlobalSettings = settingsLoad.Value,
            Profiles = profilesLoad.Value.ToList(),
            ProjectMappings = mappingsLoad.Value,
            StatusEntries = BuildStatuses(settingsLoad.Warnings, profilesLoad.Warnings, mappingsLoad.Warnings),
        };
    }

    public void SaveGlobalSettings(ExportDialogSettings settings)
    {
        _settingsStore.Save(settings);
    }

    public void SaveProjectMappings(ProjectMappingRules rules)
    {
        _mappingRuleStore.Save(_projectKey, rules);
    }

    public void ReplaceProfiles(SettingsScope scope, IEnumerable<ExportProfile> profiles)
    {
        _profileStore.ReplaceProfiles(
            _projectKey,
            scope == SettingsScope.Global ? ExportProfileScope.Global : ExportProfileScope.Project,
            profiles ?? Array.Empty<ExportProfile>());
    }

    public void ExportScope(SettingsScope scope, SettingsBundleSnapshot snapshot, string exportPath)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
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

        string json = scope == SettingsScope.Global
            ? JsonConvert.SerializeObject(
                new GlobalSettingsDocument
                {
                    Settings = snapshot.GlobalSettings,
                    Profiles = snapshot.Profiles
                        .Where(profile => profile.Scope == ExportProfileScope.Global)
                        .ToList(),
                },
                JsonSettings)
            : JsonConvert.SerializeObject(
                new ProjectSettingsDocument
                {
                    ProjectKey = _projectKey,
                    Profiles = snapshot.Profiles
                        .Where(profile => profile.Scope == ExportProfileScope.Project)
                        .ToList(),
                    MappingRules = snapshot.ProjectMappings.Rules.ToList(),
                },
                JsonSettings);

        File.WriteAllText(exportPath, json);
    }

    public SettingsImportResult ImportScope(SettingsScope scope, string importPath)
    {
        if (string.IsNullOrWhiteSpace(importPath))
        {
            throw new ArgumentException("An import path is required.", nameof(importPath));
        }

        try
        {
            string json = File.ReadAllText(importPath);
            if (scope == SettingsScope.Global)
            {
                GlobalSettingsDocument? document = JsonConvert.DeserializeObject<GlobalSettingsDocument>(json);
                SaveGlobalSettings(document?.Settings ?? new ExportDialogSettings());
                ReplaceProfiles(SettingsScope.Global, (IEnumerable<ExportProfile>)(document?.Profiles ?? new List<ExportProfile>()));
            }
            else
            {
                ProjectSettingsDocument? document = JsonConvert.DeserializeObject<ProjectSettingsDocument>(json);
                SaveProjectMappings(ProjectMappingRules.FromRules(document?.MappingRules));
                ReplaceProfiles(SettingsScope.Project, (IEnumerable<ExportProfile>)(document?.Profiles ?? new List<ExportProfile>()));
            }

            return new SettingsImportResult(scope, succeeded: true);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            return new SettingsImportResult(
                scope,
                succeeded: false,
                new[]
                {
                    new SettingsStatusEntry(scope, ex.Message, importPath),
                });
        }
    }

    public void ResetScope(SettingsScope scope)
    {
        if (scope == SettingsScope.Global)
        {
            SaveGlobalSettings(new ExportDialogSettings());
            ReplaceProfiles(SettingsScope.Global, Array.Empty<ExportProfile>());
            return;
        }

        SaveProjectMappings(ProjectMappingRules.Empty);
        ReplaceProfiles(SettingsScope.Project, Array.Empty<ExportProfile>());
    }

    private static IReadOnlyList<SettingsStatusEntry> BuildStatuses(
        IReadOnlyList<string> settingsWarnings,
        IReadOnlyList<string> profileWarnings,
        IReadOnlyList<string> mappingWarnings)
    {
        List<SettingsStatusEntry> statuses = new();
        statuses.AddRange((settingsWarnings ?? Array.Empty<string>()).Select(message => new SettingsStatusEntry(SettingsScope.Global, message)));
        statuses.AddRange((profileWarnings ?? Array.Empty<string>()).Select(message => new SettingsStatusEntry(SettingsScope.Global, message)));
        statuses.AddRange((mappingWarnings ?? Array.Empty<string>()).Select(message => new SettingsStatusEntry(SettingsScope.Project, message)));
        return statuses;
    }

    private sealed class GlobalSettingsDocument
    {
        public ExportDialogSettings Settings { get; set; } = new();

        public List<ExportProfile> Profiles { get; set; } = new();
    }

    private sealed class ProjectSettingsDocument
    {
        public string ProjectKey { get; set; } = string.Empty;

        public List<ExportProfile> Profiles { get; set; } = new();

        public List<MappingRule> MappingRules { get; set; } = new();
    }
}
