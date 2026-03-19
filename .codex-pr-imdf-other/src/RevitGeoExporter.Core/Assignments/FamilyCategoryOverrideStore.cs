using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Core.Assignments;

public sealed class FamilyCategoryOverrideStore
{
    private readonly MappingRuleStore _mappingRuleStore;

    public FamilyCategoryOverrideStore(string? rootDirectory = null)
    {
        _mappingRuleStore = new MappingRuleStore(rootDirectory);
    }

    public IReadOnlyDictionary<string, string> Load(string projectKey)
    {
        return LoadWithDiagnostics(projectKey).Value;
    }

    public LoadResult<IReadOnlyDictionary<string, string>> LoadWithDiagnostics(string projectKey)
    {
        LoadResult<ProjectMappingRules> result = _mappingRuleStore.LoadWithDiagnostics(projectKey);
        return new LoadResult<IReadOnlyDictionary<string, string>>(result.Value.FamilyCategoryOverrides, result.Warnings);
    }

    public void Save(string projectKey, IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        ProjectMappingRules current = _mappingRuleStore.Load(projectKey);
        ProjectMappingRules updated = ProjectMappingRules.Create(
            current.FloorCategoryOverrides,
            current.RoomCategoryOverrides,
            NormalizeOverrides(overrides),
            current.AcceptedOpeningFamilies);
        _mappingRuleStore.Save(projectKey, updated);
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

    private static string NormalizeFamilyName(string? familyName)
    {
        return string.IsNullOrWhiteSpace(familyName) ? string.Empty : familyName.Trim();
    }

    private static string NormalizeCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? string.Empty : category.Trim();
    }

    private static Dictionary<string, string> EmptyOverrides()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
