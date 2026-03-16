using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;

namespace RevitGeoExporter.Export;

public sealed class ExportPackageService
{
    public ExportPackageResult BuildPackage(
        PreparedExportSession session,
        ExportDiagnosticsReport report,
        FloorGeoPackageExportResult exportResult)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (exportResult == null)
        {
            throw new ArgumentNullException(nameof(exportResult));
        }

        string packageDirectory = Path.Combine(
            session.OutputDirectory,
            $"handoff-package-{DateTime.Now:yyyyMMdd-HHmmss}");
        ExportPackageManifest manifest = BuildManifest(session, exportResult, packageDirectory, report.ExportedAtUtc);

        if (!session.PackageOptions.Enabled)
        {
            return new ExportPackageResult(manifest, null, null);
        }

        Directory.CreateDirectory(packageDirectory);
        if (!string.IsNullOrWhiteSpace(exportResult.DiagnosticsReportPath) && File.Exists(exportResult.DiagnosticsReportPath))
        {
            File.Copy(
                exportResult.DiagnosticsReportPath,
                Path.Combine(packageDirectory, Path.GetFileName(exportResult.DiagnosticsReportPath)),
                overwrite: true);
        }

        foreach (ViewExportResult file in exportResult.ViewResults)
        {
            if (File.Exists(file.OutputFilePath))
            {
                File.Copy(file.OutputFilePath, Path.Combine(packageDirectory, Path.GetFileName(file.OutputFilePath)), overwrite: true);
            }
        }

        foreach (PreparedViewExportData view in session.Prepared.Views)
        {
            string imagePath = Path.Combine(packageDirectory, $"{Sanitize(view.View.Name)}-preview.png");
            using Bitmap bitmap = RenderPreviewBitmap(view);
            bitmap.Save(imagePath);
        }

