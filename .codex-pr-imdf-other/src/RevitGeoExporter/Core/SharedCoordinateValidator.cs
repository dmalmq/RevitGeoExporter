using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.Core;

public sealed class SharedCoordinateValidator
{
    private const double FeetTolerance = 0.01d;

    public SharedCoordinateValidationResult Validate(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        ModelCoordinateInfo coordinateInfo = new ModelCoordinateInfoReader().Read(document);
        List<SharedCoordinateValidationFinding> findings = new();

        ProjectLocation? location = document.ActiveProjectLocation;
        if (location == null)
        {
            findings.Add(new SharedCoordinateValidationFinding(
                ValidationSeverity.Warning,
                SharedCoordinateValidationCode.MissingActiveProjectLocation,
                "Active project location was not found; shared coordinate validation was skipped."));
            return BuildResult(coordinateInfo, findings);
        }

        ProjectPosition positionAtOrigin = location.GetProjectPosition(XYZ.Zero);
        bool locationAtOrigin =
            IsNearZero(positionAtOrigin.EastWest) &&
            IsNearZero(positionAtOrigin.NorthSouth);
        if (locationAtOrigin)
        {
            findings.Add(new SharedCoordinateValidationFinding(
                ValidationSeverity.Warning,
                SharedCoordinateValidationCode.SharedCoordinatesNearOrigin,
                "Shared coordinates appear to be near origin (East/West and North/South are approximately zero). Confirm Survey Point / shared coordinates are configured."));
        }

        BasePoint? surveyPoint = new FilteredElementCollector(document)
            .OfClass(typeof(BasePoint))
            .Cast<BasePoint>()
            .FirstOrDefault(basePoint => basePoint.IsShared);
        if (surveyPoint == null)
        {
            findings.Add(new SharedCoordinateValidationFinding(
                ValidationSeverity.Warning,
                SharedCoordinateValidationCode.MissingSurveyPoint,
                "Survey point was not found; shared coordinate validation is incomplete."));
            AddSourceCoordinateSystemFinding(coordinateInfo, findings);
            return BuildResult(coordinateInfo, findings);
        }

        XYZ surveyPointPosition = surveyPoint.Position;
        bool surveyAtOrigin =
            IsNearZero(surveyPointPosition.X) &&
            IsNearZero(surveyPointPosition.Y);
        if (surveyAtOrigin)
        {
            findings.Add(new SharedCoordinateValidationFinding(
                ValidationSeverity.Warning,
                SharedCoordinateValidationCode.SurveyPointNearOrigin,
                "Survey point is near (0,0). Export may not be georeferenced unless this is intentional."));
        }

        AddSourceCoordinateSystemFinding(coordinateInfo, findings);
        if (findings.Count == 0)
        {
            findings.Add(new SharedCoordinateValidationFinding(
                ValidationSeverity.Info,
                SharedCoordinateValidationCode.Ready,
                "Shared coordinate checks did not find any obvious georeference problems."));
        }

        return BuildResult(coordinateInfo, findings);
    }

    private static SharedCoordinateValidationResult BuildResult(
        ModelCoordinateInfo coordinateInfo,
        IReadOnlyList<SharedCoordinateValidationFinding> findings)
    {
        return new SharedCoordinateValidationResult(
            coordinateInfo,
            findings,
            coordinateInfo.ActiveProjectLocationName,
            coordinateInfo.SharedCoordinateSummary,
            coordinateInfo.ResolvedSourceEpsg,
            coordinateInfo.ResolvedSourceLabel,
            coordinateInfo.SurveyPointSharedCoordinates);
    }

    private static void AddSourceCoordinateSystemFinding(
        ModelCoordinateInfo coordinateInfo,
        ICollection<SharedCoordinateValidationFinding> findings)
    {
        if (coordinateInfo.ResolvedSourceEpsg.HasValue || coordinateInfo.SiteCoordinateSystemDefinition.Length > 0)
        {
            findings.Add(new SharedCoordinateValidationFinding(
                ValidationSeverity.Info,
                SharedCoordinateValidationCode.SourceCoordinateSystemResolved,
                $"Source coordinate system resolved as '{coordinateInfo.ResolvedSourceLabel}'."));
            return;
        }

        findings.Add(new SharedCoordinateValidationFinding(
            ValidationSeverity.Warning,
            SharedCoordinateValidationCode.SourceCoordinateSystemUnresolved,
            "Source coordinate system could not be resolved from the active Revit site settings."));
    }

