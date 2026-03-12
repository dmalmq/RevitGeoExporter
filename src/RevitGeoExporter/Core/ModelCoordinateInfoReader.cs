using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core;

public sealed class ModelCoordinateInfoReader
{
    private static readonly Regex WktAuthorityRegex = new(@"(?:AUTHORITY\[""EPSG"",""(\d+)""\]|ID\[""EPSG"",\s*(\d+)\s*\])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ModelCoordinateInfo Read(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        ProjectLocation? location = document.ActiveProjectLocation;
        ProjectPosition? position = location?.GetProjectPosition(XYZ.Zero);
        SiteLocation? siteLocation = document.SiteLocation;
        string siteCoordinateSystemId = siteLocation?.GeoCoordinateSystemId?.Trim() ?? string.Empty;
        string siteCoordinateSystemDefinition = siteLocation?.GeoCoordinateSystemDefinition?.Trim() ?? string.Empty;
        int? resolvedSourceEpsg = TryResolveSourceEpsg(siteCoordinateSystemId, siteCoordinateSystemDefinition);

        return new ModelCoordinateInfo
        {
            DisplayLengthUnitLabel = ReadDisplayLengthUnitLabel(document),
            ActiveProjectLocationName = location?.Name?.Trim() ?? string.Empty,
            SharedCoordinateSummary = BuildSharedCoordinateSummary(position),
            SiteCoordinateSystemId = siteCoordinateSystemId,
            SiteCoordinateSystemDefinition = siteCoordinateSystemDefinition,
            ResolvedSourceEpsg = resolvedSourceEpsg,
            ResolvedSourceLabel = BuildResolvedSourceLabel(siteCoordinateSystemId, resolvedSourceEpsg),
            SurveyPointSharedCoordinates = ReadSurveyPointSharedCoordinates(document, location),
        };
    }

    private static string ReadDisplayLengthUnitLabel(Document document)
    {
        try
        {
            Units units = document.GetUnits();
            FormatOptions formatOptions = units.GetFormatOptions(SpecTypeId.Length);
            ForgeTypeId unitTypeId = formatOptions.GetUnitTypeId();
            string? label = LabelUtils.GetLabelForUnit(unitTypeId);
            return string.IsNullOrWhiteSpace(label) ? unitTypeId.TypeId : label.Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string BuildSharedCoordinateSummary(ProjectPosition? position)
    {
        if (position == null)
        {
            return "Unavailable";
        }

        double eastWestMeters = position.EastWest * CrsTransformer.FeetToMetersFactor;
        double northSouthMeters = position.NorthSouth * CrsTransformer.FeetToMetersFactor;
        double elevationMeters = position.Elevation * CrsTransformer.FeetToMetersFactor;
        double angleDegrees = position.Angle * (180d / Math.PI);
        return string.Format(
            CultureInfo.InvariantCulture,
            "EW {0:0.###} m, NS {1:0.###} m, Elev {2:0.###} m, Angle {3:0.###}ﾂｰ",
            eastWestMeters,
            northSouthMeters,
            elevationMeters,
            angleDegrees);
    }

    private static int? TryResolveSourceEpsg(string siteCoordinateSystemId, string siteCoordinateSystemDefinition)
    {
        if (JapanPlaneRectangular.TryResolveEpsg(siteCoordinateSystemId, out int idEpsg))
        {
            return idEpsg;
        }

        Match authorityMatch = WktAuthorityRegex.Match(siteCoordinateSystemDefinition ?? string.Empty);
        for (int i = 1; i < authorityMatch.Groups.Count; i++)
        {
            if (authorityMatch.Groups[i].Success && int.TryParse(authorityMatch.Groups[i].Value, out int wktEpsg))
            {
                return wktEpsg;
            }
        }

        return null;
    }

    private static string BuildResolvedSourceLabel(string siteCoordinateSystemId, int? resolvedSourceEpsg)
    {
        if (resolvedSourceEpsg.HasValue)
        {
            return JapanPlaneRectangular.DescribeEpsg(resolvedSourceEpsg.Value);
        }

        if (!string.IsNullOrWhiteSpace(siteCoordinateSystemId))
        {
            return siteCoordinateSystemId.Trim();
        }

        return "Not resolved";
    }

    private static Point2D? ReadSurveyPointSharedCoordinates(Document document, ProjectLocation? location)
    {
        if (location == null)
        {
            return null;
        }

        try
        {
            BasePoint? surveyPoint = new FilteredElementCollector(document)
                .OfClass(typeof(BasePoint))
                .Cast<BasePoint>()
                .FirstOrDefault(basePoint => basePoint.IsShared);
            if (surveyPoint == null)
            {
                return null;
            }

            SharedCoordinateProjector projector = new(location);
            return projector.ProjectPoint(surveyPoint.Position);
        }
        catch
        {
            return null;
        }
    }
}