        if (session.PackageOptions.IncludeLegendFile)
        {
            File.WriteAllLines(
                Path.Combine(packageDirectory, "legend.txt"),
                session.Prepared.Views
                    .SelectMany(view => view.UnitLayer?.Features.OfType<ExportPolygon>() ?? Array.Empty<ExportPolygon>())
                    .GroupBy(feature => feature.Attributes.TryGetValue("category", out object? value) ? value?.ToString() ?? "<none>" : "<none>")
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => $"{group.Key}: {group.Count()}"));
        }

        string manifestPath = Path.Combine(packageDirectory, "package-manifest.json");
        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        return new ExportPackageResult(manifest, packageDirectory, manifestPath);
    }

    private static ExportPackageManifest BuildManifest(
        PreparedExportSession session,
        FloorGeoPackageExportResult exportResult,
        string packageDirectory,
        DateTimeOffset exportedAtUtc)
    {
        ExportPackageManifest manifest = new()
        {
            SourceModelName = session.SourceModelName,
            PackageDirectory = packageDirectory,
            TargetEpsg = session.OutputEpsg,
            ExportedAtUtc = exportedAtUtc,
            IncludedLinks = session.IncludedLinks
                .Select(link => ExportLinkedModelInfo.Create(
                    link.LinkInstanceId,
                    link.LinkInstanceName,
                    link.SourceDocumentKey,
                    link.SourceDocumentName))
                .ToList(),
        };

        foreach (ViewExportResult file in exportResult.ViewResults)
        {
            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = file.FeatureType,
                RelativePath = Path.GetFileName(file.OutputFilePath),
            });
        }

        if (!string.IsNullOrWhiteSpace(exportResult.DiagnosticsReportPath))
        {
            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = "diagnostics",
                RelativePath = Path.GetFileName(exportResult.DiagnosticsReportPath),
            });
        }

        foreach (PreparedViewExportData view in session.Prepared.Views)
        {
            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = "preview-image",
                RelativePath = $"{Sanitize(view.View.Name)}-preview.png",
            });
        }

        if (session.PackageOptions.IncludeLegendFile)
        {
            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = "legend",
                RelativePath = "legend.txt",
            });
        }

        manifest.Files.Add(new ExportPackageManifestFile
        {
            Kind = "manifest",
            RelativePath = "package-manifest.json",
        });
        return manifest;
    }

    private static Bitmap RenderPreviewBitmap(PreparedViewExportData view)
    {
        List<IExportFeature> features = new();
        if (view.LevelLayer != null)
        {
            features.AddRange(view.LevelLayer.Features);
        }

        if (view.UnitLayer != null)
        {
            features.AddRange(view.UnitLayer.Features);
        }

        if (view.DetailLayer != null)
        {
            features.AddRange(view.DetailLayer.Features);
        }

        if (view.OpeningLayer != null)
        {
            features.AddRange(view.OpeningLayer.Features);
        }

        Bounds2D bounds = FeatureBoundsCalculator.FromFeatures(features);
        Bitmap bitmap = new(1400, 900);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.White);
        ViewTransform2D transform = ViewTransform2D.Fit(bounds, bitmap.Width, bitmap.Height, 24d);

        if (view.LevelLayer != null)
        {
            foreach (ExportPolygon feature in view.LevelLayer.Features.OfType<ExportPolygon>())
            {
                DrawPolygon(graphics, transform, feature, Color.FromArgb(90, 221, 231, 240), Color.SlateGray);
            }
        }

        if (view.UnitLayer != null)
        {
            foreach (ExportPolygon feature in view.UnitLayer.Features.OfType<ExportPolygon>())
            {
                Color fill = Color.LightGray;
                if (feature.Attributes.TryGetValue("preview_fill_color", out object? fillValue))
                {
                    fill = ParseColor(fillValue?.ToString(), Color.LightGray);
                }

                DrawPolygon(graphics, transform, feature, Color.FromArgb(210, fill), Color.DimGray);
            }
        }

        if (view.DetailLayer != null)
        {
            foreach (ExportLineString feature in view.DetailLayer.Features.OfType<ExportLineString>())
            {
                DrawLine(graphics, transform, feature, Color.DimGray, 1.5f);
            }
        }

        if (view.OpeningLayer != null)
        {
            foreach (ExportLineString feature in view.OpeningLayer.Features.OfType<ExportLineString>())
            {
                DrawLine(graphics, transform, feature, Color.OrangeRed, 2f);
            }
        }

        return bitmap;
    }

    private static void DrawPolygon(Graphics graphics, ViewTransform2D transform, ExportPolygon polygon, Color fill, Color outline)
    {
        using GraphicsPath path = new() { FillMode = FillMode.Alternate };
        foreach (Polygon2D featurePolygon in polygon.Polygons)
        {
            path.AddPolygon(featurePolygon.ExteriorRing.Select(point => ToPointF(transform.WorldToScreen(point))).ToArray());
            for (int i = 0; i < featurePolygon.InteriorRings.Count; i++)
            {
                path.AddPolygon(featurePolygon.InteriorRings[i].Select(point => ToPointF(transform.WorldToScreen(point))).ToArray());
            }
        }

        using SolidBrush brush = new(fill);
        using Pen pen = new(outline, 1.4f);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);
    }

    private static void DrawLine(Graphics graphics, ViewTransform2D transform, ExportLineString line, Color color, float width)
    {
        PointF[] points = line.LineString.Points.Select(point => ToPointF(transform.WorldToScreen(point))).ToArray();
        if (points.Length < 2)
        {
            return;
        }

        using Pen pen = new(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        graphics.DrawLines(pen, points);
    }

    private static PointF ToPointF(Point2D point)
    {
        return new PointF((float)point.X, (float)point.Y);
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = string.IsNullOrWhiteSpace(value) ? "view" : value.Trim();
        for (int i = 0; i < invalid.Length; i++)
        {
            sanitized = sanitized.Replace(invalid[i], '_');
        }

        return sanitized;
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        string normalized = (hex ?? string.Empty).Trim().TrimStart('#');
        if (normalized.Length != 6)
        {
            return fallback;
        }

        try
        {
            return ColorTranslator.FromHtml($"#{normalized}");
        }
        catch
        {
            return fallback;
        }
    }
}

public sealed class ExportPackageResult
{
    public ExportPackageResult(ExportPackageManifest manifest, string? packageDirectory, string? manifestPath)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        PackageDirectory = packageDirectory;
        ManifestPath = manifestPath;
    }

    public ExportPackageManifest Manifest { get; }

    public string? PackageDirectory { get; }

    public string? ManifestPath { get; }
}
