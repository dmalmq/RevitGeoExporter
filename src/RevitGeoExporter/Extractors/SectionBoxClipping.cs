using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitGeoExporter.Extractors;

internal static class SectionBoxClipping
{
    private const double ZEpsilon = 1e-9d;

    public readonly struct ZRange
    {
        public ZRange(double min, double max)
        {
            Min = min;
            Max = max;
        }

        public double Min { get; }
        public double Max { get; }
    }

    public static ZRange? TryGetZRange(View? view)
    {
        if (view is not View3D view3D || !view3D.IsSectionBoxActive)
        {
            return null;
        }

        BoundingBoxXYZ? box;
        try
        {
            box = view3D.GetSectionBox();
        }
        catch (Exception)
        {
            return null;
        }

        if (box == null)
        {
            return null;
        }

        Transform t = box.Transform ?? Transform.Identity;
        XYZ[] corners =
        {
            t.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Max.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Max.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Max.Z)),
            t.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Max.Z)),
        };

        double zMin = double.MaxValue;
        double zMax = double.MinValue;
        foreach (XYZ corner in corners)
        {
            if (corner.Z < zMin) zMin = corner.Z;
            if (corner.Z > zMax) zMax = corner.Z;
        }

        if (zMax - zMin <= ZEpsilon)
        {
            return null;
        }

        return new ZRange(zMin, zMax);
    }

    public static List<XYZ[]> ClipTriangleToZRange(XYZ a, XYZ b, XYZ c, ZRange range)
    {
        List<XYZ> step1 = ClipPolygonByZPlane(new List<XYZ> { a, b, c }, range.Min, keepAbove: true);
        if (step1.Count < 3)
        {
            return new List<XYZ[]>();
        }

        List<XYZ> step2 = ClipPolygonByZPlane(step1, range.Max, keepAbove: false);
        if (step2.Count < 3)
        {
            return new List<XYZ[]>();
        }

        List<XYZ[]> result = new(step2.Count - 2);
        for (int i = 1; i < step2.Count - 1; i++)
        {
            result.Add(new[] { step2[0], step2[i], step2[i + 1] });
        }
        return result;
    }

    private static List<XYZ> ClipPolygonByZPlane(List<XYZ> input, double zPlane, bool keepAbove)
    {
        List<XYZ> output = new(input.Count + 2);
        int n = input.Count;
        if (n == 0)
        {
            return output;
        }

        for (int i = 0; i < n; i++)
        {
            XYZ current = input[i];
            XYZ next = input[(i + 1) % n];
            bool currentInside = IsInside(current, zPlane, keepAbove);
            bool nextInside = IsInside(next, zPlane, keepAbove);

            if (currentInside)
            {
                output.Add(current);
                if (!nextInside)
                {
                    XYZ? intersect = TryInterpolateAtZ(current, next, zPlane);
                    if (intersect != null)
                    {
                        output.Add(intersect);
                    }
                }
            }
            else if (nextInside)
            {
                XYZ? intersect = TryInterpolateAtZ(current, next, zPlane);
                if (intersect != null)
                {
                    output.Add(intersect);
                }
            }
        }

        return output;
    }

    private static bool IsInside(XYZ point, double zPlane, bool keepAbove)
    {
        return keepAbove ? point.Z >= zPlane - ZEpsilon : point.Z <= zPlane + ZEpsilon;
    }

    private static XYZ? TryInterpolateAtZ(XYZ a, XYZ b, double zPlane)
    {
        double dz = b.Z - a.Z;
        if (Math.Abs(dz) < ZEpsilon)
        {
            return null;
        }

        double t = (zPlane - a.Z) / dz;
        if (t < 0d) t = 0d;
        else if (t > 1d) t = 1d;
        return new XYZ(
            a.X + (t * (b.X - a.X)),
            a.Y + (t * (b.Y - a.Y)),
            zPlane);
    }
}
