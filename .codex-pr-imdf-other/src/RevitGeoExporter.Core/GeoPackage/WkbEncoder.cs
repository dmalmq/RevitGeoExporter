using System;
using System.Collections.Generic;
using System.IO;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.GeoPackage;

public static class WkbEncoder
{
    private const byte LittleEndianByteOrder = 1;
    private const uint LineStringWkbType = 2;
    private const uint PolygonWkbType = 3;
    private const uint MultiPolygonWkbType = 6;

    public static byte[] EncodeLineString(LineString2D lineString)
    {
        if (lineString is null)
        {
            throw new ArgumentNullException(nameof(lineString));
        }

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        WriteLineString(writer, lineString);
        return stream.ToArray();
    }

    public static byte[] EncodePolygon(Polygon2D polygon)
    {
        if (polygon is null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        WritePolygon(writer, polygon);
        return stream.ToArray();
    }

    public static byte[] EncodeMultiPolygon(IReadOnlyList<Polygon2D> polygons)
    {
        if (polygons is null)
        {
            throw new ArgumentNullException(nameof(polygons));
        }

        if (polygons.Count == 0)
        {
            throw new ArgumentException("At least one polygon is required.", nameof(polygons));
        }

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(LittleEndianByteOrder);
        writer.Write(MultiPolygonWkbType);
        writer.Write((uint)polygons.Count);

        for (int i = 0; i < polygons.Count; i++)
        {
            WritePolygon(writer, polygons[i]);
        }

        return stream.ToArray();
    }

    public static byte[] WrapInGeoPackageHeader(byte[] wkbPayload, int srsId)
    {
        if (wkbPayload is null)
        {
            throw new ArgumentNullException(nameof(wkbPayload));
        }

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        // GeoPackage header:
        // magic (2), version (1), flags (1), srs_id (4), WKB payload (n)
        writer.Write((byte)'G');
        writer.Write((byte)'P');
        writer.Write((byte)0); // version
        writer.Write((byte)1); // little-endian, standard, non-empty, no envelope
        writer.Write(srsId);
        writer.Write(wkbPayload);

        return stream.ToArray();
    }

    private static void WritePolygon(BinaryWriter writer, Polygon2D polygon)
    {
        writer.Write(LittleEndianByteOrder);
        writer.Write(PolygonWkbType);

        uint ringCount = (uint)(1 + polygon.InteriorRings.Count);
        writer.Write(ringCount);
        WriteRing(writer, polygon.ExteriorRing);

        for (int i = 0; i < polygon.InteriorRings.Count; i++)
        {
            WriteRing(writer, polygon.InteriorRings[i]);
        }
    }

    private static void WriteRing(BinaryWriter writer, IReadOnlyList<Point2D> ring)
    {
        writer.Write((uint)ring.Count);
        for (int i = 0; i < ring.Count; i++)
        {
            writer.Write(ring[i].X);
            writer.Write(ring[i].Y);
        }
    }

    private static void WriteLineString(BinaryWriter writer, LineString2D lineString)
    {
        writer.Write(LittleEndianByteOrder);
        writer.Write(LineStringWkbType);
        writer.Write((uint)lineString.Points.Count);
        for (int i = 0; i < lineString.Points.Count; i++)
        {
            writer.Write(lineString.Points[i].X);
            writer.Write(lineString.Points[i].Y);
        }
    }
}
