using System;
using System.Drawing;
using System.Windows.Forms;
using RevitGeoExporter.Export;
using WinFormsForm = System.Windows.Forms.Form;

namespace RevitGeoExporter.UI;

public sealed class ExportProgressForm : WinFormsForm
{
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _countLabel = new();

    public ExportProgressForm()
    {
        Text = "Exporting GeoPackages";
        Width = 540;
        Height = 160;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = "Preparing export...";
        root.Controls.Add(_statusLabel, 0, 0);

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1;
        _progressBar.Value = 0;
        root.Controls.Add(_progressBar, 0, 1);

        _countLabel.Dock = DockStyle.Fill;
        _countLabel.TextAlign = ContentAlignment.MiddleRight;
        _countLabel.Text = "0 / 1";
        root.Controls.Add(_countLabel, 0, 2);

        Controls.Add(root);
    }

    public void UpdateProgress(ExportProgressUpdate update)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        int total = Math.Max(1, update.TotalSteps);
        int completed = Math.Max(0, Math.Min(update.CompletedSteps, total));

        _statusLabel.Text = string.IsNullOrWhiteSpace(update.StatusText)
            ? "Exporting..."
            : update.StatusText;
        _progressBar.Maximum = total;
        _progressBar.Value = completed;
        _countLabel.Text = $"{completed} / {total}";

        Refresh();
    }
}
