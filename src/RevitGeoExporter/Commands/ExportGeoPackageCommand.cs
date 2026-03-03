using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        UIDocument? uiDocument = commandData.Application?.ActiveUIDocument;
        Document? document = uiDocument?.Document;
        if (document is null)
        {
            TaskDialog.Show(ProjectInfo.Name, "An active document is required.");
            return Result.Failed;
        }

        ViewCollector collector = new();
        IReadOnlyList<ViewPlan> views = collector.GetExportablePlanViews(document);
        if (views.Count == 0)
        {
            TaskDialog.Show(ProjectInfo.Name, "No exportable plan views were found.");
            return Result.Failed;
        }

        ExportDialogSettingsStore settingsStore = new();
        ExportDialogSettings settings = settingsStore.Load();

        ExportDialogResult? request = null;
        using (ExportDialog dialog = new(views, settings))
        {
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || dialog.Result == null)
            {
                return Result.Cancelled;
            }

            request = dialog.Result;
            settingsStore.Save(dialog.BuildSettings());
        }

        if (request == null || request.SelectedViews.Count == 0)
        {
            return Result.Cancelled;
        }

        try
        {
            FloorGeoPackageExporter exporter = new(document);
            FloorGeoPackageExportResult result;
            using (ExportProgressForm progressForm = new())
            {
                progressForm.Show();
                System.Windows.Forms.Application.DoEvents();

                result = exporter.ExportSelectedViews(
                    request.OutputDirectory,
                    targetEpsg: request.TargetEpsg,
                    selectedViews: request.SelectedViews,
                    featureTypes: request.FeatureTypes,
                    splitUnitsByWalls: request.SplitUnitsByWalls,
                    progressCallback: update =>
                    {
                        progressForm.UpdateProgress(update);
                        System.Windows.Forms.Application.DoEvents();
                    });

                progressForm.Close();
            }

            TaskDialog.Show(ProjectInfo.Name, BuildSummary(result));
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show(ProjectInfo.Name, $"Export failed.\n\n{ex}");
            return Result.Failed;
        }
    }

    private static string BuildSummary(FloorGeoPackageExportResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine("GeoPackage export completed.");
        builder.AppendLine();
        int viewCount = result.ViewResults
            .Select(x => x.ViewName)
            .Distinct(StringComparer.Ordinal)
            .Count();
        builder.AppendLine($"Views exported: {viewCount}");
        builder.AppendLine($"Files exported: {result.ViewResults.Count}");
        builder.AppendLine($"Total features: {result.ViewResults.Sum(x => x.FeatureCount)}");
        builder.AppendLine();

        foreach (ViewExportResult export in result.ViewResults)
        {
            builder.AppendLine(
                $"{export.ViewName} ({export.LevelName}) [{export.FeatureType}]: {export.FeatureCount} features");
        }

        if (result.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings:");
            foreach (string warning in result.Warnings.Take(15))
            {
                builder.AppendLine($"- {warning}");
            }

            if (result.Warnings.Count > 15)
            {
                builder.AppendLine($"- ... and {result.Warnings.Count - 15} more");
            }
        }

        return builder.ToString();
    }
}
