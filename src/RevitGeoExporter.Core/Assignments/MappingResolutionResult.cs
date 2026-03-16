namespace RevitGeoExporter.Core.Assignments;

public sealed class MappingResolutionResult
{
    public MappingResolutionResult(
        MappingRuleType ruleType,
        string inputValue,
        bool matched,
        string? resolvedValue)
    {
        RuleType = ruleType;
        InputValue = inputValue ?? string.Empty;
        Matched = matched;
        ResolvedValue = resolvedValue;
    }

    public MappingRuleType RuleType { get; }

    public string InputValue { get; }

    public bool Matched { get; }

    public string? ResolvedValue { get; }
}
