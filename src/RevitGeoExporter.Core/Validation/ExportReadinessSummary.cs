using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Validation;

public sealed class ExportReadinessSummary
{
    public ExportReadinessSummary(
        int totalUnitCount,
        int unitsWithNameCount,
        int unitsWithAltNameCount,
        int missingStableIdCount,
        int duplicateStableIdCount,
        int unsupportedOpeningFamilyCount,
        int blockingIssueCount,
        int warningIssueCount,
        IReadOnlyList<ValidationMappingSuggestion> mappingSuggestions)
    {
        TotalUnitCount = Math.Max(0, totalUnitCount);
        UnitsWithNameCount = Math.Max(0, unitsWithNameCount);
        UnitsWithAltNameCount = Math.Max(0, unitsWithAltNameCount);
        MissingStableIdCount = Math.Max(0, missingStableIdCount);
        DuplicateStableIdCount = Math.Max(0, duplicateStableIdCount);
        UnsupportedOpeningFamilyCount = Math.Max(0, unsupportedOpeningFamilyCount);
        BlockingIssueCount = Math.Max(0, blockingIssueCount);
        WarningIssueCount = Math.Max(0, warningIssueCount);
        MappingSuggestions = mappingSuggestions ?? throw new ArgumentNullException(nameof(mappingSuggestions));
    }

    public int TotalUnitCount { get; }

    public int UnitsWithNameCount { get; }

    public int UnitsWithAltNameCount { get; }

    public int MissingStableIdCount { get; }

    public int DuplicateStableIdCount { get; }

    public int UnsupportedOpeningFamilyCount { get; }

    public int BlockingIssueCount { get; }

    public int WarningIssueCount { get; }

    public IReadOnlyList<ValidationMappingSuggestion> MappingSuggestions { get; }

    public int UnitsMissingNameCount => Math.Max(0, TotalUnitCount - UnitsWithNameCount);

    public int UnitsMissingAltNameCount => Math.Max(0, TotalUnitCount - UnitsWithAltNameCount);

    public int UnmappedAssignmentCount => MappingSuggestions.Sum(suggestion => suggestion.OccurrenceCount);
}
