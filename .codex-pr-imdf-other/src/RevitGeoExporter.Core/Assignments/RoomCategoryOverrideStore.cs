using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Core.Assignments;

public sealed class RoomCategoryOverrideStore
{
    private readonly MappingRuleStore _mappingRuleStore;

    public RoomCategoryOverrideStore(string? rootDirectory = null)
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
        return new LoadResult<IReadOnlyDictionary<string, string>>(result.Value.RoomCategoryOverrides, result.Warnings);
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
            NormalizeOverrides(overrides),
            current.FamilyCategoryOverrides,
            current.AcceptedOpeningFamilies);
        _mappingRuleStore.Save(projectKey, updated);
    }

    public void SetOverride(string projectKey, string roomValue, string category)
    {
        Dictionary<string, string> current = CopyOverrides(Load(projectKey));
        current[NormalizeRoomValue(roomValue)] = NormalizeCategory(category);
        Save(projectKey, current);
    }

    public void ClearOverride(string projectKey, string roomValue)
    {
        Dictionary<string, string> current = CopyOverrides(Load(projectKey));
        current.Remove(NormalizeRoomValue(roomValue));
        Save(projectKey, current);
    }

    private static Dictionary<string, string> NormalizeOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in overrides ?? EmptyOverrides())
        {
            string roomValue = NormalizeRoomValue(entry.Key);
            string category = NormalizeCategory(entry.Value);
            if (roomValue.Length == 0 || category.Length == 0)
            {
                continue;
            }

            normalized[roomValue] = category;
        }

        return normalized;
    }

    private static string NormalizeRoomValue(string? roomValue)
    {
        if (string.IsNullOrWhiteSpace(roomValue))
        {
            throw new ArgumentException("Room mapping value is required.", nameof(roomValue));
        }

        return roomValue.Trim();
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
