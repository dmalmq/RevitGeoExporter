using System.Collections.Generic;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Assignments;

public sealed class FloorCategoryResolverTests
{
    [Fact]
    public void Resolve_UsesCatalogMatch_WhenOverrideDoesNotExist()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();
        FloorCategoryResolver resolver = new(catalog);

        ResolvedFloorCategory resolved = resolver.Resolve(
            "j Retail Outside_gabc",
            "Retail (outside fare gates)");

        Assert.False(resolved.IsUnassigned);
        Assert.Equal(FloorCategoryResolutionSource.Catalog, resolved.ResolutionSource);
        Assert.Equal("retail", resolved.ZoneInfo.Category);
    }

    [Fact]
    public void Resolve_UsesOverride_WhenRawFloorTypeHasSavedCategory()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();
        FloorCategoryResolver resolver = new(
            catalog,
            new Dictionary<string, string>
            {
                ["Bad Floor Name"] = "nonpublic",
            });

        ResolvedFloorCategory resolved = resolver.Resolve("Bad Floor Name", "Bad Floor Name");

        Assert.False(resolved.IsUnassigned);
        Assert.Equal(FloorCategoryResolutionSource.Override, resolved.ResolutionSource);
        Assert.Equal("nonpublic", resolved.ZoneInfo.Category);
    }

    [Fact]
    public void Resolve_FallsBackToUnspecified_WhenNoCatalogMatchOrOverrideExists()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();
        FloorCategoryResolver resolver = new(catalog);

        ResolvedFloorCategory resolved = resolver.Resolve("Bad Floor Name", "Bad Floor Name");

        Assert.True(resolved.IsUnassigned);
        Assert.Equal(FloorCategoryResolutionSource.FallbackUnspecified, resolved.ResolutionSource);
        Assert.Equal("unspecified", resolved.ZoneInfo.Category);
    }

    [Fact]
    public void Resolve_WrongFloorName_BecomesResolvedAfterOverride()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();
        FloorCategoryResolver withoutOverride = new(catalog);
        FloorCategoryResolver withOverride = new(
            catalog,
            new Dictionary<string, string>
            {
                ["Wrong Floor Name"] = "walkway",
            });

        ResolvedFloorCategory unresolved = withoutOverride.Resolve("Wrong Floor Name", "Wrong Floor Name");
        ResolvedFloorCategory resolved = withOverride.Resolve("Wrong Floor Name", "Wrong Floor Name");

        Assert.True(unresolved.IsUnassigned);
        Assert.False(resolved.IsUnassigned);
        Assert.Equal(FloorCategoryResolutionSource.Override, resolved.ResolutionSource);
        Assert.Equal("walkway", resolved.ZoneInfo.Category);
    }
}
