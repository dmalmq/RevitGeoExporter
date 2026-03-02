using System;
using System.Reflection;
using Autodesk.Revit.UI;
using RevitGeoExporter.Commands;

namespace RevitGeoExporter;

public sealed class App : IExternalApplication
{
    private const string RibbonPanelName = "Export";

    public Result OnStartup(UIControlledApplication application)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        EnsureRibbonTab(application, ProjectInfo.RibbonTabName);
        RibbonPanel panel = application.CreateRibbonPanel(ProjectInfo.RibbonTabName, RibbonPanelName);

        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        PushButtonData exportButton = new(
            name: "ExportGeoPackageButton",
            text: "Export GeoPackage",
            assemblyName: assemblyPath,
            className: typeof(ExportGeoPackageCommand).FullName);

        PushButton? button = panel.AddItem(exportButton) as PushButton;
        if (button != null)
        {
            button.ToolTip = "Export IMDF GeoPackages from selected plan views.";
            button.LongDescription =
                "Select floor/ceiling plan views and export one _unit, _detail, _opening, and _level GeoPackage per view.";
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }

    private static void EnsureRibbonTab(UIControlledApplication application, string tabName)
    {
        try
        {
            application.CreateRibbonTab(tabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Tab already exists in this Revit session.
        }
    }
}