    private static bool IsNearZero(double value)
    {
        return Math.Abs(value) <= FeetTolerance;
    }
}

public enum SharedCoordinateValidationCode
{
    Ready = 0,
    MissingActiveProjectLocation = 1,
    SharedCoordinatesNearOrigin = 2,
    MissingSurveyPoint = 3,
    SurveyPointNearOrigin = 4,
    SourceCoordinateSystemResolved = 5,
    SourceCoordinateSystemUnresolved = 6,
}

public sealed class SharedCoordinateValidationFinding
{
    public SharedCoordinateValidationFinding(
        ValidationSeverity severity,
        SharedCoordinateValidationCode code,
        string message)
    {
        Severity = severity;
        Code = code;
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("A message is required.", nameof(message))
            : message.Trim();
    }

    public ValidationSeverity Severity { get; }

    public SharedCoordinateValidationCode Code { get; }

    public string Message { get; }

    public SharedCoordinateValidationFinding WithSeverity(ValidationSeverity severity)
    {
        return new SharedCoordinateValidationFinding(severity, Code, Message);
    }
}

public sealed class SharedCoordinateValidationResult
{
    public SharedCoordinateValidationResult(
        ModelCoordinateInfo coordinateInfo,
        IReadOnlyList<SharedCoordinateValidationFinding> findings,
        string activeProjectLocationName,
        string sharedCoordinateSummary,
        int? resolvedSourceEpsg,
        string resolvedSourceLabel,
        Point2D? surveyPointSharedCoordinates)
    {
        CoordinateInfo = coordinateInfo ?? throw new ArgumentNullException(nameof(coordinateInfo));
        Findings = findings ?? throw new ArgumentNullException(nameof(findings));
        ActiveProjectLocationName = string.IsNullOrWhiteSpace(activeProjectLocationName) ? string.Empty : activeProjectLocationName.Trim();
        SharedCoordinateSummary = string.IsNullOrWhiteSpace(sharedCoordinateSummary) ? string.Empty : sharedCoordinateSummary.Trim();
        ResolvedSourceEpsg = resolvedSourceEpsg;
        ResolvedSourceLabel = string.IsNullOrWhiteSpace(resolvedSourceLabel) ? string.Empty : resolvedSourceLabel.Trim();
        SurveyPointSharedCoordinates = surveyPointSharedCoordinates;
    }

    public ModelCoordinateInfo CoordinateInfo { get; }

    public IReadOnlyList<SharedCoordinateValidationFinding> Findings { get; }

    public string ActiveProjectLocationName { get; }

    public string SharedCoordinateSummary { get; }

    public int? ResolvedSourceEpsg { get; }

    public string ResolvedSourceLabel { get; }

    public Point2D? SurveyPointSharedCoordinates { get; }

    public IReadOnlyList<string> Warnings => Findings
        .Where(finding => finding.Severity != ValidationSeverity.Info)
        .Select(finding => finding.Message)
        .ToList();

    public bool HasWarnings => Warnings.Count > 0;

    public SharedCoordinateValidationResult ApplyPolicy(ValidationPolicyProfile? policyProfile)
    {
        ValidationPolicyProfile effectivePolicy = policyProfile?.Clone() ?? ValidationPolicyProfile.CreateRecommendedProfile();
        return new SharedCoordinateValidationResult(
            CoordinateInfo,
            Findings.Select(finding => finding.WithSeverity(
                finding.Severity == ValidationSeverity.Info
                    ? ValidationSeverity.Info
                    : effectivePolicy.ResolveSeverity(ValidationPolicyTarget.GeoreferenceWarnings, finding.Severity))).ToList(),
            ActiveProjectLocationName,
            SharedCoordinateSummary,
            ResolvedSourceEpsg,
            ResolvedSourceLabel,
            SurveyPointSharedCoordinates);
    }
}
