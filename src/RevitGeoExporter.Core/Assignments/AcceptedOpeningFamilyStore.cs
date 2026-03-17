using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Core.Assignments;

public sealed class AcceptedOpeningFamilyStore
{
    private readonly MappingRuleStore _mappingRuleStore;

    public AcceptedOpeningFamilyStore(string? rootDirectory = null)
    {
        _mappingRuleStore = new MappingRuleStore(rootDirectory);
    }

    public IReadOnlyList<string> Load(string projectKey)
    {
        return LoadWithDiagnostics(projectKey).Value;
    }

    public LoadResult<IReadOnlyList<string>> LoadWithDiagnostics(string projectKey)
    {
        LoadResult<ProjectMappingRules> result = _mappingRuleStore.LoadWithDiagnostics(projectKey);
        return new LoadResult<IReadOnlyList<string>>(result.Value.AcceptedOpeningFamilies, result.Warnings);
    }

    public void Save(string projectKey, IReadOnlyList<string> families)
    {
        if (families is null)
        {
            throw new ArgumentNullException(nameof(families));
        }

        ProjectMappingRules current = _mappingRuleStore.Load(projectKey);
        ProjectMappingRules updated = ProjectMappingRules.Create(
            current.FloorCategoryOverrides,
            current.RoomCategoryOverrides,
            current.FamilyCategoryOverrides,
            NormalizeFamilies(families));
        _mappingRuleStore.Save(projectKey, updated);
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
}
