using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Validation;

public sealed class ExportValidationRequest
{
    public ExportValidationRequest(
        int targetEpsg,
        bool includeUnits,
        bool includeDetails,
        bool includeOpenings,
        bool includeLevels,
        IReadOnlyList<ValidationViewSnapshot> views,
        UnitSource unitSource,
        string roomCategoryParameterName)
    {
        TargetEpsg = targetEpsg;
        IncludeUnits = includeUnits;
        IncludeDetails = includeDetails;
        IncludeOpenings = includeOpenings;
        IncludeLevels = includeLevels;
        Views = views ?? throw new ArgumentNullException(nameof(views));
        UnitSource = unitSource;
        RoomCategoryParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName) ? "Name" : roomCategoryParameterName.Trim();
    }

    public int TargetEpsg { get; }

    public bool IncludeUnits { get; }

    public bool IncludeDetails { get; }

    public bool IncludeOpenings { get; }

    public bool IncludeLevels { get; }

    public IReadOnlyList<ValidationViewSnapshot> Views { get; }

    public UnitSource UnitSource { get; }

    public string RoomCategoryParameterName { get; }
}
