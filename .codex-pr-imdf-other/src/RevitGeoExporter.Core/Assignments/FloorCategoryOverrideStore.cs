using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Core.Assignments;

public sealed class FloorCategoryOverrideStore
{
    private readonly MappingRuleStore _mappingRuleStore;

    public FloorCategoryOverrideStore(string? rootDirectory = null)
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
        return new LoadResult<IReadOnlyDictionary<string, string>>(result.Value.FloorCategoryOverrides, result.Warnings);
    }

    public void Save(string projectKey, IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        ProjectMappingRules current = _mappingRuleStore.Load(projectKey);
        ProjectMappingRules updated = ProjectMappingRules.Create(
            NormalizeOverrides(overrides),
            current.RoomCategoryOverrides,
            current.FamilyCategoryOverrides,
            current.AcceptedOpeningFamilies);
        _mappingRuleStore.Save(projectKey, updated);
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

    private static string NormalizeFloorTypeName(string? floorTypeName)
    {
        if (string.IsNullOrWhiteSpace(floorTypeName))
        {
            throw new ArgumentException("Floor type name is required.", nameof(floorTypeName));
        }

        return floorTypeName.Trim();
    }

    private static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category is required.", nameof(category));
        }

        return category.Trim();
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
}
