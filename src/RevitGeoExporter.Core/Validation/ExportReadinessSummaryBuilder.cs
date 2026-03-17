using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Validation;

public sealed class ExportReadinessSummaryBuilder
{
    public ExportReadinessSummary Build(
        ExportValidationRequest request,
        ExportValidationResult validationResult,
        ZoneCatalog zoneCatalog,
        int additionalBlockingIssueCount = 0,
        int additionalWarningIssueCount = 0)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (validationResult is null)
        {
            throw new ArgumentNullException(nameof(validationResult));
        }

        if (zoneCatalog is null)
        {
            throw new ArgumentNullException(nameof(zoneCatalog));
        }

        List<ExportFeatureValidationSnapshot> unitFeatures = request.Views
            .SelectMany(view => view.Features)
            .Where(feature => string.Equals(feature.FeatureType, "unit", StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<ValidationMappingSuggestion> mappingSuggestions = request.Views
            .SelectMany(view => view.Features)
            .Where(feature => feature.IsUnassigned && !string.IsNullOrWhiteSpace(feature.AssignmentMappingKey))
            .GroupBy(
                feature => BuildGroupingKey(feature),
                StringComparer.Ordinal)
            .Select(group =>
            {
                ExportFeatureValidationSnapshot first = group.First();
                string sourceKind = first.AssignmentSourceKind ?? "unit";
                string mappingKey = first.AssignmentMappingKey ?? string.Empty;
                string? parameterName = first.AssignmentParameterName;
                string? parsedCandidate = first.AssignmentParsedCandidate;
                return new ValidationMappingSuggestion(
                sourceKind,
                mappingKey,
                group.Count(),
                parameterName,
                parsedCandidate,
                SuggestCategory(zoneCatalog, parsedCandidate, mappingKey));
            })
            .OrderByDescending(suggestion => suggestion.OccurrenceCount)
            .ThenBy(suggestion => suggestion.MappingKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ExportReadinessSummary(
            unitFeatures.Count,
            unitFeatures.Count(feature => !string.IsNullOrWhiteSpace(feature.Name)),
            unitFeatures.Count(feature => !string.IsNullOrWhiteSpace(feature.AltName)),
            validationResult.Issues.Count(issue => issue.Code == ValidationCode.MissingStableId),
            validationResult.Issues.Count(issue => issue.Code == ValidationCode.DuplicateStableId),
            request.Views.Sum(view => view.UnsupportedOpenings.Count),
            validationResult.Issues.Count(issue => issue.Severity == ValidationSeverity.Error) + Math.Max(0, additionalBlockingIssueCount),
            validationResult.Issues.Count(issue => issue.Severity == ValidationSeverity.Warning) + Math.Max(0, additionalWarningIssueCount),
            mappingSuggestions);
    }

    private static string? SuggestCategory(ZoneCatalog zoneCatalog, string? parsedCandidate, string mappingKey)
    {
        string candidate = Normalize(parsedCandidate);
        if (candidate.Length == 0)
        {
            candidate = Normalize(mappingKey);
        }

        if (candidate.Length == 0)
        {
            return null;
        }

        if (zoneCatalog.TryGetZoneInfo(candidate, out ZoneInfo exactMatch))
        {
            return exactMatch.Category;
        }

        foreach (KeyValuePair<string, ZoneInfo> entry in zoneCatalog.ZoneLookup)
        {
            string known = Normalize(entry.Key);
            if (known.Length == 0)
            {
                continue;
            }

            if (ContainsIgnoreCase(candidate, known) ||
                ContainsIgnoreCase(known, candidate))
            {
                return entry.Value.Category;
            }
        }

        return null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Replace('\u3000', ' ');
    }

    private static string BuildGroupingKey(ExportFeatureValidationSnapshot feature)
    {
        return string.Join(
            "|",
            feature.AssignmentSourceKind ?? "unit",
            feature.AssignmentMappingKey ?? string.Empty,
            feature.AssignmentParameterName ?? string.Empty,
            feature.AssignmentParsedCandidate ?? string.Empty);
    }

    private static bool ContainsIgnoreCase(string left, string right)
    {
        return left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
