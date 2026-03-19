using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;

namespace RevitGeoExporter.UI;

internal sealed class PreviewTileProvider : IDisposable
{
    private readonly PreviewTileCache _cache;
    private readonly Action _invalidateRequested;
    private readonly SemaphoreSlim _downloadSemaphore = new(4, 4);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedTileBitmap> _memoryCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
    private string _statusMessage = string.Empty;
    private bool _disposed;

    public PreviewTileProvider(Action invalidateRequested)
    {
        _invalidateRequested = invalidateRequested ?? throw new ArgumentNullException(nameof(invalidateRequested));
        string cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProjectInfo.Name,
            "TileCache");
        _cache = new PreviewTileCache(cacheRoot);
    }

    public event Action<string?>? StatusMessageChanged;

    public void ClearStatus()
    {
        UpdateStatus(string.Empty);
    }

    public void DrawTiles(Graphics graphics, ViewTransform2D transform, PreviewBasemapSettings settings)
    {
        if (graphics is null)
        {
            throw new ArgumentNullException(nameof(graphics));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (!settings.IsConfigured)
        {
            return;
        }

        Bounds2D visibleBounds = GetVisibleWorldBounds(transform);
        if (visibleBounds.IsEmpty)
        {
            return;
        }

        int zoom = WebMercatorTileCalculator.GetZoomLevel(transform.PixelsPerWorldUnit);
        IReadOnlyList<WebMercatorTile> tiles = WebMercatorTileCalculator.GetVisibleTiles(visibleBounds, zoom);
        for (int i = 0; i < tiles.Count; i++)
        {
            WebMercatorTile tile = tiles[i];
            PreviewTileCacheEntry entry = _cache.GetEntry(settings.UrlTemplate, tile);
            bool hasUsableFallback = false;
            if (entry.Exists && TryLoadBitmap(settings.UrlTemplate, tile, entry, out Bitmap? bitmap))
            {
                DrawTile(graphics, transform, tile, bitmap!);
                hasUsableFallback = true;
            }

            if (!entry.Exists || !entry.IsFresh)
            {
                ScheduleDownload(settings, tile, hasUsableFallback);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _downloadSemaphore.Dispose();
        _cancellation.Dispose();

        lock (_sync)
        {
            foreach (CachedTileBitmap cached in _memoryCache.Values)
            {
                cached.Bitmap.Dispose();
            }

            _memoryCache.Clear();
            _inFlight.Clear();
        }
    }

    private bool TryLoadBitmap(string urlTemplate, WebMercatorTile tile, PreviewTileCacheEntry entry, out Bitmap? bitmap)
    {
        string key = BuildCacheKey(urlTemplate, tile);
        lock (_sync)
        {
            if (_memoryCache.TryGetValue(key, out CachedTileBitmap? cached) &&
                cached.LastWriteTimeUtc == entry.LastWriteTimeUtc)
            {
                bitmap = cached.Bitmap;
                return true;
            }
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(entry.Path);
            using MemoryStream stream = new(bytes);
            using Image image = Image.FromStream(stream);
            Bitmap loadedBitmap = new(image);

            lock (_sync)
            {
                if (_memoryCache.TryGetValue(key, out CachedTileBitmap? existing))
                {
                    existing.Bitmap.Dispose();
                }

                _memoryCache[key] = new CachedTileBitmap(loadedBitmap, entry.LastWriteTimeUtc);
            }

            bitmap = loadedBitmap;
            return true;
        }
        catch
        {
            bitmap = null;
            return false;
        }
    }

    private void ScheduleDownload(PreviewBasemapSettings settings, WebMercatorTile tile, bool hasUsableFallback)
    {
        if (_disposed)
        {
            return;
        }

        string cacheKey = BuildCacheKey(settings.UrlTemplate, tile);
        lock (_sync)
        {
            if (_inFlight.Contains(cacheKey))
            {
                return;
            }

            _inFlight.Add(cacheKey);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _downloadSemaphore.WaitAsync(_cancellation.Token).ConfigureAwait(false);
                try
                {
                    byte[] bytes = await DownloadTileBytesAsync(settings.UrlTemplate, tile, _cancellation.Token).ConfigureAwait(false);
                    _cache.SaveTileBytes(_cache.GetTilePath(settings.UrlTemplate, tile), bytes);
                    UpdateStatus(string.Empty);
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }
            catch when (_disposed || _cancellation.IsCancellationRequested)
            {
                // Ignore cancellation during shutdown.
            }
            catch
            {
                if (!hasUsableFallback)
                {
                    UpdateStatus("Basemap loading failed; showing model only.");
                }
            }
            finally
            {
                lock (_sync)
                {
                    _inFlight.Remove(cacheKey);
                }

                RequestInvalidate();
            }
        }, _cancellation.Token);
    }

    private static async Task<byte[]> DownloadTileBytesAsync(string urlTemplate, WebMercatorTile tile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        HttpWebRequest request = WebRequest.CreateHttp(ResolveTileUrl(urlTemplate, tile));
        request.Method = WebRequestMethods.Http.Get;
        request.UserAgent = $"{ProjectInfo.Name}/{ProjectInfo.DisplayVersion}";
        request.Timeout = 15000;
        request.ReadWriteTimeout = 15000;

        using WebResponse response = await request.GetResponseAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        using Stream responseStream = response.GetResponseStream() ?? throw new InvalidOperationException("Tile response stream was empty.");
        using MemoryStream memoryStream = new();
        await responseStream.CopyToAsync(memoryStream, 81920, cancellationToken).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    private void RequestInvalidate()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _invalidateRequested();
        }
        catch
        {
            // Ignore redraw requests that race disposal.
        }
    }

    private void UpdateStatus(string message)
    {
        string normalized = (message ?? string.Empty).Trim();
        if (string.Equals(_statusMessage, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _statusMessage = normalized;
        StatusMessageChanged?.Invoke(_statusMessage.Length == 0 ? null : _statusMessage);
    }

    private static void DrawTile(Graphics graphics, ViewTransform2D transform, WebMercatorTile tile, Bitmap bitmap)
    {
        Point2D topLeft = transform.WorldToScreen(new Point2D(tile.Bounds.MinX, tile.Bounds.MaxY));
        Point2D bottomRight = transform.WorldToScreen(new Point2D(tile.Bounds.MaxX, tile.Bounds.MinY));
        RectangleF destination = RectangleF.FromLTRB(
            (float)topLeft.X,
            (float)topLeft.Y,
            (float)bottomRight.X,
            (float)bottomRight.Y);
        if (destination.Width <= 0f || destination.Height <= 0f)
        {
            return;
        }

        graphics.DrawImage(bitmap, destination);
    }

    private static Bounds2D GetVisibleWorldBounds(ViewTransform2D transform)
    {
        Point2D topLeft = transform.ScreenToWorld(new Point2D(0d, 0d));
        Point2D bottomRight = transform.ScreenToWorld(new Point2D(transform.ViewportWidth, transform.ViewportHeight));
        double minX = Math.Min(topLeft.X, bottomRight.X);
        double minY = Math.Min(bottomRight.Y, topLeft.Y);
        double maxX = Math.Max(topLeft.X, bottomRight.X);
        double maxY = Math.Max(bottomRight.Y, topLeft.Y);
        return new Bounds2D(minX, minY, maxX, maxY);
    }

    private static string ResolveTileUrl(string urlTemplate, WebMercatorTile tile)
    {
        return (urlTemplate ?? string.Empty)
            .Replace("{z}", tile.Zoom.ToString())
            .Replace("{x}", tile.X.ToString())
            .Replace("{y}", tile.Y.ToString());
    }

    private static string BuildCacheKey(string urlTemplate, WebMercatorTile tile)
    {
        return $"{urlTemplate}|{tile.CacheKey}";
    }

    private sealed class CachedTileBitmap
    {
        public CachedTileBitmap(Bitmap bitmap, DateTimeOffset lastWriteTimeUtc)
        {
            Bitmap = bitmap;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }

        public Bitmap Bitmap { get; }

        public DateTimeOffset LastWriteTimeUtc { get; }
    }
}
