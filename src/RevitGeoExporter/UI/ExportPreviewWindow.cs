using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

internal sealed class ExportPreviewWindow : IDisposable
{
    private readonly Window _window;
    private readonly ExportPreviewForm _embeddedForm;

    public ExportPreviewWindow(ExportPreviewRequest request, ExportPreviewService previewService)
    {
        _embeddedForm = new ExportPreviewForm(request, previewService)
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            Dock = DockStyle.Fill,
        };

        _embeddedForm.FormClosed += (_, _) =>
        {
            if (_window.IsVisible)
            {
                _window.Close();
            }
        };

        Panel hostPanel = new()
        {
            Dock = DockStyle.Fill,
        };
        hostPanel.Controls.Add(_embeddedForm);
        _embeddedForm.Show();

        WindowsFormsHost host = new()
        {
            Child = hostPanel,
        };

        Grid layout = new();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        TextBlock banner = new()
        {
            Text = "WPF preview shell enabled (phase 4 in progress).",
            Margin = new Thickness(12, 8, 12, 8),
        };
        layout.Children.Add(banner);

        Grid.SetRow(host, 1);
        layout.Children.Add(host);

        _window = new Window
        {
            Title = "Export Preview",
            Width = 1380,
            Height = 860,
            MinWidth = 980,
            MinHeight = 680,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = layout,
        };
    }

    public bool? ShowDialog()
    {
        return _window.ShowDialog();
    }

    public void Dispose()
    {
        if (_embeddedForm is not null && !_embeddedForm.IsDisposed)
        {
            _embeddedForm.Dispose();
        }

        if (_window.IsVisible)
        {
            _window.Close();
        }
    }
}
