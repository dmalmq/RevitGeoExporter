using System;
using System.Collections.Generic;
using System.IO;
using RevitGeoExporter.Core.Assignments;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Assignments;

public sealed class FloorCategoryOverrideStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsOverridesPerProject()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            FloorCategoryOverrideStore store = new(tempDirectory);
            Dictionary<string, string> overrides = new()
            {
                ["Wrong Floor Name"] = "walkway",
                ["Bad Floor Name"] = "nonpublic",
            };

            store.Save("project-a", overrides);
            IReadOnlyDictionary<string, string> reloaded = store.Load("project-a");

            Assert.Equal(2, reloaded.Count);
            Assert.Equal("walkway", reloaded["Wrong Floor Name"]);
            Assert.Equal("nonpublic", reloaded["Bad Floor Name"]);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ClearOverride_RemovesOneProjectOverrideWithoutAffectingAnotherProject()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            FloorCategoryOverrideStore store = new(tempDirectory);
            store.SetOverride("project-a", "Wrong Floor Name", "walkway");
            store.SetOverride("project-b", "Wrong Floor Name", "nonpublic");

            store.ClearOverride("project-a", "Wrong Floor Name");

            IReadOnlyDictionary<string, string> projectA = store.Load("project-a");
            IReadOnlyDictionary<string, string> projectB = store.Load("project-b");
            Assert.Empty(projectA);
            Assert.Single(projectB);
            Assert.Equal("nonpublic", projectB["Wrong Floor Name"]);
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
            $"RevitGeoExporter-FloorOverrideTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
