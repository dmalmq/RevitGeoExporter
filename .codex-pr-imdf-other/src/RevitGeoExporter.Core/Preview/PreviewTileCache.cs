using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RevitGeoExporter.Core.Preview;

public sealed class PreviewTileCache
{
    private static readonly TimeSpan DefaultFreshnessWindow = TimeSpan.FromDays(7);

    private readonly string _cacheRoot;
    private readonly TimeSpan _freshnessWindow;

    public PreviewTileCache(string cacheRoot, TimeSpan? freshnessWindow = null)
    {
        _cacheRoot = string.IsNullOrWhiteSpace(cacheRoot)
            ? throw new ArgumentException("A cache root path is required.", nameof(cacheRoot))
            : cacheRoot.Trim();
        _freshnessWindow = freshnessWindow ?? DefaultFreshnessWindow;
    }

    public string CacheRoot => _cacheRoot;

    public string GetProviderHash(string urlTemplate)
    {
        string normalizedTemplate = (urlTemplate ?? string.Empty).Trim();
        using SHA1 sha1 = SHA1.Create();
        byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(normalizedTemplate));
        StringBuilder builder = new(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            builder.Append(hash[i].ToString("x2"));
        }

        return builder.ToString();
    }

    public string GetTilePath(string urlTemplate, WebMercatorTile tile)
    {
        string providerHash = GetProviderHash(urlTemplate);
        return Path.Combine(_cacheRoot, providerHash, tile.Zoom.ToString(), tile.X.ToString(), $"{tile.Y}.png");
    }

    public PreviewTileCacheEntry GetEntry(string urlTemplate, WebMercatorTile tile, DateTimeOffset? now = null)
    {
        string path = GetTilePath(urlTemplate, tile);
        if (!File.Exists(path))
        {
            return new PreviewTileCacheEntry(path, exists: false, isFresh: false, DateTimeOffset.MinValue);
        }

        DateTimeOffset lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        bool isFresh = ((now ?? DateTimeOffset.UtcNow) - lastWriteTimeUtc) <= _freshnessWindow;
        return new PreviewTileCacheEntry(path, exists: true, isFresh, lastWriteTimeUtc);
    }

    public void SaveTileBytes(string path, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A tile path is required.", nameof(path));
        }

        if (bytes is null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{path}.tmp";
        File.WriteAllBytes(tempPath, bytes);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }
}
