namespace RevitGeoExporter.Core.Preview;

public readonly struct WebMercatorTile
{
    public WebMercatorTile(int zoom, int x, int y, Bounds2D bounds)
    {
        Zoom = zoom;
        X = x;
        Y = y;
        Bounds = bounds;
    }

    public int Zoom { get; }

    public int X { get; }

    public int Y { get; }

    public Bounds2D Bounds { get; }

    public string CacheKey => $"{Zoom}/{X}/{Y}";
}
