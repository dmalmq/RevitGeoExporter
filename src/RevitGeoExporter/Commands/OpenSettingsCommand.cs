using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Export;
using RevitGeoExporter.Resources;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenSettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Document? document = commandData.Application.ActiveUIDocument?.Document;
            if (document == null)
            {
                TaskDialog.Show(
                    ProjectInfo.Name,
                    LocalizedTextProvider.Get(UiLanguage.English, "Command.ActiveDocumentRequired", "An active document is required."));
                return Result.Failed;
            }

            string projectKey = DocumentProjectKeyBuilder.Create(document);
            SettingsBundle bundle = new(projectKey);
            SettingsBundleSnapshot snapshot = bundle.Load();

            using SettingsHubForm dialog = new(projectKey, bundle, snapshot, ZoneCatalog.CreateDefault());
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                ? Result.Succeeded
                : Result.Cancelled;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show(
                ProjectInfo.Name,
                $"{LocalizedTextProvider.Get(UiLanguage.English, "Command.OpenSettingsFailed", "Unable to open settings.")}\n\n{ex}");
            return Result.Failed;
        }
    }
}
