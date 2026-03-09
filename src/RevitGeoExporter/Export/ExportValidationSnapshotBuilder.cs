using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.Export;

public sealed class ExportValidationSnapshotBuilder
{
    private readonly ZoneCatalog _zoneCatalog = ZoneCatalog.CreateDefault();

    public ExportValidationRequest Build(PreparedExportSession session)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        List<ValidationViewSnapshot> views = new();
        Dictionary<long, PreparedViewExportData> preparedByViewId = session.Prepared.Views
            .ToDictionary(view => view.View.Id.Value);

        foreach (ViewExportContext context in session.Contexts)
        {
            preparedByViewId.TryGetValue(context.View.Id.Value, out PreparedViewExportData? prepared);
            List<ExportFeatureValidationSnapshot> features = new();
            if (prepared != null)
            {
                AddLayerFeatures(features, "unit", prepared.UnitLayer?.Features);
                AddLayerFeatures(features, "detail", prepared.DetailLayer?.Features);
                AddLayerFeatures(features, "opening", prepared.OpeningLayer?.Features);
                AddLayerFeatures(features, "level", prepared.LevelLayer?.Features);
            }

            List<UnsupportedOpeningFamilySnapshot> unsupportedOpenings = context.UnsupportedOpenings
                .Select(opening => new UnsupportedOpeningFamilySnapshot(
                    OpeningFamilyClassifier.GetFamilyName(opening),
                    opening.Id.Value))
                .ToList();
            views.Add(
                new ValidationViewSnapshot(
                    context.View.Id.Value,
                    context.View.Name,
                    context.Level.Name,
                    features,
                    unsupportedOpenings,
                    sourceStairsCount: context.Stairs.Count,
                    sourceEscalatorCount: CountSourceFamilyUnits(context.FamilyUnits, "escalator"),
                    sourceElevatorCount: CountSourceFamilyUnits(context.FamilyUnits, "elevator")));
        }

        return new ExportValidationRequest(
            session.TargetEpsg,
            session.FeatureTypes.HasFlag(ExportFeatureType.Unit),
            session.FeatureTypes.HasFlag(ExportFeatureType.Detail),
            session.FeatureTypes.HasFlag(ExportFeatureType.Opening),
            session.FeatureTypes.HasFlag(ExportFeatureType.Level),
            views);
    }

    private static void AddLayerFeatures(
        ICollection<ExportFeatureValidationSnapshot> target,
        string featureType,
        IEnumerable<IExportFeature>? features)
    {
        if (features == null)
        {
            return;
        }

        foreach (IExportFeature feature in features)
        {
            target.Add(BuildSnapshot(featureType, feature));
        }
    }

    private static ExportFeatureValidationSnapshot BuildSnapshot(string featureType, IExportFeature feature)
    {
        bool hasGeometry = FeatureHasGeometry(feature);
        bool geometryValid = FeatureGeometryIsValid(feature);
        string? category = ReadString(feature.Attributes, "category");
        if (string.Equals(featureType, "opening", StringComparison.OrdinalIgnoreCase) &&
            ReadBool(feature.Attributes, "is_elevator_door"))
        {
            category = "elevator";
        }

        return new ExportFeatureValidationSnapshot(
            featureType,
            ReadString(feature.Attributes, "id"),
            category,
            ReadNullableLong(feature.Attributes, "source_element_id") ??
            ReadNullableLong(feature.Attributes, "element_id"),
            hasGeometry,
            geometryValid,
            ReadBool(feature.Attributes, "is_unassigned"),
            ReadString(feature.Attributes, "source_floor_type_name"),
            ReadBool(feature.Attributes, "is_snapped_to_outline", defaultValue: true));
    }

    private int CountSourceFamilyUnits(IReadOnlyList<FamilyInstance> familyUnits, string category)
    {
        int count = 0;
        for (int i = 0; i < familyUnits.Count; i++)
        {
            FamilyInstance unit = familyUnits[i];
            string familyName = Extractors.UnitExtractor.GetFamilyName(unit);
            if (!_zoneCatalog.TryGetFamilyInfo(familyName, out ZoneInfo zoneInfo))
            {
                continue;
            }

            if (string.Equals(zoneInfo.Category, category, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static bool FeatureHasGeometry(IExportFeature feature)
    {
        return feature switch
        {
            ExportPolygon polygon => polygon.Polygons.Any(p => p.ExteriorRing.Count >= 4),
            ExportLineString line => line.LineString.Points.Count >= 2,
            _ => feature.GetAllPoints().Any(),
        };
    }

    private static bool FeatureGeometryIsValid(IExportFeature feature)
    {
        return feature switch
        {
            ExportPolygon polygon => polygon.Polygons.All(IsValidPolygon),
            ExportLineString line => line.LineString.Points.Count >= 2,
            _ => FeatureHasGeometry(feature),
        };
    }

    private static bool IsValidPolygon(Polygon2D polygon)
    {
        if (polygon == null || polygon.ExteriorRing == null || polygon.ExteriorRing.Count < 4)
        {
            return false;
        }

        Point2D first = polygon.ExteriorRing[0];
        Point2D last = polygon.ExteriorRing[polygon.ExteriorRing.Count - 1];
        return Math.Abs(first.X - last.X) <= 1e-8d &&
               Math.Abs(first.Y - last.Y) <= 1e-8d;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return null;
        }

        string trimmed = value.ToString()?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object?> attributes, string key, bool defaultValue = false)
    {
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return defaultValue;
        }

        return value switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out bool parsed) => parsed,
            _ => defaultValue,
        };
    }

    private static long? ReadNullableLong(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return null;
        }

        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) => parsed,
            _ => null,
        };
    }
}
