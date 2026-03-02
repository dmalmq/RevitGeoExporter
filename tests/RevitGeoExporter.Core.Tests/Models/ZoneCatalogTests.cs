using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class ZoneCatalogTests
{
    [Fact]
    public void KnownZone_ReturnsCategoryColorAndRestriction()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        bool found = catalog.TryGetZoneInfo("ラチ内コンコース", out ZoneInfo info);

        Assert.True(found);
        Assert.Equal("walkway", info.Category);
        Assert.Equal("FEFEF2", info.FillColor);
        Assert.Equal("rachi_nai", info.Restriction);
    }

    [Fact]
    public void UnknownZone_ReturnsDefaultInfo()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        bool found = catalog.TryGetZoneInfo("Unknown Zone", out ZoneInfo info);

        Assert.False(found);
        Assert.Equal("unspecified", info.Category);
        Assert.Equal("CCCCCC", info.FillColor);
        Assert.Null(info.Restriction);
    }

    [Fact]
    public void RestroomCategories_AreMapped()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        Assert.Equal("restroom.male", catalog.GetZoneInfoOrDefault("男子トイレ").Category);
        Assert.Equal("restroom.female", catalog.GetZoneInfoOrDefault("女子トイレ").Category);
        Assert.Equal("restroom.unisex", catalog.GetZoneInfoOrDefault("多目的トイレ").Category);
    }

    [Fact]
    public void FamilyLookup_ElevatorAndEscalator_AreMapped()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        bool foundElevator = catalog.TryGetFamilyInfo("j EV", out ZoneInfo elevator);
        bool foundEscalator = catalog.TryGetFamilyInfo("j エスカレーター-lightweight", out ZoneInfo escalator);

        Assert.True(foundElevator);
        Assert.Equal("elevator", elevator.Category);
        Assert.True(foundEscalator);
        Assert.Equal("escalator", escalator.Category);
    }

    [Fact]
    public void StairsDefault_IsConfigured()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        Assert.Equal("stairs", catalog.StairsDefault.Category);
        Assert.Equal("C0C0C0", catalog.StairsDefault.FillColor);
    }
}
