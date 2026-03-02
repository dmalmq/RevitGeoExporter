using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoExporter.Core;

public sealed class SharedCoordinateValidator
{
    private const double FeetTolerance = 0.01d;

    public SharedCoordinateValidationResult Validate(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        List<string> warnings = new();
        ProjectLocation? location = document.ActiveProjectLocation;
        if (location == null)
        {
            warnings.Add("Active project location was not found; shared coordinate validation was skipped.");
            return new SharedCoordinateValidationResult(warnings);
        }

        ProjectPosition positionAtOrigin = location.GetProjectPosition(XYZ.Zero);
        bool locationAtOrigin =
            IsNearZero(positionAtOrigin.EastWest) &&
            IsNearZero(positionAtOrigin.NorthSouth);
        if (locationAtOrigin)
        {
            warnings.Add(
                "Shared coordinates appear to be near origin (East/West and North/South are approximately zero). " +
                "Confirm Survey Point / shared coordinates are configured.");
        }

        BasePoint? surveyPoint = new FilteredElementCollector(document)
            .OfClass(typeof(BasePoint))
            .Cast<BasePoint>()
            .FirstOrDefault(basePoint => basePoint.IsShared);
        if (surveyPoint == null)
        {
            warnings.Add("Survey point was not found; shared coordinate validation is incomplete.");
            return new SharedCoordinateValidationResult(warnings);
        }

        XYZ surveyPointPosition = surveyPoint.Position;
        bool surveyAtOrigin =
            IsNearZero(surveyPointPosition.X) &&
            IsNearZero(surveyPointPosition.Y);
        if (surveyAtOrigin)
        {
            warnings.Add(
                "Survey point is near (0,0). Export may not be georeferenced unless this is intentional.");
        }

        return new SharedCoordinateValidationResult(warnings);
    }

    private static bool IsNearZero(double value)
    {
        return Math.Abs(value) <= FeetTolerance;
    }
}

public sealed class SharedCoordinateValidationResult
{
    public SharedCoordinateValidationResult(IReadOnlyList<string> warnings)
    {
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
    }

    public IReadOnlyList<string> Warnings { get; }
}
