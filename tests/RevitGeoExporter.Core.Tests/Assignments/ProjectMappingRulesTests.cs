using System.Collections.Generic;
using RevitGeoExporter.Core.Assignments;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Assignments;

public sealed class ProjectMappingRulesTests
{
    [Fact]
    public void FromRules_NormalizesAndResolvesTypedMappings()
    {
        ProjectMappingRules rules = ProjectMappingRules.FromRules(new[]
        {
            new MappingRule { RuleType = MappingRuleType.FloorCategory, MatchValue = " Wrong Floor ", ResolvedValue = " walkway " },
            new MappingRule { RuleType = MappingRuleType.FamilyCategory, MatchValue = " Custom Family ", ResolvedValue = " retail " },
            new MappingRule { RuleType = MappingRuleType.AcceptedOpeningFamily, MatchValue = " EV扉 " },
        });

        Assert.Equal("walkway", rules.ResolveFloorCategory("Wrong Floor").ResolvedValue);
        Assert.Equal("retail", rules.ResolveFamilyCategory("Custom Family").ResolvedValue);
        Assert.True(rules.ResolveAcceptedOpeningFamily("EV扉").Matched);
    }

    [Fact]
    public void Create_ProducesExpectedRuleInventory()
    {
        ProjectMappingRules rules = ProjectMappingRules.Create(
            new Dictionary<string, string> { ["Floor A"] = "walkway" },
            new Dictionary<string, string> { ["Family A"] = "retail" },
            new[] { "EV扉", "EV扉" });

        Assert.Equal(3, rules.Rules.Count);
        Assert.Single(rules.AcceptedOpeningFamilies);
    }
}
