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

        string? packageDirectory = session.PackageOptions.Enabled
            ? Path.Combine(session.OutputDirectory, $"handoff-package-{DateTime.Now:yyyyMMdd-HHmmss}")
            : null;
        if (!string.IsNullOrWhiteSpace(packageDirectory))
        {
            Directory.CreateDirectory(packageDirectory);
        }

        ExportPackageManifest manifest = BuildManifest(session, exportResult, packageDirectory, report.ExportedAtUtc);

        if (!string.IsNullOrWhiteSpace(exportResult.DiagnosticsReportPath) && File.Exists(exportResult.DiagnosticsReportPath))
        {
            string diagnosticsOutputPath = exportResult.DiagnosticsReportPath;
            if (!string.IsNullOrWhiteSpace(packageDirectory))
            {
                diagnosticsOutputPath = Path.Combine(packageDirectory, Path.GetFileName(exportResult.DiagnosticsReportPath));
                File.Copy(exportResult.DiagnosticsReportPath, diagnosticsOutputPath, overwrite: true);
            }

            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = "diagnostics",
                RelativePath = Path.GetFileName(diagnosticsOutputPath),
                OutputFilePath = diagnosticsOutputPath,
            });
        }

        if (!string.IsNullOrWhiteSpace(packageDirectory))
        {
            foreach (ExportPackageManifestFile artifact in manifest.Files.Where(file => file.IsArtifact))
            {
                ExportArtifactResult? sourceArtifact = exportResult.ArtifactResults.FirstOrDefault(result =>
                    string.Equals(result.ArtifactKey, artifact.ArtifactKey, StringComparison.Ordinal));
                if (sourceArtifact != null && !string.IsNullOrWhiteSpace(artifact.OutputFilePath))
                {
                    File.Copy(sourceArtifact.OutputFilePath, artifact.OutputFilePath, overwrite: true);
                }
            }

            WritePreviewImages(session, manifest, packageDirectory);

            if (session.PackageOptions.IncludeLegendFile)
            {
                WriteLegendFile(session, manifest, packageDirectory);
            }

            if (session.PackageOptions.GenerateQgisArtifacts)
            {
                WriteQgisArtifacts(session, manifest, packageDirectory);
            }
        }

        PackageValidationResult? validationResult = null;
        if (session.PackageOptions.ValidateAfterWrite)
        {
            validationResult = new PackageValidationService().Validate(manifest);
            manifest.ValidationResult = validationResult;
        }

        if (!string.IsNullOrWhiteSpace(packageDirectory) && session.PackageOptions.GenerateQgisArtifacts)
        {
            WriteReadmeFile(session, manifest, packageDirectory);
        }

        string? manifestPath = null;
        if (!string.IsNullOrWhiteSpace(packageDirectory))
        {
            manifestPath = Path.Combine(packageDirectory, "package-manifest.json");
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = "manifest",
                RelativePath = "package-manifest.json",
                OutputFilePath = manifestPath,
            });
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }

        return new ExportPackageResult(manifest, packageDirectory, manifestPath, validationResult);
    }

    private static ExportPackageManifest BuildManifest(
        PreparedExportSession session,
        FloorGeoPackageExportResult exportResult,
        string? packageDirectory,
        DateTimeOffset exportedAtUtc)
    {
        ExportPackageManifest manifest = new()
        {
            SourceModelName = session.SourceModelName,
            SourceDocumentKey = session.SourceDocumentKey,
            ProfileName = session.ProfileName,
            SchemaProfileName = session.ActiveSchemaProfile.Name,
            ValidationPolicyProfileName = session.ActiveValidationPolicyProfile.Name,
            OperatorName = Environment.UserName ?? string.Empty,
            CoordinateMode = session.CoordinateMode.ToString(),
            SourceEpsg = session.SourceEpsg,
            SourceCoordinateSystemId = session.SourceCoordinateSystemId,
            SourceCoordinateSystemDefinition = session.SourceCoordinateSystemDefinition,
            PackagingMode = session.PackageOptions.PackagingMode.ToString(),
            PackageDirectory = packageDirectory ?? string.Empty,
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

        foreach (ExportArtifactResult artifact in exportResult.ArtifactResults)
        {
            manifest.Files.Add(new ExportPackageManifestFile
            {
                ArtifactKey = artifact.ArtifactKey,
                Kind = "gpkg",
                RelativePath = Path.GetFileName(artifact.OutputFilePath),
                OutputFilePath = string.IsNullOrWhiteSpace(packageDirectory)
                    ? artifact.OutputFilePath
                    : Path.Combine(packageDirectory, Path.GetFileName(artifact.OutputFilePath)),
                PackagingMode = artifact.PackagingMode.ToString(),
                Disposition = artifact.Disposition.ToString(),
                FeatureCount = artifact.FeatureCount,
                ContributingViewIds = artifact.ContributingViewIds.ToList(),
                ContributingViewNames = artifact.ContributingViewNames.ToList(),
                ContributingLevelNames = artifact.ContributingLevelNames.ToList(),
                ContainedLayers = artifact.LayerNames.ToList(),
                MandatoryLayers = artifact.LayerNames.ToList(),
                IsArtifact = true,
            });
        }

        return manifest;
    }

    private static void WritePreviewImages(PreparedExportSession session, ExportPackageManifest manifest, string packageDirectory)
    {
        foreach (PreparedViewExportData view in session.Prepared.Views)
        {
            string imagePath = Path.Combine(packageDirectory, $"{Sanitize(view.View.Name)}-preview.png");
            using Bitmap bitmap = RenderPreviewBitmap(view);
            bitmap.Save(imagePath);
            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = "preview-image",
                RelativePath = Path.GetFileName(imagePath),
                OutputFilePath = imagePath,
            });
        }
    }

    private static void WriteLegendFile(PreparedExportSession session, ExportPackageManifest manifest, string packageDirectory)
    {
        string legendPath = Path.Combine(packageDirectory, "legend.txt");
        File.WriteAllLines(
            legendPath,
            session.Prepared.Views
                .SelectMany(view => view.UnitLayer?.Features.OfType<ExportPolygon>() ?? Array.Empty<ExportPolygon>())
                .GroupBy(feature => feature.Attributes.TryGetValue("category", out object? value) ? value?.ToString() ?? "<none>" : "<none>")
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Key}: {group.Count()}"));
        manifest.Files.Add(new ExportPackageManifestFile
        {
            Kind = "legend",
            RelativePath = "legend.txt",
            OutputFilePath = legendPath,
        });
    }

    private static void WriteQgisArtifacts(PreparedExportSession session, ExportPackageManifest manifest, string packageDirectory)
    {
        HashSet<string> layerNames = manifest.Files
            .Where(file => file.IsArtifact)
            .SelectMany(file => file.ContainedLayers)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string layerName in layerNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            string styleFileName = $"{layerName}.qml";
            string stylePath = Path.Combine(packageDirectory, styleFileName);
            File.WriteAllText(stylePath, BuildQgisStyleTemplate(layerName));
            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = "qgis-style",
                RelativePath = styleFileName,
                OutputFilePath = stylePath,
            });
        }

        string metadataPath = Path.Combine(packageDirectory, "layer-groups.json");
        File.WriteAllText(
            metadataPath,
            JsonConvert.SerializeObject(
                manifest.Files
                    .Where(file => file.IsArtifact)
                    .Select(file => new
                    {
                        file.ArtifactKey,
                        file.ContributingViewNames,
                        file.ContributingLevelNames,
                        file.ContainedLayers,
                    }),
                Formatting.Indented));
        manifest.Files.Add(new ExportPackageManifestFile
        {
            Kind = "qgis-metadata",
            RelativePath = "layer-groups.json",
            OutputFilePath = metadataPath,
        });
    }

    private static void WriteReadmeFile(PreparedExportSession session, ExportPackageManifest manifest, string packageDirectory)
    {
        string readmePath = Path.Combine(packageDirectory, "README.txt");
        File.WriteAllLines(readmePath, BuildReadmeLines(session, manifest));

        ExportPackageManifestFile? existing = manifest.Files.FirstOrDefault(file =>
            string.Equals(file.Kind, "readme", StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = "readme",
                RelativePath = "README.txt",
                OutputFilePath = readmePath,
            });
            return;
        }

        existing.RelativePath = "README.txt";
        existing.OutputFilePath = readmePath;
    }

    private static IEnumerable<string> BuildReadmeLines(PreparedExportSession session, ExportPackageManifest manifest)
    {
        yield return $"Source model: {session.SourceModelName}";
        yield return $"Packaging mode: {session.PackageOptions.PackagingMode}";
        yield return $"Output CRS: EPSG:{session.OutputEpsg}";
        yield return $"Coordinate mode: {session.CoordinateMode}";
        yield return $"Profile: {session.ProfileName ?? "<none>"}";
        yield return $"Schema profile: {session.ActiveSchemaProfile.Name}";
        yield return $"Validation policy: {session.ActiveValidationPolicyProfile.Name}";
        yield return string.Empty;
        yield return "Artifacts:";
        foreach (ExportPackageManifestFile file in manifest.Files.Where(file => file.IsArtifact))
        {
            yield return $"- {file.RelativePath} [{string.Join(", ", file.ContainedLayers)}]";
        }

        if (manifest.ValidationResult != null)
        {
            yield return string.Empty;
            yield return $"Validation errors: {manifest.ValidationResult.Issues.Count(issue => issue.Severity == PackageValidationSeverity.Error)}";
            yield return $"Validation warnings: {manifest.ValidationResult.Issues.Count(issue => issue.Severity == PackageValidationSeverity.Warning)}";
        }
    }

    private static string BuildQgisStyleTemplate(string layerName)
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<qgis styleCategories=\"AllStyleCategories\" version=\"3.34\">\n" +
            $"  <renderer-v2 type=\"singleSymbol\" symbollevels=\"0\" referencescale=\"-1\" forceraster=\"0\" enableorderby=\"0\" attr=\"{layerName}\"/>\n" +
            "</qgis>\n";
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
    public ExportPackageResult(
        ExportPackageManifest manifest,
        string? packageDirectory,
        string? manifestPath,
        PackageValidationResult? validationResult)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        PackageDirectory = packageDirectory;
        ManifestPath = manifestPath;
        ValidationResult = validationResult;
    }

    public ExportPackageManifest Manifest { get; }

    public string? PackageDirectory { get; }

    public string? ManifestPath { get; }

    public PackageValidationResult? ValidationResult { get; }
}
