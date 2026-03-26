using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RevitGeoExporter.UI;

public sealed class BatchExportResultForm : IDisposable
{
    private readonly Window _window;

    public BatchExportResultForm(IReadOnlyList<BatchJobResultRow> jobs)
    {
        if (jobs is null)
        {
            throw new ArgumentNullException(nameof(jobs));
        }

        int succeededCount = jobs.Count(j => j.Succeeded);
        int failedCount = jobs.Count - succeededCount;

        Grid root = new() { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string summaryText = failedCount > 0
            ? $"Batch export completed: {succeededCount} succeeded, {failedCount} failed."
            : $"Batch export completed: {succeededCount} succeeded.";
        TextBlock header = new()
        {
            Text = summaryText,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(header);

        Style statusCellStyle = new(typeof(TextBlock));
        statusCellStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(BatchJobResultRow.Status)),
            Value = "Failed",
            Setters = { new Setter(TextBlock.ForegroundProperty, Brushes.DarkRed) },
        });
        statusCellStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(BatchJobResultRow.Status)),
            Value = "Succeeded",
            Setters = { new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0, 120, 0))) },
        });

        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            CanUserResizeColumns = true,
            ItemsSource = jobs,
        };

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Profile",
            Binding = new Binding(nameof(BatchJobResultRow.ProfileName)),
            Width = new DataGridLength(0.2, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status",
            Binding = new Binding(nameof(BatchJobResultRow.Status)),
            Width = new DataGridLength(0.12, DataGridLengthUnitType.Star),
            ElementStyle = statusCellStyle,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Written",
            Binding = new Binding(nameof(BatchJobResultRow.Written)),
            Width = new DataGridLength(0.1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Reused",
            Binding = new Binding(nameof(BatchJobResultRow.Reused)),
            Width = new DataGridLength(0.1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Warnings",
            Binding = new Binding(nameof(BatchJobResultRow.Warnings)),
            Width = new DataGridLength(0.1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Message",
            Binding = new Binding(nameof(BatchJobResultRow.Message)),
            Width = new DataGridLength(0.38, DataGridLengthUnitType.Star),
        });

        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        Button closeButton = new()
        {
            Content = "Close",
            Width = 100,
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            IsDefault = true,
            IsCancel = true,
        };
        closeButton.Click += (_, _) =>
        {
            _window.DialogResult = true;
            _window.Close();
        };
        Grid.SetRow(closeButton, 2);
        root.Children.Add(closeButton);

        _window = new Window
        {
            Title = "Batch Export Results",
            Width = 860,
            Height = 420,
            MinWidth = 640,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = root,
        };
    }

    public bool? ShowDialog() => _window.ShowDialog();

    public void Dispose()
    {
        if (_window.IsVisible)
        {
            _window.Close();
        }
    }
}

public sealed class BatchJobResultRow
{
    public BatchJobResultRow(string profileName, bool succeeded, int written, int reused, int warnings, string message)
    {
        ProfileName = profileName;
        Succeeded = succeeded;
        Status = succeeded ? "Succeeded" : "Failed";
        Written = written;
        Reused = reused;
        Warnings = warnings;
        Message = message;
    }

    public string ProfileName { get; }

    public bool Succeeded { get; }

    public string Status { get; }

    public int Written { get; }

    public int Reused { get; }

    public int Warnings { get; }

    public string Message { get; }
}
