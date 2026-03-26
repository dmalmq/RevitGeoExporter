using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class ImdfRestrictionNormalizerTests
{
    [Theory]
    [InlineData("rachi_gai")]
    [InlineData("rachi_nai")]
    [InlineData("rachigai")]
    [InlineData("rachinai")]
    [InlineData("RACHI_GAI")]
    [InlineData("rachi-nai")]
    public void NormalizeUnitRestriction_RemovesLegacyFareGateValues(string restriction)
    {
        Assert.Null(ImdfRestrictionNormalizer.NormalizeUnitRestriction(restriction));
    }

    [Fact]
    public void NormalizeUnitRestriction_PreservesOtherValues()
    {
        Assert.Equal("employeesonly", ImdfRestrictionNormalizer.NormalizeUnitRestriction(" employeesonly "));
    }
}
