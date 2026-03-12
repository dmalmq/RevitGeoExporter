using System;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core;

internal sealed class SharedCoordinateProjector
{
    private static readonly AffineTransform2D IdentityMetersTransform = new(
        new Point2D(0d, 0d),
        new Point2D(CrsTransformer.FeetToMetersFactor, 0d),
        new Point2D(0d, CrsTransformer.FeetToMetersFactor));

    private readonly AffineTransform2D _transform;

    public SharedCoordinateProjector(ProjectLocation? projectLocation)
    {
        _transform = projectLocation == null
            ? IdentityMetersTransform
            : CreateTransform(projectLocation);
    }

    public Point2D ProjectPoint(XYZ point)
    {
        if (point == null)
        {
            throw new ArgumentNullException(nameof(point));
        }

        return _transform.Transform(point.X, point.Y);
    }

    public Point2D ProjectVector(XYZ vector)
    {
        if (vector == null)
        {
            throw new ArgumentNullException(nameof(vector));
        }

        return _transform.TransformVector(vector.X, vector.Y);
    }

    private static AffineTransform2D CreateTransform(ProjectLocation projectLocation)
    {
        ProjectPosition origin = projectLocation.GetProjectPosition(XYZ.Zero);
        ProjectPosition xAxis = projectLocation.GetProjectPosition(XYZ.BasisX);
        ProjectPosition yAxis = projectLocation.GetProjectPosition(XYZ.BasisY);

        double feetToMeters = CrsTransformer.FeetToMetersFactor;
        Point2D originMeters = new(
            origin.EastWest * feetToMeters,
            origin.NorthSouth * feetToMeters);
        Point2D xBasisMeters = new(
            (xAxis.EastWest - origin.EastWest) * feetToMeters,
            (xAxis.NorthSouth - origin.NorthSouth) * feetToMeters);
        Point2D yBasisMeters = new(
            (yAxis.EastWest - origin.EastWest) * feetToMeters,
            (yAxis.NorthSouth - origin.NorthSouth) * feetToMeters);

        return new AffineTransform2D(originMeters, xBasisMeters, yBasisMeters);
    }
}
