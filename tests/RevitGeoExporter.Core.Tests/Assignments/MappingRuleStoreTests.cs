using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Utilities;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Assignments;

public sealed class MappingRuleStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsTypedRules()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            MappingRuleStore store = new(tempDirectory);
            ProjectMappingRules rules = ProjectMappingRules.Create(
                new Dictionary<string, string> { ["Wrong Floor"] = "walkway" },
                new Dictionary<string, string> { ["Custom Family"] = "retail" },
                new[] { "EV扉" });

            store.Save("project-a", rules);
            ProjectMappingRules reloaded = store.Load("project-a");

            Assert.Equal("walkway", reloaded.FloorCategoryOverrides["Wrong Floor"]);
            Assert.Equal("retail", reloaded.FamilyCategoryOverrides["Custom Family"]);
            Assert.Contains("EV扉", reloaded.AcceptedOpeningFamilies);
            Assert.Equal(3, reloaded.Rules.Count);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void LoadWithDiagnostics_FallsBackToLegacyFilesWhenCombinedFileIsMissing()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            string legacyBase = Path.Combine(tempDirectory, "legacy");
            Directory.CreateDirectory(Path.Combine(legacyBase, "floor-category-overrides"));
            Directory.CreateDirectory(Path.Combine(legacyBase, "family-category-overrides"));
            Directory.CreateDirectory(Path.Combine(legacyBase, "accepted-opening-families"));

            string fileName = ComputeProjectFileName("project-a");
            File.WriteAllText(
                Path.Combine(legacyBase, "floor-category-overrides", fileName),
                JsonConvert.SerializeObject(new { projectKey = "project-a", overrides = new Dictionary<string, string> { ["Wrong Floor"] = "walkway" } }));
            File.WriteAllText(
                Path.Combine(legacyBase, "family-category-overrides", fileName),
                JsonConvert.SerializeObject(new { projectKey = "project-a", overrides = new Dictionary<string, string> { ["Custom Family"] = "retail" } }));
            File.WriteAllText(
                Path.Combine(legacyBase, "accepted-opening-families", fileName),
                JsonConvert.SerializeObject(new { projectKey = "project-a", families = new[] { "EV扉" } }));

            MappingRuleStore store = new(Path.Combine(tempDirectory, "combined"), legacyBase);

            LoadResult<ProjectMappingRules> result = store.LoadWithDiagnostics("project-a");

            Assert.Empty(result.Warnings);
            Assert.Equal("walkway", result.Value.FloorCategoryOverrides["Wrong Floor"]);
            Assert.Equal("retail", result.Value.FamilyCategoryOverrides["Custom Family"]);
            Assert.Contains("EV扉", result.Value.AcceptedOpeningFamilies);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ImportFromFile_LoadsRulesFromExportedDocument()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            MappingRuleStore store = new(tempDirectory);
            ProjectMappingRules rules = ProjectMappingRules.Create(
                new Dictionary<string, string> { ["Wrong Floor"] = "walkway" },
                new Dictionary<string, string> { ["Custom Family"] = "retail" },
                new[] { "EV扉" });
            string exportPath = Path.Combine(tempDirectory, "rules.json");

            store.ExportToFile("project-a", rules, exportPath);
            LoadResult<ProjectMappingRules> imported = store.ImportFromFile(exportPath);

            Assert.Empty(imported.Warnings);
            Assert.Equal("walkway", imported.Value.ResolveFloorCategory("Wrong Floor").ResolvedValue);
            Assert.Equal("retail", imported.Value.ResolveFamilyCategory("Custom Family").ResolvedValue);
            Assert.True(imported.Value.ResolveAcceptedOpeningFamily("EV扉").Matched);
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
            $"RevitGeoExporter-MappingRuleTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ComputeProjectFileName(string projectKey)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(projectKey));
        return $"{BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant()}.json";
    }
}
