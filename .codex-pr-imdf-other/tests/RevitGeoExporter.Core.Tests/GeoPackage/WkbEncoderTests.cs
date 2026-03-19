using System.IO;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.GeoPackage;

public sealed class WkbEncoderTests
{
    private const double Tolerance = 0.000001d;

    [Fact]
    public void SimpleSquare_EncodesPolygon()
    {
        Polygon2D polygon = new(
            new[]
            {
                new Point2D(0, 0),
                new Point2D(10, 0),
                new Point2D(10, 10),
                new Point2D(0, 10),
            });

        byte[] wkb = WkbEncoder.EncodePolygon(polygon);

        using MemoryStream stream = new(wkb);
        using BinaryReader reader = new(stream);

        Assert.Equal(1, reader.ReadByte());
        Assert.Equal((uint)3, reader.ReadUInt32());
        Assert.Equal((uint)1, reader.ReadUInt32());
        Assert.Equal((uint)5, reader.ReadUInt32());

        double x0 = reader.ReadDouble();
        double y0 = reader.ReadDouble();
        Assert.InRange(x0, -Tolerance, Tolerance);
        Assert.InRange(y0, -Tolerance, Tolerance);
    }

    [Fact]
    public void PolygonWithHole_EncodesRingCount()
    {
        Polygon2D polygon = new(
            exteriorRing: new[]
            {
                new Point2D(0, 0),
                new Point2D(20, 0),
                new Point2D(20, 20),
                new Point2D(0, 20),
            },
            interiorRings: new[]
            {
                new[]
                {
                    new Point2D(5, 5),
                    new Point2D(15, 5),
                    new Point2D(15, 15),
                    new Point2D(5, 15),
                },
            });

        byte[] wkb = WkbEncoder.EncodePolygon(polygon);
        using MemoryStream stream = new(wkb);
        using BinaryReader reader = new(stream);

        _ = reader.ReadByte();     // byte order
        _ = reader.ReadUInt32();   // type
        uint ringCount = reader.ReadUInt32();
        Assert.Equal((uint)2, ringCount);
    }

    [Fact]
    public void GpkgHeader_IsPrependedCorrectly()
    {
        Polygon2D polygon = new(
            new[]
            {
                new Point2D(0, 0),
                new Point2D(1, 0),
                new Point2D(1, 1),
                new Point2D(0, 1),
            });
        byte[] wkb = WkbEncoder.EncodePolygon(polygon);

        byte[] gpkg = WkbEncoder.WrapInGeoPackageHeader(wkb, 6677);

        Assert.Equal((byte)'G', gpkg[0]);
        Assert.Equal((byte)'P', gpkg[1]);
        Assert.Equal((byte)0, gpkg[2]);
        Assert.Equal((byte)1, gpkg[3]);
        Assert.Equal(6677, System.BitConverter.ToInt32(gpkg, 4));
        Assert.Equal((byte)1, gpkg[8]); // byte order at start of WKB payload
    }

    [Fact]
    public void LineString_EncodesCoordinateSequence()
    {
        LineString2D line = new(
            new[]
            {
                new Point2D(0, 0),
                new Point2D(2, 3),
                new Point2D(4, 5),
            });

        byte[] wkb = WkbEncoder.EncodeLineString(line);
        using MemoryStream stream = new(wkb);
        using BinaryReader reader = new(stream);

        Assert.Equal(1, reader.ReadByte());      // little-endian
        Assert.Equal((uint)2, reader.ReadUInt32()); // LineString
        Assert.Equal((uint)3, reader.ReadUInt32()); // point count

        Assert.Equal(0d, reader.ReadDouble());
        Assert.Equal(0d, reader.ReadDouble());
        Assert.Equal(2d, reader.ReadDouble());
        Assert.Equal(3d, reader.ReadDouble());
        Assert.Equal(4d, reader.ReadDouble());
        Assert.Equal(5d, reader.ReadDouble());
    }
}
