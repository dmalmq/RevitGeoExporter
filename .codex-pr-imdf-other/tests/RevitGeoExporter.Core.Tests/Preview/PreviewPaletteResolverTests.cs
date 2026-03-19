using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class PreviewPaletteResolverTests
{
    [Fact]
    public void ResolveFillColor_UsesDefaultOverrideForKnownCategory()
    {
        PreviewPaletteResolver resolver = new();

        string resolved = resolver.ResolveFillColor("nonpublic", "CCCCCC");

        Assert.Equal("979797", resolved);
    }

    [Fact]
    public void ResolveFillColor_FallsBackWhenCategoryIsUnknown()
    {
        PreviewPaletteResolver resolver = new();

        string resolved = resolver.ResolveFillColor("platform", "ABCDEF");

        Assert.Equal("ABCDEF", resolved);
    }
}
