using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjNet;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Coordinates;

public static class CoordinateSystemCatalog
{
    private const string Wgs84Wkt =
        "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]]," +
        "PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4326\"]]";
    private const string WebMercatorWkt =
        "PROJCS[\"WGS 84 / Pseudo-Mercator\"," +
        "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]]," +
        "PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]]," +
        // OSM tiles use EPSG:3857 pseudo-Mercator, not ellipsoidal Mercator_1SP.
        "PROJECTION[\"Popular_Visualisation_Pseudo_Mercator\"]," +
        "PARAMETER[\"latitude_of_origin\",0]," +
        "PARAMETER[\"central_meridian\",0]," +
        "PARAMETER[\"false_easting\",0]," +
        "PARAMETER[\"false_northing\",0]," +
        "UNIT[\"metre\",1]," +
        "AXIS[\"X\",EAST]," +
        "AXIS[\"Y\",NORTH]," +
        "AUTHORITY[\"EPSG\",\"3857\"]]";

    private static readonly CoordinateSystemFactory Factory = new();
    private static readonly CoordinateTransformationFactory TransformationFactory = new();
    private static readonly CoordinateSystemServices Services = CreateServices();

    public static bool TryGetDefinitionWkt(int epsg, out string wkt)
    {
        if (epsg == 4326)
        {
            wkt = Wgs84Wkt;
            return true;
        }

        if (epsg == 3857)
        {
            wkt = WebMercatorWkt;
            return true;
        }

        if (JapanPlaneRectangular.TryGetZone(epsg, out JapanPlaneRectangularZoneDefinition? zone))
        {
            wkt = BuildJapanPlaneRectangularWkt(zone!);
            return true;
        }

        wkt = string.Empty;
        return false;
    }

    public static bool TryCreateFromEpsg(int epsg, out CoordinateSystem? coordinateSystem)
    {
        try
        {
            coordinateSystem = Services.GetCoordinateSystem(epsg);
            return coordinateSystem != null;
        }
        catch
        {
            coordinateSystem = null;
            return false;
        }
    }

    public static bool TryCreateWebMercator(out CoordinateSystem? coordinateSystem)
    {
        return TryCreateFromEpsg(3857, out coordinateSystem);
    }

    public static string DescribeEpsg(int epsg)
    {
        return epsg switch
        {
            3857 => "EPSG:3857 - WGS 84 / Pseudo-Mercator",
            4326 => "EPSG:4326 - WGS 84",
            _ when JapanPlaneRectangular.TryGetZone(epsg, out JapanPlaneRectangularZoneDefinition? zone)
                => $"EPSG:{epsg} - {zone!.DisplayName}",
            _ => $"EPSG:{epsg}",
        };
    }

    public static bool TryCreateSourceCoordinateSystem(
        string? siteCoordinateSystemDefinition,
        string? siteCoordinateSystemId,
        int? resolvedSourceEpsg,
        out CoordinateSystem? coordinateSystem,
        out string failureReason)
    {
        // Prefer numeric CRS identifiers over raw Revit WKT because the site WKT can parse
        // successfully while still drifting from the model's resolved shared-coordinate CRS.
        if (resolvedSourceEpsg.HasValue && TryCreateFromEpsg(resolvedSourceEpsg.Value, out coordinateSystem))
        {
            failureReason = string.Empty;
            return true;
        }

        if (JapanPlaneRectangular.TryResolveEpsg(siteCoordinateSystemId, out int parsedEpsg) &&
            TryCreateFromEpsg(parsedEpsg, out coordinateSystem))
        {
            failureReason = string.Empty;
            return true;
        }

        string trimmedDefinition = siteCoordinateSystemDefinition?.Trim() ?? string.Empty;
        if (trimmedDefinition.Length > 0)
        {
            try
            {
                coordinateSystem = Factory.CreateFromWkt(trimmedDefinition);
                failureReason = string.Empty;
                return coordinateSystem != null;
            }
            catch
            {
                // Fall through to the shared failure reason below.
            }
        }

        coordinateSystem = null;
        failureReason = "The model's shared/site coordinate system could not be resolved to a supported CRS definition.";
        return false;
    }

