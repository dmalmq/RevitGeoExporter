using System;
using System.IO;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Utilities;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Utilities;

public sealed class JsonFileLoadHelperTests
{
    [Fact]
    public void Load_ReturnsWarningAndDefaultValueForMalformedJson()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(tempDirectory, "bad.json");
            File.WriteAllText(path, "{not valid json");

            LoadResult<TestDocument> result = JsonFileLoadHelper.Load(
                path,
                createDefaultValue: () => new TestDocument { Name = "default" },
                deserialize: json => JsonConvert.DeserializeObject<TestDocument>(json),
                documentLabel: "Test settings");

            Assert.Equal("default", result.Value.Name);
            Assert.Single(result.Warnings);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsWarningAndDefaultValueForUnreadablePath()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            LoadResult<TestDocument> result = JsonFileLoadHelper.Load(
                tempDirectory,
                createDefaultValue: () => new TestDocument { Name = "default" },
                deserialize: json => JsonConvert.DeserializeObject<TestDocument>(json),
                documentLabel: "Test settings");

            Assert.Equal("default", result.Value.Name);
            Assert.Single(result.Warnings);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"RevitGeoExporter-JsonLoadHelperTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestDocument
    {
        public string Name { get; set; } = string.Empty;
    }
}
