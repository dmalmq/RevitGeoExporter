using System;
using System.IO;
using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class PreviewTileCacheTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), "RevitGeoExporter.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetProviderHash_IsDeterministic()
    {
        PreviewTileCache cache = new(_cacheRoot);

        string first = cache.GetProviderHash("https://tile.openstreetmap.org/{z}/{x}/{y}.png");
        string second = cache.GetProviderHash("https://tile.openstreetmap.org/{z}/{x}/{y}.png");
        string third = cache.GetProviderHash("https://example.com/{z}/{x}/{y}.png");

        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
    }

    [Fact]
    public void GetEntry_MissingTile_ReturnsNonexistentEntry()
    {
        PreviewTileCache cache = new(_cacheRoot);
        WebMercatorTile tile = new(3, 4, 5, new Bounds2D(0d, 0d, 1d, 1d));

        PreviewTileCacheEntry entry = cache.GetEntry("https://tile.openstreetmap.org/{z}/{x}/{y}.png", tile);

        Assert.False(entry.Exists);
        Assert.False(entry.IsFresh);
        Assert.EndsWith(Path.Combine("3", "4", "5.png"), entry.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEntry_UsesFreshnessWindowFromLastWriteTime()
    {
        PreviewTileCache cache = new(_cacheRoot, TimeSpan.FromDays(7));
        WebMercatorTile tile = new(3, 4, 5, new Bounds2D(0d, 0d, 1d, 1d));
        string path = cache.GetTilePath("https://tile.openstreetmap.org/{z}/{x}/{y}.png", tile);
        cache.SaveTileBytes(path, new byte[] { 1, 2, 3, 4 });

        DateTimeOffset now = new(2026, 3, 12, 12, 0, 0, TimeSpan.Zero);

        File.SetLastWriteTimeUtc(path, now.AddDays(-2).UtcDateTime);
        PreviewTileCacheEntry freshEntry = cache.GetEntry("https://tile.openstreetmap.org/{z}/{x}/{y}.png", tile, now);

        File.SetLastWriteTimeUtc(path, now.AddDays(-10).UtcDateTime);
        PreviewTileCacheEntry staleEntry = cache.GetEntry("https://tile.openstreetmap.org/{z}/{x}/{y}.png", tile, now);

        Assert.True(freshEntry.Exists);
        Assert.True(freshEntry.IsFresh);
        Assert.True(staleEntry.Exists);
        Assert.False(staleEntry.IsFresh);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }
}