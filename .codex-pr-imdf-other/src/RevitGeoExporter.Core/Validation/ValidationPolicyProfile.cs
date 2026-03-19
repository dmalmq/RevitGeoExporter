using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Validation;

public sealed class ValidationPolicyProfile
{
    public const string RecommendedProfileName = "Recommended";
    public const string StrictProfileName = "Strict";
    public const string LenientProfileName = "Lenient";

    public ValidationPolicyProfile(string name, IReadOnlyList<ValidationPolicySetting>? settings = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? RecommendedProfileName : name.Trim();
        Settings = NormalizeSettings(settings);
    }

    public string Name { get; set; }

    public List<ValidationPolicySetting> Settings { get; set; }

    public ValidationPolicyProfile Clone()
    {
        return new ValidationPolicyProfile(Name, Settings.Select(setting => setting.Clone()).ToList());
    }

    public ValidationSeverity ResolveSeverity(ValidationCode code, ValidationSeverity defaultSeverity)
    {
        ValidationPolicyTarget? target = Map(code);
        return target.HasValue ? ResolveSeverity(target.Value, defaultSeverity) : defaultSeverity;
    }

    public static ValidationPolicyProfile CreateRecommendedProfile()
    {
        return new ValidationPolicyProfile(
            RecommendedProfileName,
            CreateRecommendedSettings());
    }

    public static ValidationPolicyProfile CreateStrictProfile()
    {
        return new ValidationPolicyProfile(
            StrictProfileName,
            CreateStrictSettings());
    }

    public static ValidationPolicyProfile CreateLenientProfile()
    {
        return new ValidationPolicyProfile(
            LenientProfileName,
            CreateLenientSettings());
    }

