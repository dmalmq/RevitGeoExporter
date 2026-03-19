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
        string roomCategoryParameterName,
        string? sourceDocumentKey = null,
        UnitGeometrySource unitGeometrySource = UnitGeometrySource.Unset,
        UnitAttributeSource unitAttributeSource = UnitAttributeSource.Unset,
        ValidationPolicyProfile? validationPolicyProfile = null)
    {
        TargetEpsg = targetEpsg;
        IncludeUnits = includeUnits;
        IncludeDetails = includeDetails;
        IncludeOpenings = includeOpenings;
        IncludeLevels = includeLevels;
        Views = views ?? throw new ArgumentNullException(nameof(views));
        UnitSource = unitSource;
        UnitGeometrySource = UnitExportSettingsResolver.ResolveGeometrySource(unitSource, unitGeometrySource);
        UnitAttributeSource = UnitExportSettingsResolver.ResolveAttributeSource(unitSource, UnitGeometrySource, unitAttributeSource);
        RoomCategoryParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName) ? "Name" : roomCategoryParameterName.Trim();
        string normalizedSourceDocumentKey = sourceDocumentKey?.Trim() ?? string.Empty;
        SourceDocumentKey = normalizedSourceDocumentKey.Length == 0 ? null : normalizedSourceDocumentKey;
        ActiveValidationPolicyProfile = validationPolicyProfile?.Clone() ?? ValidationPolicyProfile.CreateRecommendedProfile();
    }

    public int TargetEpsg { get; }

    public bool IncludeUnits { get; }

    public bool IncludeDetails { get; }

    public bool IncludeOpenings { get; }

    public bool IncludeLevels { get; }

    public IReadOnlyList<ValidationViewSnapshot> Views { get; }

    public UnitSource UnitSource { get; }

    public UnitGeometrySource UnitGeometrySource { get; }

    public UnitAttributeSource UnitAttributeSource { get; }

    public string RoomCategoryParameterName { get; }

    public string? SourceDocumentKey { get; }

    public ValidationPolicyProfile ActiveValidationPolicyProfile { get; }
}
