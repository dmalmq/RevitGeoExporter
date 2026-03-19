using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Schema;

public sealed class SchemaProfile
{
    public const string CoreProfileName = "Core IMDF";

    public string Name { get; set; } = CoreProfileName;

    public List<CustomAttributeMapping> Mappings { get; set; } = new();

    public SchemaProfile Clone()
    {
        return new SchemaProfile
        {
            Name = NormalizeName(Name),
            Mappings = (Mappings ?? new List<CustomAttributeMapping>())
                .Where(mapping => mapping != null)
                .Select(mapping => mapping.Clone())
                .ToList(),
        };
    }

    public static SchemaProfile CreateCoreProfile()
    {
        return new SchemaProfile
        {
            Name = CoreProfileName,
            Mappings = new List<CustomAttributeMapping>(),
        };
    }

    public static IReadOnlyList<SchemaProfile> NormalizeProfiles(IEnumerable<SchemaProfile>? profiles)
    {
        List<SchemaProfile> normalized = (profiles ?? Array.Empty<SchemaProfile>())
            .Where(profile => profile != null)
            .Select(profile => profile.Clone())
            .Where(profile => NormalizeName(profile.Name).Length > 0)
            .GroupBy(profile => NormalizeName(profile.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                SchemaProfile profile = group.First();
                profile.Name = NormalizeName(profile.Name);
                return profile;
            })
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.Add(CreateCoreProfile());
        }

        return normalized;
    }

    public static SchemaProfile ResolveActive(IEnumerable<SchemaProfile>? profiles, string? activeProfileName)
    {
        IReadOnlyList<SchemaProfile> normalized = NormalizeProfiles(profiles);
        string normalizedName = NormalizeName(activeProfileName);
        if (normalizedName.Length > 0)
        {
            SchemaProfile? exact = normalized.FirstOrDefault(profile =>
                string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact.Clone();
            }
        }

        return normalized[0].Clone();
    }

    public static string ResolveActiveName(IEnumerable<SchemaProfile>? profiles, string? activeProfileName)
    {
        return ResolveActive(profiles, activeProfileName).Name;
    }

    private static string NormalizeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
