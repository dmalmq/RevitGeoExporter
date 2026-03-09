using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class OpeningFamilyRulesTests
{
    [Theory]
    [InlineData("EV扉")]
    [InlineData("ev扉")]
    public void IsAcceptedElevatorDoorFamilyName_ReturnsTrueForConfiguredFamilies(string familyName)
    {
        Assert.True(OpeningFamilyRules.IsAcceptedElevatorDoorFamilyName(familyName));
    }

    [Fact]
    public void IsAcceptedElevatorDoorFamilyName_ReturnsFalseForOtherFamilies()
    {
        Assert.False(OpeningFamilyRules.IsAcceptedElevatorDoorFamilyName("Generic Door"));
    }
}
