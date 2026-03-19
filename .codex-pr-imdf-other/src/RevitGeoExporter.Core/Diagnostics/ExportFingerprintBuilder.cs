using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportFingerprintBuilder
{
    public string ComputeLayerFingerprint(IEnumerable<ExportLayer> layers)
    {
        if (layers == null)
        {
            throw new ArgumentNullException(nameof(layers));
        }

        StringBuilder builder = new();
        foreach (ExportLayer layer in layers
                     .Where(layer => layer != null)
                     .OrderBy(layer => layer.Name, StringComparer.Ordinal))
        {
            builder.Append("layer|")
                .Append(layer.Name)
                .Append('|')
                .Append(layer.GeometryType)
                .AppendLine();

            foreach (AttributeDefinition attribute in layer.Attributes
                         .OrderBy(attribute => attribute.Name, StringComparer.Ordinal))
            {
                builder.Append("attr|")
                    .Append(attribute.Name)
                    .Append('|')
                    .Append(attribute.Type)
                    .AppendLine();
            }

            foreach (string featureFingerprint in layer.Features
                         .Select(ComputeFeatureFingerprint)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                builder.Append("feature|")
                    .Append(featureFingerprint)
                    .AppendLine();
            }
        }

        return ComputeHash(builder.ToString());
    }

    public string ComputeConfigurationFingerprint(IEnumerable<string> exportAffectingInputs)
    {
        if (exportAffectingInputs == null)
        {
            throw new ArgumentNullException(nameof(exportAffectingInputs));
        }

        string normalized = string.Join(
            "\n",
            exportAffectingInputs
                .Where(value => value != null)
                .Select(value => value.Trim())
                .OrderBy(value => value, StringComparer.Ordinal));
        return ComputeHash(normalized);
    }

    private static string ComputeFeatureFingerprint(IExportFeature feature)
    {
        StringBuilder builder = new();
        builder.Append(feature.GetType().Name).Append('|');
        foreach (KeyValuePair<string, object?> attribute in feature.Attributes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            builder.Append(attribute.Key)
                .Append('=')
                .Append(SerializeValue(attribute.Value))
                .Append(';');
        }

        builder.Append('|');
        switch (feature)
        {
            case ExportPolygon polygon:
                AppendPolygon(builder, polygon);
                break;
            case ExportLineString lineString:
                AppendLineString(builder, lineString);
                break;
            default:
                foreach (Point2D point in feature.GetAllPoints())
                {
                    AppendPoint(builder, point);
                }

                break;
        }

        return ComputeHash(builder.ToString());
    }

    private static void AppendPolygon(StringBuilder builder, ExportPolygon polygon)
    {
        foreach (Polygon2D geometry in polygon.Polygons)
        {
            builder.Append("poly:");
            foreach (Point2D point in geometry.ExteriorRing)
            {
                AppendPoint(builder, point);
            }

            for (int i = 0; i < geometry.InteriorRings.Count; i++)
            {
                builder.Append("hole:");
                foreach (Point2D point in geometry.InteriorRings[i])
                {
                    AppendPoint(builder, point);
                }
            }
        }
    }

    private static void AppendLineString(StringBuilder builder, ExportLineString lineString)
    {
        builder.Append("line:");
        foreach (Point2D point in lineString.LineString.Points)
        {
            AppendPoint(builder, point);
        }
    }

    private static void AppendPoint(StringBuilder builder, Point2D point)
    {
        builder.Append(point.X.ToString("R", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(point.Y.ToString("R", CultureInfo.InvariantCulture))
            .Append('|');
    }

    private static string SerializeValue(object? value)
    {
        return value switch
        {
            null => "<null>",
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString()?.Trim() ?? string.Empty,
        };
    }

    private static string ComputeHash(string value)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
        StringBuilder builder = new(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
