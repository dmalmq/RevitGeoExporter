using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Assignments;

public sealed class ProjectMappingRules
{
    public static ProjectMappingRules Empty { get; } = new(
        Array.Empty<MappingRule>(),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        Array.Empty<string>());

    private ProjectMappingRules(
        IReadOnlyList<MappingRule> rules,
        IReadOnlyDictionary<string, string> floorCategoryOverrides,
        IReadOnlyDictionary<string, string> roomCategoryOverrides,
        IReadOnlyDictionary<string, string> familyCategoryOverrides,
        IReadOnlyList<string> acceptedOpeningFamilies)
    {
        Rules = rules;
        FloorCategoryOverrides = floorCategoryOverrides;
        RoomCategoryOverrides = roomCategoryOverrides;
        FamilyCategoryOverrides = familyCategoryOverrides;
        AcceptedOpeningFamilies = acceptedOpeningFamilies;
    }

    public IReadOnlyList<MappingRule> Rules { get; }

    public IReadOnlyDictionary<string, string> FloorCategoryOverrides { get; }

    public IReadOnlyDictionary<string, string> RoomCategoryOverrides { get; }

    public IReadOnlyDictionary<string, string> FamilyCategoryOverrides { get; }

    public IReadOnlyList<string> AcceptedOpeningFamilies { get; }

    public static ProjectMappingRules Create(
        IReadOnlyDictionary<string, string>? floorCategoryOverrides,
        IReadOnlyDictionary<string, string>? roomCategoryOverrides,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides,
        IReadOnlyList<string>? acceptedOpeningFamilies)
    {
        List<MappingRule> rules = new();

        Dictionary<string, string> floor = NormalizeMappings(
            floorCategoryOverrides,
            MappingRuleType.FloorCategory,
            rules);
        Dictionary<string, string> room = NormalizeMappings(
            roomCategoryOverrides,
            MappingRuleType.RoomCategory,
            rules);
        Dictionary<string, string> family = NormalizeMappings(
            familyCategoryOverrides,
            MappingRuleType.FamilyCategory,
            rules);
        List<string> openings = NormalizeAcceptedOpeningFamilies(acceptedOpeningFamilies, rules);

        return new ProjectMappingRules(rules, floor, room, family, openings);
    }

    public static ProjectMappingRules FromRules(IEnumerable<MappingRule>? rules)
    {
        List<MappingRule> normalizedRules = new();
        Dictionary<string, string> floor = new(StringComparer.Ordinal);
        Dictionary<string, string> room = new(StringComparer.Ordinal);
        Dictionary<string, string> family = new(StringComparer.Ordinal);
        HashSet<string> openings = new(StringComparer.Ordinal);

        foreach (MappingRule rule in rules ?? Array.Empty<MappingRule>())
        {
            MappingRule normalized = (rule ?? new MappingRule()).Normalize();
            if (normalized.MatchValue.Length == 0)
            {
                continue;
            }

            switch (normalized.RuleType)
            {
                case MappingRuleType.FloorCategory:
                    if (TryAddResolvedRule(normalized, floor, normalizedRules))
                    {
                    }

                    break;
                case MappingRuleType.RoomCategory:
                    if (TryAddResolvedRule(normalized, room, normalizedRules))
                    {
                    }

                    break;
                case MappingRuleType.FamilyCategory:
                    if (TryAddResolvedRule(normalized, family, normalizedRules))
                    {
                    }

                    break;
                case MappingRuleType.AcceptedOpeningFamily:
                    if (openings.Add(normalized.MatchValue))
                    {
                        normalizedRules.Add(new MappingRule
                        {
                            RuleType = MappingRuleType.AcceptedOpeningFamily,
                            MatchValue = normalized.MatchValue,
                        });
                    }

                    break;
            }
        }

        List<MappingRule> ordered = normalizedRules
            .OrderBy(rule => rule.RuleType)
            .ThenBy(rule => rule.MatchValue, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProjectMappingRules(
            ordered,
            floor,
            room,
            family,
            openings.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public MappingResolutionResult ResolveFloorCategory(string floorTypeName)
    {
        return ResolveMapping(MappingRuleType.FloorCategory, floorTypeName, FloorCategoryOverrides);
    }

    public MappingResolutionResult ResolveRoomCategory(string roomValue)
    {
        return ResolveMapping(MappingRuleType.RoomCategory, roomValue, RoomCategoryOverrides);
    }

    public MappingResolutionResult ResolveFamilyCategory(string familyName)
    {
        return ResolveMapping(MappingRuleType.FamilyCategory, familyName, FamilyCategoryOverrides);
    }

    public MappingResolutionResult ResolveAcceptedOpeningFamily(string familyName)
    {
        string key = MappingRule.NormalizeMatchValue(familyName);
        bool matched = AcceptedOpeningFamilies.Contains(key, StringComparer.Ordinal);
        return new MappingResolutionResult(
            MappingRuleType.AcceptedOpeningFamily,
            key,
            matched,
            matched ? key : null);
    }

    private static bool TryAddResolvedRule(
        MappingRule normalized,
        IDictionary<string, string> target,
        ICollection<MappingRule> normalizedRules)
    {
        if (string.IsNullOrWhiteSpace(normalized.ResolvedValue))
        {
            return false;
        }

        target[normalized.MatchValue] = normalized.ResolvedValue!;
        normalizedRules.Add(new MappingRule
        {
            RuleType = normalized.RuleType,
            MatchValue = normalized.MatchValue,
            ResolvedValue = normalized.ResolvedValue,
        });
        return true;
    }

    private static MappingResolutionResult ResolveMapping(
        MappingRuleType ruleType,
        string value,
        IReadOnlyDictionary<string, string> mappings)
    {
        string key = MappingRule.NormalizeMatchValue(value);
        return mappings.TryGetValue(key, out string resolved)
            ? new MappingResolutionResult(ruleType, key, true, resolved)
            : new MappingResolutionResult(ruleType, key, false, null);
    }

    private static Dictionary<string, string> NormalizeMappings(
        IReadOnlyDictionary<string, string>? mappings,
        MappingRuleType ruleType,
        ICollection<MappingRule> rules)
    {
        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in mappings ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            string matchValue = MappingRule.NormalizeMatchValue(entry.Key);
            string? resolvedValue = MappingRule.NormalizeResolvedValue(ruleType, entry.Value);
            if (matchValue.Length == 0 || string.IsNullOrWhiteSpace(resolvedValue))
            {
                continue;
            }

            normalized[matchValue] = resolvedValue!;
        }

        foreach (KeyValuePair<string, string> entry in normalized.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            rules.Add(new MappingRule
            {
                RuleType = ruleType,
                MatchValue = entry.Key,
                ResolvedValue = entry.Value,
            });
        }

        return normalized;
    }

    private static List<string> NormalizeAcceptedOpeningFamilies(
        IReadOnlyList<string>? families,
        ICollection<MappingRule> rules)
    {
        List<string> normalized = (families ?? Array.Empty<string>())
            .Select(MappingRule.NormalizeMatchValue)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string family in normalized)
        {
            rules.Add(new MappingRule
            {
                RuleType = MappingRuleType.AcceptedOpeningFamily,
                MatchValue = family,
            });
        }

        return normalized;
    }
}
