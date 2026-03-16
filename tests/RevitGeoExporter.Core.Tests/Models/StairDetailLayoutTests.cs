using System.Collections.Generic;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class StairDetailLayoutTests
{
    [Fact]
    public void BuildCenteredLinePositions_UsesFixedSpacingAndCentersRemainder()
    {
        IReadOnlyList<double> positions = StairDetailLayout.BuildCenteredLinePositions(4.5d, 0.6d);

        double[] expected = { 0.45d, 1.05d, 1.65d, 2.25d, 2.85d, 3.45d, 4.05d };
        Assert.Equal(expected.Length, positions.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], positions[i], 10);
        }
    }

    [Fact]
    public void BuildCenteredLinePositions_ReturnsSingleCenteredLineForShortRun()
    {
        IReadOnlyList<double> positions = StairDetailLayout.BuildCenteredLinePositions(1.0d, 0.6d);

        Assert.Single(positions);
        Assert.Equal(0.5d, positions[0]);
    }
}
