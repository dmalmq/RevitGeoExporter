using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class PreviewBasemapSettingsTests
{
    [Fact]
    public void NullTemplate_UsesDefaultProvider()
    {
        PreviewBasemapSettings settings = new(null, null);

        Assert.Equal(PreviewBasemapSettings.DefaultUrlTemplate, settings.UrlTemplate);
        Assert.Equal(PreviewBasemapSettings.DefaultAttribution, settings.Attribution);
        Assert.True(settings.IsConfigured);
    }

    [Theory]
    [InlineData("")]
    [InlineData("offline")]
    [InlineData("none")]
    public void EmptyOrOfflineTemplate_DisablesBasemap(string template)
    {
        PreviewBasemapSettings settings = new(template, string.Empty);

        Assert.Equal(string.Empty, settings.UrlTemplate);
        Assert.False(settings.IsConfigured);
    }
}
