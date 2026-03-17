using System.Resources;

namespace RevitGeoExporter.Resources;

internal static class AppStrings
{
    private static readonly ResourceManager ResourceManagerInstance = new("RevitGeoExporter.Resources.AppStrings", typeof(AppStrings).Assembly);

    public static ResourceManager ResourceManager => ResourceManagerInstance;
}
