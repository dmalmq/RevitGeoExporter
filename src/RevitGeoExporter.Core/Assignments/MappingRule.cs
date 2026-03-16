using System;

namespace RevitGeoExporter.Core.Assignments;

public sealed class MappingRule
{
    public MappingRuleType RuleType { get; set; }

    public string MatchValue { get; set; } = string.Empty;

    public string? ResolvedValue { get; set; }

    public MappingRule Normalize()
    {
        string normalizedMatchValue = NormalizeMatchValue(MatchValue);
        return new MappingRule
        {
            RuleType = RuleType,
            MatchValue = normalizedMatchValue,
            ResolvedValue = NormalizeResolvedValue(RuleType, ResolvedValue),
        };
    }

    internal static string NormalizeMatchValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    internal static string? NormalizeResolvedValue(MappingRuleType ruleType, string? value)
    {
        if (ruleType == MappingRuleType.AcceptedOpeningFamily)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }
}
