using System.Linq;
using RevitGeoExporter.Core.Schema;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Schema;

public sealed class SchemaProfileTests
{
    [Fact]
    public void NormalizeProfiles_AddsCoreProfileWhenMissing()
    {
        var profiles = SchemaProfile.NormalizeProfiles(null);

        SchemaProfile profile = Assert.Single(profiles);
        Assert.Equal(SchemaProfile.CoreProfileName, profile.Name);
        Assert.Empty(profile.Mappings);
    }

    [Fact]
    public void ResolveActive_FallsBackToFirstNormalizedProfile()
    {
        SchemaProfile resolved = SchemaProfile.ResolveActive(
            new[]
            {
                new SchemaProfile { Name = " Client B " },
                new SchemaProfile { Name = "client a" },
            },
            activeProfileName: "missing");

        Assert.Equal("client a", resolved.Name, ignoreCase: true);
    }

    [Fact]
    public void Clone_CopiesMappings()
    {
        SchemaProfile original = new()
        {
            Name = "Client",
            Mappings =
            {
                new CustomAttributeMapping
                {
                    FieldName = "client_name",
                    SourceKey = "ExportId",
                },
            },
        };

        SchemaProfile clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.NotSame(original.Mappings, clone.Mappings);
        Assert.Single(clone.Mappings);
        Assert.Equal("client_name", clone.Mappings.Single().FieldName);
    }
}
