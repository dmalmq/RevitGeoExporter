using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class OpeningSnapPolicyTests
{
    [Theory]
    [InlineData(true, false, OpeningSnapPolicy.ElevatorOpeningSnapDistanceMeters)]
    [InlineData(false, true, OpeningSnapPolicy.ElevatorOpeningSnapDistanceMeters)]
    [InlineData(true, true, OpeningSnapPolicy.ElevatorOpeningSnapDistanceMeters)]
    [InlineData(false, false, OpeningSnapPolicy.DefaultOpeningSnapDistanceMeters)]
    public void ResolveMaxSnapDistance_UsesElevatorToleranceWhenNeeded(
        bool isAcceptedElevatorDoorFamily,
        bool isNearElevatorBoundary,
        double expected)
    {
        double resolved = OpeningSnapPolicy.ResolveMaxSnapDistance(
            isAcceptedElevatorDoorFamily,
            isNearElevatorBoundary);

        Assert.Equal(expected, resolved);
    }
}
