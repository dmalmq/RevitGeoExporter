using System.Collections.Generic;
using RevitGeoExporter.Core.Assignments;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Assignments;

public sealed class PreviewFloorAssignmentSessionTests
{
    [Fact]
    public void StageOverride_DoesNotMutateSavedOverridesUntilApplied()
    {
        PreviewFloorAssignmentSession session = new(new Dictionary<string, string>
        {
            ["Floor A"] = "walkway",
        });

        session.StageOverride("Floor A", "nonpublic");

        Assert.True(session.HasPendingChanges);
        Assert.Equal("walkway", session.SavedOverrides["Floor A"]);
        Assert.Equal("nonpublic", session.GetEffectiveOverrides()["Floor A"]);
    }

    [Fact]
    public void DiscardPendingChanges_RevertsEffectiveOverridesToSavedState()
    {
        PreviewFloorAssignmentSession session = new(new Dictionary<string, string>
        {
            ["Floor A"] = "walkway",
        });

        session.StageOverride("Floor A", "nonpublic");
        session.DiscardPendingChanges();

        Assert.False(session.HasPendingChanges);
        Assert.Equal("walkway", session.GetEffectiveOverrides()["Floor A"]);
    }

    [Fact]
    public void ApplyPendingChanges_PromotesPendingStateAndClearsDirtyFlag()
    {
        PreviewFloorAssignmentSession session = new(new Dictionary<string, string>
        {
            ["Floor A"] = "walkway",
        });

        session.StageClearOverride("Floor A");
        session.StageOverride("Floor B", "retail");
        IReadOnlyDictionary<string, string> saved = session.ApplyPendingChanges();

        Assert.False(session.HasPendingChanges);
        Assert.DoesNotContain("Floor A", saved.Keys);
        Assert.Equal("retail", saved["Floor B"]);
    }
}
