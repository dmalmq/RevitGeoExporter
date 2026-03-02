using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.Core;
using RevitGeoExporter.Export;

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

        IReadOnlyList<ViewPlan>? selectedViews = PromptForViewSelection(document);
        if (selectedViews == null || selectedViews.Count == 0)
        {
            return Result.Cancelled;
        }

        string? outputDirectory = PromptForOutputDirectory();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Result.Cancelled;
        }

        try
        {
            FloorGeoPackageExporter exporter = new(document);
            FloorGeoPackageExportResult result =
                exporter.ExportSelectedViews(
                    outputDirectory!,
                    targetEpsg: ProjectInfo.DefaultTargetEpsg,
                    selectedViews);

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

    private static string? PromptForOutputDirectory()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Select output folder for GeoPackage files",
            ShowNewFolderButton = true,
        };

        DialogResult result = dialog.ShowDialog();
        return result == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static IReadOnlyList<ViewPlan>? PromptForViewSelection(Document document)
    {
        ViewCollector collector = new();
        IReadOnlyList<ViewPlan> views = collector.GetExportablePlanViews(document);
        if (views.Count == 0)
        {
            TaskDialog.Show(ProjectInfo.Name, "No exportable plan views were found.");
            return null;
        }

        List<ViewSelectionItem> items = views
            .Select(view => new ViewSelectionItem(view))
            .ToList();

        using System.Windows.Forms.Form form = new()
        {
            Text = "Select Plan Views",
            Width = 600,
            Height = 650,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.Sizable,
        };

        Label header = new()
        {
            Dock = DockStyle.Top,
            Height = 38,
            Text = "Select plan views to export",
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
        };

        CheckedListBox list = new()
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            HorizontalScrollbar = true,
            IntegralHeight = false,
        };

        foreach (ViewSelectionItem item in items)
        {
            list.Items.Add(item, isChecked: true);
        }

        Button selectAllButton = new()
        {
            Text = "Select All",
            Width = 90,
            Height = 28,
        };
        selectAllButton.Click += (_, _) =>
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                list.SetItemChecked(i, true);
            }
        };

        Button clearAllButton = new()
        {
            Text = "Clear All",
            Width = 90,
            Height = 28,
        };
        clearAllButton.Click += (_, _) =>
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                list.SetItemChecked(i, false);
            }
        };

        Button okButton = new()
        {
            Text = "Export",
            Width = 90,
            Height = 28,
            DialogResult = DialogResult.OK,
        };

        Button cancelButton = new()
        {
            Text = "Cancel",
            Width = 90,
            Height = 28,
            DialogResult = DialogResult.Cancel,
        };

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6),
        };
        actions.Controls.Add(cancelButton);
        actions.Controls.Add(okButton);
        actions.Controls.Add(clearAllButton);
        actions.Controls.Add(selectAllButton);

        form.Controls.Add(list);
        form.Controls.Add(actions);
        form.Controls.Add(header);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        while (true)
        {
            DialogResult dialogResult = form.ShowDialog();
            if (dialogResult != DialogResult.OK)
            {
                return null;
            }

            List<ViewPlan> selected = new();
            foreach (object checkedItem in list.CheckedItems)
            {
                if (checkedItem is ViewSelectionItem selectedItem)
                {
                    selected.Add(selectedItem.View);
                }
            }

            if (selected.Count > 0)
            {
                return selected;
            }

            MessageBox.Show(
                "Select at least one plan view to export.",
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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

    private sealed class ViewSelectionItem
    {
        public ViewSelectionItem(ViewPlan view)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
        }

        public ViewPlan View { get; }

        public override string ToString()
        {
            string levelName = View.GenLevel?.Name ?? "<no level>";
            return $"{View.Name}  [Level: {levelName}]";
        }
    }
}
