using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.Help;
using RevitGeoExporter.Resources;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenHelpCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UiLanguage language = CommandLanguageResolver.Resolve();

        try
        {
            HelpLauncher.Show(null, HelpTopic.GettingStarted, language, ProjectInfo.Name);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show(
                ProjectInfo.Name,
                $"{UiLanguageText.Get(language, "Help.Command.OpenFailed", "Unable to open help.")}\n\n{ex}");
            return Result.Failed;
        }
    }
}
