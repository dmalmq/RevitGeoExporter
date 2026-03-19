using System;
using System.IO;
using Newtonsoft.Json;

namespace RevitGeoExporter.Core.Utilities;

public static class JsonFileLoadHelper
{
    public static LoadResult<T> Load<T>(
        string path,
        Func<T> createDefaultValue,
        Func<string, T?> deserialize,
        string documentLabel)
    {
        if (createDefaultValue is null)
        {
            throw new ArgumentNullException(nameof(createDefaultValue));
        }

        if (deserialize is null)
        {
            throw new ArgumentNullException(nameof(deserialize));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(documentLabel))
        {
            throw new ArgumentException("A document label is required.", nameof(documentLabel));
        }

        if (Directory.Exists(path))
        {
            string warning =
                $"{documentLabel} could not be loaded from '{path}'. Using defaults. The configured path is a directory, not a file.";
            return new LoadResult<T>(createDefaultValue(), new[] { warning });
        }

        if (!File.Exists(path))
        {
            return new LoadResult<T>(createDefaultValue());
        }

        try
        {
            string json = File.ReadAllText(path);
            T? value = deserialize(json);
            return new LoadResult<T>(value ?? createDefaultValue());
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            string warning =
                $"{documentLabel} could not be loaded from '{path}'. Using defaults. {ex.Message}";
            return new LoadResult<T>(createDefaultValue(), new[] { warning });
        }
    }
}
