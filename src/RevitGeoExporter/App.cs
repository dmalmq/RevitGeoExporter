using System;
using System.Reflection;
using Autodesk.Revit.UI;
using RevitGeoExporter.Commands;
using RevitGeoExporter.Resources;
using RevitGeoExporter.UI;

namespace RevitGeoExporter;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }



        ExportDialogSettings savedSettings = new ExportDialogSettingsStore().Load();
        UiLanguage language = savedSettings.UiLanguage;

        EnsureRibbonTab(application, ProjectInfo.RibbonTabName);
        RibbonPanel panel = application.CreateRibbonPanel(
            ProjectInfo.RibbonTabName,
            LocalizedTextProvider.Get(language, "Ribbon.Panel.Export", "Export"));

        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        PushButtonData exportButton = new(
            name: "ExportGeoPackageButton",
            text: LocalizedTextProvider.Get(language, "Ribbon.Export.Text", "Export GeoPackage"),
            assemblyName: assemblyPath,
            className: typeof(ExportGeoPackageCommand).FullName);

        PushButton? button = panel.AddItem(exportButton) as PushButton;
        if (button != null)
        {
            button.ToolTip = $"{LocalizedTextProvider.Get(language, "Ribbon.Export.ToolTip", "Export IMDF GeoPackages from selected plan views.")} ({ProjectInfo.VersionTag})";
            button.LongDescription = LocalizedTextProvider.Get(
                language,
                "Ribbon.Export.Description",
                "Select floor or ceiling plan views and export one _unit, _detail, _opening, and _level GeoPackage per view.");
            button.LargeImage = RibbonIcons.CreateExportIcon(32);
            button.Image = RibbonIcons.CreateExportIcon(16);
        }

        PushButtonData helpButton = new(
            name: "ExportHelpButton",
            text: LocalizedTextProvider.Get(language, "Ribbon.Help.Text", "Help"),
            assemblyName: assemblyPath,
            className: typeof(OpenHelpCommand).FullName);

        PushButton? helpButtonControl = panel.AddItem(helpButton) as PushButton;
        if (helpButtonControl != null)
        {
            helpButtonControl.ToolTip = $"{LocalizedTextProvider.Get(language, "Ribbon.Help.ToolTip", "Open the offline help viewer.")} ({ProjectInfo.VersionTag})";
            helpButtonControl.LongDescription = LocalizedTextProvider.Get(
                language,
                "Ribbon.Help.Description",
                "Opens the built-in bilingual help viewer with offline documentation for export, preview, settings, and troubleshooting.");
            helpButtonControl.LargeImage = RibbonIcons.CreateHelpIcon(32);
            helpButtonControl.Image = RibbonIcons.CreateHelpIcon(16);
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

