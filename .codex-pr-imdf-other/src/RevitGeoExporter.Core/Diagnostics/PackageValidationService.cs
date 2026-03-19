using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class PackageValidationService
{
    public PackageValidationResult Validate(ExportPackageManifest manifest)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        PackageValidationResult result = new();
        foreach (ExportPackageManifestFile artifact in manifest.Files.Where(file => file.IsArtifact))
        {
            ValidateArtifact(manifest, artifact, result.Issues);
        }

        return result;
    }

    private static void ValidateArtifact(
        ExportPackageManifest manifest,
        ExportPackageManifestFile artifact,
        ICollection<PackageValidationIssue> issues)
    {
        string path = artifact.OutputFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = string.IsNullOrWhiteSpace(manifest.PackageDirectory)
                ? artifact.RelativePath
                : Path.Combine(manifest.PackageDirectory, artifact.RelativePath ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            issues.Add(new PackageValidationIssue
            {
                Severity = PackageValidationSeverity.Error,
                ArtifactKey = artifact.ArtifactKey,
                RelativePath = artifact.RelativePath,
                Message = "Expected artifact file was not found.",
            });
            return;
        }

        if (!string.Equals(Path.GetExtension(path), ".gpkg", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using SqliteConnection connection = new($"Data Source={path};Pooling=False");
        connection.Open();

        HashSet<string> actualLayerNames = ReadLayerNames(connection);
        foreach (string expectedLayer in artifact.ContainedLayers ?? new List<string>())
        {
            if (!actualLayerNames.Contains(expectedLayer))
            {
                issues.Add(new PackageValidationIssue
                {
                    Severity = PackageValidationSeverity.Error,
                    ArtifactKey = artifact.ArtifactKey,
                    RelativePath = artifact.RelativePath,
                    LayerName = expectedLayer,
                    Message = $"Expected layer '{expectedLayer}' was not found.",
                });
                continue;
            }

            if (artifact.MandatoryLayers.Contains(expectedLayer, StringComparer.OrdinalIgnoreCase) &&
                ReadTableCount(connection, expectedLayer) == 0)
            {
                issues.Add(new PackageValidationIssue
                {
                    Severity = PackageValidationSeverity.Error,
                    ArtifactKey = artifact.ArtifactKey,
                    RelativePath = artifact.RelativePath,
                    LayerName = expectedLayer,
                    Message = $"Mandatory layer '{expectedLayer}' was empty.",
                });
            }
        }
    }

    private static HashSet<string> ReadLayerNames(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM gpkg_contents;";
        using SqliteDataReader reader = command.ExecuteReader();

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static long ReadTableCount(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\";";
        return (long)(command.ExecuteScalar() ?? 0L);
    }
}