    public static List<ValidationPolicyProfile> NormalizeProfiles(IEnumerable<ValidationPolicyProfile>? profiles)
    {
        List<ValidationPolicyProfile> normalized = (profiles ?? Array.Empty<ValidationPolicyProfile>())
            .Where(profile => profile != null)
            .Select(profile => profile.Clone())
            .GroupBy(profile => string.IsNullOrWhiteSpace(profile.Name) ? RecommendedProfileName : profile.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                ValidationPolicyProfile profile = group.First();
                profile.Name = group.First().Name;
                profile.Settings = NormalizeSettings(profile.Settings);
                return profile;
            })
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.Add(CreateRecommendedProfile());
            normalized.Add(CreateStrictProfile());
            normalized.Add(CreateLenientProfile());
            return normalized;
        }

        EnsureBuiltInProfile(normalized, CreateRecommendedProfile());
        EnsureBuiltInProfile(normalized, CreateStrictProfile());
        EnsureBuiltInProfile(normalized, CreateLenientProfile());
        return normalized;
    }

    public static string ResolveActiveName(IEnumerable<ValidationPolicyProfile>? profiles, string? activeName)
    {
        List<ValidationPolicyProfile> normalized = NormalizeProfiles(profiles);
        string trimmed = activeName?.Trim() ?? string.Empty;
        if (trimmed.Length > 0 && normalized.Any(profile => string.Equals(profile.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized.First(profile => string.Equals(profile.Name, trimmed, StringComparison.OrdinalIgnoreCase)).Name;
        }

        return normalized[0].Name;
    }

    public ValidationSeverity ResolveSeverity(ValidationPolicyTarget target, ValidationSeverity defaultSeverity)
    {
        ValidationPolicySetting? setting = Settings.FirstOrDefault(candidate => candidate.Target == target);
        return setting?.Severity ?? defaultSeverity;
    }

    private static List<ValidationPolicySetting> NormalizeSettings(IEnumerable<ValidationPolicySetting>? settings)
    {
        Dictionary<ValidationPolicyTarget, ValidationPolicySetting> normalized = new();
        foreach (ValidationPolicySetting setting in settings ?? Array.Empty<ValidationPolicySetting>())
        {
            if (setting == null)
            {
                continue;
            }

            normalized[setting.Target] = setting.Clone();
        }

        foreach (ValidationPolicySetting defaultSetting in CreateRecommendedSettings())
        {
            if (!normalized.ContainsKey(defaultSetting.Target))
            {
                normalized[defaultSetting.Target] = defaultSetting.Clone();
            }
        }

        return normalized
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value)
            .ToList();
    }

    private static void EnsureBuiltInProfile(ICollection<ValidationPolicyProfile> profiles, ValidationPolicyProfile builtInProfile)
    {
        if (profiles.Any(profile => string.Equals(profile.Name, builtInProfile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        profiles.Add(builtInProfile);
    }

    private static ValidationPolicyTarget? Map(ValidationCode code)
    {
        return code switch
        {
            ValidationCode.MissingName => ValidationPolicyTarget.MissingNames,
            ValidationCode.UnassignedFloorCategory => ValidationPolicyTarget.UnmappedCategories,
            ValidationCode.DuplicateStableId => ValidationPolicyTarget.DuplicateStableIds,
            ValidationCode.LinkedElementUsingFallbackId => ValidationPolicyTarget.LinkedFallbackIds,
            ValidationCode.UnsupportedOpeningFamily => ValidationPolicyTarget.UnsupportedOpeningFamilies,
            ValidationCode.UnsnappedOpening => ValidationPolicyTarget.UnsnappedOpenings,
            _ => null,
        };
    }

    private static IReadOnlyList<ValidationPolicySetting> CreateRecommendedSettings()
    {
        return new[]
        {
            new ValidationPolicySetting(ValidationPolicyTarget.MissingNames, ValidationSeverity.Warning),
            new ValidationPolicySetting(ValidationPolicyTarget.UnmappedCategories, ValidationSeverity.Warning),
            new ValidationPolicySetting(ValidationPolicyTarget.DuplicateStableIds, ValidationSeverity.Error),
            new ValidationPolicySetting(ValidationPolicyTarget.LinkedFallbackIds, ValidationSeverity.Warning),
            new ValidationPolicySetting(ValidationPolicyTarget.UnsupportedOpeningFamilies, ValidationSeverity.Warning),
            new ValidationPolicySetting(ValidationPolicyTarget.GeoreferenceWarnings, ValidationSeverity.Warning),
            new ValidationPolicySetting(ValidationPolicyTarget.UnsnappedOpenings, ValidationSeverity.Warning),
        };
    }

    private static IReadOnlyList<ValidationPolicySetting> CreateStrictSettings()
    {
        return new[]
        {
            new ValidationPolicySetting(ValidationPolicyTarget.MissingNames, ValidationSeverity.Error),
            new ValidationPolicySetting(ValidationPolicyTarget.UnmappedCategories, ValidationSeverity.Error),
            new ValidationPolicySetting(ValidationPolicyTarget.DuplicateStableIds, ValidationSeverity.Error),
            new ValidationPolicySetting(ValidationPolicyTarget.LinkedFallbackIds, ValidationSeverity.Error),
            new ValidationPolicySetting(ValidationPolicyTarget.UnsupportedOpeningFamilies, ValidationSeverity.Error),
            new ValidationPolicySetting(ValidationPolicyTarget.GeoreferenceWarnings, ValidationSeverity.Error),
            new ValidationPolicySetting(ValidationPolicyTarget.UnsnappedOpenings, ValidationSeverity.Error),
        };
    }

    private static IReadOnlyList<ValidationPolicySetting> CreateLenientSettings()
    {
        return new[]
        {
            new ValidationPolicySetting(ValidationPolicyTarget.MissingNames, ValidationSeverity.Info),
            new ValidationPolicySetting(ValidationPolicyTarget.UnmappedCategories, ValidationSeverity.Info),
            new ValidationPolicySetting(ValidationPolicyTarget.DuplicateStableIds, ValidationSeverity.Warning),
            new ValidationPolicySetting(ValidationPolicyTarget.LinkedFallbackIds, ValidationSeverity.Info),
            new ValidationPolicySetting(ValidationPolicyTarget.UnsupportedOpeningFamilies, ValidationSeverity.Info),
            new ValidationPolicySetting(ValidationPolicyTarget.GeoreferenceWarnings, ValidationSeverity.Info),
            new ValidationPolicySetting(ValidationPolicyTarget.UnsnappedOpenings, ValidationSeverity.Info),
        };
    }
}
