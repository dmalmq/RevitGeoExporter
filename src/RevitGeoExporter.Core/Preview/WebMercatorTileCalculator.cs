using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Preview;

public static class WebMercatorTileCalculator
{
    public const int TileSizePixels = 256;
    public const int MinZoom = 0;
    public const int MaxZoom = 19;
    public const int DefaultPaddingTiles = 1;

    private const double EarthRadiusMeters = 6378137d;
    public const double MaxExtent = Math.PI * EarthRadiusMeters;
    private const double InitialResolution = (MaxExtent * 2d) / TileSizePixels;

    public static int GetZoomLevel(double pixelsPerMeter)
    {
        if (double.IsNaN(pixelsPerMeter) || double.IsInfinity(pixelsPerMeter) || pixelsPerMeter <= 0d)
        {
            return MinZoom;
        }

        double metersPerPixel = 1d / pixelsPerMeter;
        double rawZoom = Math.Log(InitialResolution / metersPerPixel, 2d);
        int roundedZoom = (int)Math.Round(rawZoom, MidpointRounding.AwayFromZero);
        return Math.Max(MinZoom, Math.Min(MaxZoom, roundedZoom));
    }

    public static IReadOnlyList<WebMercatorTile> GetVisibleTiles(Bounds2D worldBounds, int zoom, int paddingTiles = DefaultPaddingTiles)
    {
        if (worldBounds.IsEmpty)
        {
            return Array.Empty<WebMercatorTile>();
        }

        int clampedZoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
        int clampedPadding = Math.Max(0, paddingTiles);
        int tilesPerAxis = 1 << clampedZoom;

        int minTileX = ClampTileIndex(WorldXToTileX(worldBounds.MinX, clampedZoom) - clampedPadding, tilesPerAxis);
        int maxTileX = ClampTileIndex(WorldXToTileX(worldBounds.MaxX, clampedZoom) + clampedPadding, tilesPerAxis);
        int minTileY = ClampTileIndex(WorldYToTileY(worldBounds.MaxY, clampedZoom) - clampedPadding, tilesPerAxis);
        int maxTileY = ClampTileIndex(WorldYToTileY(worldBounds.MinY, clampedZoom) + clampedPadding, tilesPerAxis);

        List<WebMercatorTile> tiles = new((maxTileX - minTileX + 1) * (maxTileY - minTileY + 1));
        for (int tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                tiles.Add(new WebMercatorTile(clampedZoom, tileX, tileY, GetTileBounds(tileX, tileY, clampedZoom)));
            }
        }

        return tiles;
    }

    public static Bounds2D GetTileBounds(int tileX, int tileY, int zoom)
    {
        int clampedZoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
        int tilesPerAxis = 1 << clampedZoom;
        double worldSpan = MaxExtent * 2d;
        double tileSpan = worldSpan / tilesPerAxis;

        double minX = (-MaxExtent) + (tileX * tileSpan);
        double maxX = minX + tileSpan;
        double maxY = MaxExtent - (tileY * tileSpan);
        double minY = maxY - tileSpan;
        return new Bounds2D(minX, minY, maxX, maxY);
    }

    private static int WorldXToTileX(double worldX, int zoom)
    {
        double clamped = Math.Max(-MaxExtent, Math.Min(MaxExtent, worldX));
        double normalized = (clamped + MaxExtent) / (MaxExtent * 2d);
        return (int)Math.Floor(normalized * (1 << zoom));
    }

    private static int WorldYToTileY(double worldY, int zoom)
    {
        double clamped = Math.Max(-MaxExtent, Math.Min(MaxExtent, worldY));
        double normalized = (MaxExtent - clamped) / (MaxExtent * 2d);
        return (int)Math.Floor(normalized * (1 << zoom));
    }

    private static int ClampTileIndex(int tileIndex, int tilesPerAxis)
    {
        return Math.Max(0, Math.Min(tilesPerAxis - 1, tileIndex));
    }
}
