using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Validation;

public sealed class VerticalCirculationAudit
{
    public IReadOnlyList<VerticalCirculationAuditResult> Audit(ValidationViewSnapshot view, bool includeUnits, bool includeDetails, bool includeOpenings)
    {
        if (view is null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        List<VerticalCirculationAuditResult> results = new();
        if (includeUnits)
        {
            results.Add(CreateResult(view, "stairs", "unit", view.SourceStairsCount));
            results.Add(CreateResult(view, "escalator", "unit", view.SourceEscalatorCount));
            results.Add(CreateResult(view, "elevator", "unit", view.SourceElevatorCount));
        }

        if (includeDetails && view.SourceStairsCount > 0)
        {
            int detailCount = view.Features.Count(feature =>
                string.Equals(feature.FeatureType, "detail", StringComparison.OrdinalIgnoreCase));
            results.Add(new VerticalCirculationAuditResult("stairs", "detail", view.SourceStairsCount, detailCount));
        }

        if (includeOpenings && view.SourceElevatorCount > 0)
        {
            results.Add(CreateResult(view, "elevator", "opening", view.SourceElevatorCount));
        }

        return results;
    }

    private static VerticalCirculationAuditResult CreateResult(
        ValidationViewSnapshot view,
        string category,
        string featureType,
        int sourceCount)
    {
        int outputCount = view.Features.Count(feature =>
            string.Equals(feature.FeatureType, featureType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(feature.Category, category, StringComparison.OrdinalIgnoreCase));
        return new VerticalCirculationAuditResult(category, featureType, sourceCount, outputCount);
    }
}