    public static ExportLayer ReprojectLayer(ExportLayer layer, CoordinateSystem source, CoordinateSystem target)
    {
        if (layer is null)
        {
            throw new ArgumentNullException(nameof(layer));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var transformation = TransformationFactory.CreateFromCoordinateSystems(source, target);
        if (transformation == null)
        {
            throw new InvalidOperationException("A coordinate transformation could not be created for the requested CRS conversion.");
        }

        MathTransform mathTransform = transformation.MathTransform;
        ExportLayer transformed = new(layer.Name, layer.GeometryType, layer.Attributes);
        foreach (IExportFeature feature in layer.Features)
        {
            transformed.AddFeature(ReprojectFeature(feature, mathTransform));
        }

        return transformed;
    }

    public static IExportFeature ReprojectFeature(IExportFeature feature, CoordinateSystem source, CoordinateSystem target)
    {
        if (feature is null)
        {
            throw new ArgumentNullException(nameof(feature));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var transformation = TransformationFactory.CreateFromCoordinateSystems(source, target);
        if (transformation == null)
        {
            throw new InvalidOperationException("A coordinate transformation could not be created for the requested CRS conversion.");
        }

        return ReprojectFeature(feature, transformation.MathTransform);
    }

    public static Point2D ReprojectPoint(Point2D point, CoordinateSystem source, CoordinateSystem target)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var transformation = TransformationFactory.CreateFromCoordinateSystems(source, target);
        if (transformation == null)
        {
            throw new InvalidOperationException("A coordinate transformation could not be created for the requested CRS conversion.");
        }

        return TransformPoint(point, transformation.MathTransform);
    }

    private static IExportFeature ReprojectFeature(IExportFeature feature, MathTransform mathTransform)
    {
        return feature switch
        {
            ExportPolygon polygon => new ExportPolygon(
                polygon.Polygons.Select(x => TransformPolygon(x, mathTransform)).ToList(),
                CloneAttributes(polygon.Attributes)),
            ExportLineString line => new ExportLineString(
                new LineString2D(line.LineString.Points.Select(point => TransformPoint(point, mathTransform)).ToList()),
                CloneAttributes(line.Attributes)),
            _ => throw new NotSupportedException($"Unsupported export feature type '{feature.GetType().Name}'."),
        };
    }

    private static Polygon2D TransformPolygon(Polygon2D polygon, MathTransform mathTransform)
    {
        List<Point2D> exterior = polygon.ExteriorRing.Select(point => TransformPoint(point, mathTransform)).ToList();
        List<IReadOnlyList<Point2D>> interiors = polygon.InteriorRings
            .Select(ring => (IReadOnlyList<Point2D>)ring.Select(point => TransformPoint(point, mathTransform)).ToList())
            .ToList();
        return new Polygon2D(exterior, interiors);
    }

    private static Point2D TransformPoint(Point2D point, MathTransform mathTransform)
    {
        var transformed = mathTransform.Transform(point.X, point.Y);
        return new Point2D(transformed.x, transformed.y);
    }

    private static IReadOnlyDictionary<string, object?> CloneAttributes(IReadOnlyDictionary<string, object?> attributes)
    {
        return attributes.Count == 0
            ? new Dictionary<string, object?>()
            : attributes.ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    private static CoordinateSystemServices CreateServices()
    {
        List<KeyValuePair<int, string>> definitions = new()
        {
            new KeyValuePair<int, string>(4326, Wgs84Wkt),
            new KeyValuePair<int, string>(3857, WebMercatorWkt),
        };
        definitions.AddRange(
            JapanPlaneRectangular.AllZones.Select(zone =>
                new KeyValuePair<int, string>(zone.Epsg, BuildJapanPlaneRectangularWkt(zone))));
        return new CoordinateSystemServices(definitions);
    }

    private static string BuildJapanPlaneRectangularWkt(JapanPlaneRectangularZoneDefinition zone)
    {
        string latitude = zone.LatitudeOfOriginDegrees.ToString("0.###############", CultureInfo.InvariantCulture);
        string centralMeridian = zone.CentralMeridianDegrees.ToString("0.###############", CultureInfo.InvariantCulture);

        return
            $"PROJCS[\"JGD2011 / Japan Plane Rectangular CS {zone.RomanZone}\"," +
            "GEOGCS[\"JGD2011\",DATUM[\"Japanese_Geodetic_Datum_2011\"," +
            "SPHEROID[\"GRS 1980\",6378137,298.257222101]]," +
            "PRIMEM[\"Greenwich\",0]," +
            "UNIT[\"degree\",0.0174532925199433]]," +
            "PROJECTION[\"Transverse_Mercator\"]," +
            $"PARAMETER[\"latitude_of_origin\",{latitude}]," +
            $"PARAMETER[\"central_meridian\",{centralMeridian}]," +
            "PARAMETER[\"scale_factor\",0.9999]," +
            "PARAMETER[\"false_easting\",0]," +
            "PARAMETER[\"false_northing\",0]," +
            "UNIT[\"metre\",1]," +
            "AXIS[\"X\",EAST]," +
            "AXIS[\"Y\",NORTH]," +
            $"AUTHORITY[\"EPSG\",\"{zone.Epsg}\"]]";
    }
}
