using System.Collections.Generic;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class StairTreadLineSelectorTests
{
    [Fact]
    public void TrySelectFrontEdge_PrefersFarthestPerpendicularEdge()
    {
        IReadOnlyList<StairTreadLineCandidate> candidates = new[]
        {
            new StairTreadLineCandidate(CreateLine(1.0d, 0.0d, 1.0d, 1.0d), 1.00d),
            new StairTreadLineCandidate(CreateLine(1.2d, 0.0d, 1.2d, 1.0d), 1.20d),
            new StairTreadLineCandidate(CreateLine(0.0d, 0.5d, 1.0d, 0.5d), 1.10d),
        };

        bool success = StairTreadLineSelector.TrySelectFrontEdge(
            candidates,
            new Point2D(1.0d, 0.0d),
            0.10d,
            out StairTreadLineCandidate selected);

        Assert.True(success);
        Assert.Equal(1.20d, selected.StationMeters, 10);
        Assert.Equal(1.2d, selected.Line.Points[0].X, 10);
        Assert.Equal(1.2d, selected.Line.Points[1].X, 10);
    }

    [Fact]
    public void TrySelectFrontEdge_AcceptsAngledWinderLikeEdge()
    {
        IReadOnlyList<StairTreadLineCandidate> candidates = new[]
        {
            new StairTreadLineCandidate(CreateLine(1.8d, 0.0d, 2.1d, 0.9d), 1.80d),
            new StairTreadLineCandidate(CreateLine(2.0d, 0.0d, 2.3d, 1.0d), 2.00d),
        };

        bool success = StairTreadLineSelector.TrySelectFrontEdge(
            candidates,
            new Point2D(1.0d, 0.0d),
            0.10d,
            out StairTreadLineCandidate selected);

        Assert.True(success);
        Assert.Equal(2.00d, selected.StationMeters, 10);
    }

    [Fact]
    public void SelectDistinctOrderedLines_MergesNearbyDuplicatesAndKeepsLongerLine()
    {
        IReadOnlyList<StairTreadLineCandidate> candidates = new[]
        {
            new StairTreadLineCandidate(CreateLine(1.0d, 0.0d, 1.0d, 0.8d), 1.00d),
            new StairTreadLineCandidate(CreateLine(1.0d, 0.0d, 1.0d, 1.1d), 1.03d),
            new StairTreadLineCandidate(CreateLine(2.0d, 0.0d, 2.0d, 0.9d), 2.00d),
        };

        IReadOnlyList<LineString2D> selected = StairTreadLineSelector.SelectDistinctOrderedLines(candidates, 0.05d);

        Assert.Equal(2, selected.Count);
        Assert.Equal(1.1d, selected[0].Points[1].Y, 10);
        Assert.Equal(2.0d, selected[1].Points[0].X, 10);
    }

    [Fact]
    public void SelectDistinctOrderedLines_DeduplicatesReversedExactMatches()
    {
        IReadOnlyList<StairTreadLineCandidate> candidates = new[]
        {
            new StairTreadLineCandidate(CreateLine(1.5d, 0.0d, 1.5d, 1.0d), 1.50d),
            new StairTreadLineCandidate(CreateLine(1.5d, 1.0d, 1.5d, 0.0d), 1.50d),
        };

        IReadOnlyList<LineString2D> selected = StairTreadLineSelector.SelectDistinctOrderedLines(candidates, 0.01d);

        Assert.Single(selected);
    }

    private static LineString2D CreateLine(double x1, double y1, double x2, double y2)
    {
        return new LineString2D(
            new[]
            {
                new Point2D(x1, y1),
                new Point2D(x2, y2),
            });
    }
}
