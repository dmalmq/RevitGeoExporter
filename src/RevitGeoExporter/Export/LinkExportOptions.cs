using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Export;

public sealed class LinkExportOptions
{
    public bool IncludeLinkedModels { get; set; }

    public List<long> SelectedLinkInstanceIds { get; set; } = new();

    public LinkExportOptions Clone()
    {
        return new LinkExportOptions
        {
            IncludeLinkedModels = IncludeLinkedModels,
            SelectedLinkInstanceIds = SelectedLinkInstanceIds?.Distinct().ToList() ?? new List<long>(),
        };
    }

    public bool IncludesLinkInstance(long linkInstanceId)
    {
        if (!IncludeLinkedModels)
        {
            return false;
        }

        if (SelectedLinkInstanceIds == null || SelectedLinkInstanceIds.Count == 0)
        {
            return false;
        }

        return SelectedLinkInstanceIds.Contains(linkInstanceId);
    }
}
