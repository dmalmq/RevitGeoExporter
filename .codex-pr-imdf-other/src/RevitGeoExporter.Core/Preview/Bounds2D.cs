using System;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Preview;

public readonly struct Bounds2D
{
    private readonly bool _hasValue;

    public Bounds2D(double minX, double minY, double maxX, double maxY)
    {
        if (double.IsNaN(minX) ||
            double.IsNaN(minY) ||
            double.IsNaN(maxX) ||
            double.IsNaN(maxY))
        {
            throw new ArgumentException("Bounds values must be finite.");
        }

        if (maxX < minX)
        {
            throw new ArgumentOutOfRangeException(nameof(maxX), "maxX must be greater than or equal to minX.");
        }

        if (maxY < minY)
        {
            throw new ArgumentOutOfRangeException(nameof(maxY), "maxY must be greater than or equal to minY.");
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        _hasValue = true;
    }

    public static Bounds2D Empty => default;

    public double MinX { get; }

    public double MinY { get; }

    public double MaxX { get; }

    public double MaxY { get; }

    public bool IsEmpty => !_hasValue;

    public double Width => IsEmpty ? 0d : MaxX - MinX;

    public double Height => IsEmpty ? 0d : MaxY - MinY;

    public Point2D Center => IsEmpty
        ? new Point2D(0d, 0d)
        : new Point2D((MinX + MaxX) * 0.5d, (MinY + MaxY) * 0.5d);

    public Bounds2D Inflate(double paddingX, double paddingY)
    {
        if (IsEmpty)
        {
            return this;
        }

        double safePaddingX = Math.Max(0d, paddingX);
        double safePaddingY = Math.Max(0d, paddingY);
        return new Bounds2D(
            MinX - safePaddingX,
            MinY - safePaddingY,
            MaxX + safePaddingX,
            MaxY + safePaddingY);
    }

    public Bounds2D Union(Bounds2D other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return new Bounds2D(
            Math.Min(MinX, other.MinX),
            Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX),
            Math.Max(MaxY, other.MaxY));
    }
}
