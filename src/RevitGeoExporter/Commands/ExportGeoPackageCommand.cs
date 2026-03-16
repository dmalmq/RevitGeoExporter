using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.Core;
using RevitGeoExporter.Export;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ExportGeoPackageCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (commandData is null)
        {
            throw new ArgumentNullException(nameof(commandData));
        }

        UiLanguage language = CommandLanguageResolver.Resolve();
        UIDocument? uiDocument = commandData.Application?.ActiveUIDocument;
        Document? document = uiDocument?.Document;
        if (document is null)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                UiLanguageText.Get(language, "Command.ActiveDocumentRequired", "An active document is required."));
            return Result.Failed;
        }

        ViewCollector collector = new();
        IReadOnlyList<ViewPlan> views = collector.GetExportablePlanViews(document);
        if (views.Count == 0)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                UiLanguageText.Get(language, "Command.NoExportableViews", "No exportable plan views were found."));
            return Result.Failed;
        }

        string projectKey = DocumentProjectKeyBuilder.Create(document);
        SettingsBundle bundle = new(projectKey);
        SettingsBundleSnapshot bundleSnapshot = bundle.Load();
        ExportDialogSettings settings = bundleSnapshot.GlobalSettings;
        ExportProfileStore profileStore = new();
        IReadOnlyList<string> availableFloorTypeNames = GetAvailableFloorTypeNames(document);
        ModelCoordinateInfo coordinateInfo = new ModelCoordinateInfoReader().Read(document);

        bool forceLegacyWinForms = string.Equals(
            Environment.GetEnvironmentVariable("REVIT_GEOEXPORTER_FORCE_LEGACY_WINFORMS_UI"),
            "1",
            StringComparison.Ordinal);

        bool useWpfDialog = !forceLegacyWinForms;
        bool useWpfPreviewWindow = !forceLegacyWinForms;
        ExportWorkflowCoordinator workflow = new(
            document,
            projectKey,
            availableFloorTypeNames,
            profileStore,
            useWpfPreviewWindow);

        ExportDialogResult? request = useWpfDialog
            ? ShowWpfDialog(views, settings, bundleSnapshot, bundle, coordinateInfo, workflow)
            : ShowLegacyDialog(views, settings, bundleSnapshot, bundle, coordinateInfo, workflow);
        if (request == null || request.SelectedViews.Count == 0)
        {
            return Result.Cancelled;
        }

        return workflow.RunExport(request, coordinateInfo, ref message);
    }

    private static ExportDialogResult? ShowWpfDialog(
        IReadOnlyList<ViewPlan> views,
        ExportDialogSettings settings,
        SettingsBundleSnapshot bundleSnapshot,
        SettingsBundle bundle,
        ModelCoordinateInfo coordinateInfo,
        ExportWorkflowCoordinator workflow)
    {
        using ExportDialogWpf dialog = new(
            views,
            settings,
            bundleSnapshot.Profiles,
            workflow.SaveProfile,
            workflow.RenameProfile,
            workflow.DeleteProfile,
            workflow.OpenMappings,
            workflow.ShowPreview,
            coordinateInfo);

        if (dialog.ShowDialog() != DialogResult.OK || dialog.Result == null)
        {
            return null;
        }

        bundle.SaveGlobalSettings(dialog.BuildSettings());
        return dialog.Result;
    }

    private static ExportDialogResult? ShowLegacyDialog(
        IReadOnlyList<ViewPlan> views,
        ExportDialogSettings settings,
        SettingsBundleSnapshot bundleSnapshot,
        SettingsBundle bundle,
        ModelCoordinateInfo coordinateInfo,
        ExportWorkflowCoordinator workflow)
    {
        using ExportDialog dialog = new(
            views,
            settings,
            bundleSnapshot.Profiles,
            workflow.SaveProfile,
            workflow.RenameProfile,
            workflow.DeleteProfile,
            workflow.OpenMappings,
            previewRequest => workflow.ShowPreview(previewRequest),
            coordinateInfo);

        if (dialog.ShowDialog() != DialogResult.OK || dialog.Result == null)
        {
            return null;
        }

        bundle.SaveGlobalSettings(dialog.BuildSettings());
        return dialog.Result;
    }

    private static IReadOnlyList<string> GetAvailableFloorTypeNames(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Select(type => type.Name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
