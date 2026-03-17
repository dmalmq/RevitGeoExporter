using System;
using RevitGeoExporter.Core.Utilities;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class DeterministicIdGeneratorTests
{
    [Fact]
    public void CreateGuid_ReturnsStableGuidForSameSeed()
    {
        string first = DeterministicIdGenerator.CreateGuid("preview", "123");
        string second = DeterministicIdGenerator.CreateGuid("preview", "123");

        Assert.Equal(first, second);
        Assert.True(Guid.TryParse(first, out _));
    }

    [Fact]
    public void CreateGuid_ReturnsDifferentGuidForDifferentSeed()
    {
        string first = DeterministicIdGenerator.CreateGuid("preview", "123");
        string second = DeterministicIdGenerator.CreateGuid("preview", "124");

        Assert.NotEqual(first, second);
    }
}
