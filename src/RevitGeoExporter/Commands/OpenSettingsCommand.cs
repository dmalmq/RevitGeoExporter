using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenSettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            ExportDialogSettingsStore store = new();
            ExportDialogSettings settings = store.Load();

            using SettingsDialog dialog = new(settings);
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return Result.Cancelled;
            }

            store.Save(dialog.BuildSettings());
            TaskDialog.Show(ProjectInfo.Name, "Settings saved.");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show(ProjectInfo.Name, $"Unable to open settings.\n\n{ex}");
            return Result.Failed;
        }
    }
}
