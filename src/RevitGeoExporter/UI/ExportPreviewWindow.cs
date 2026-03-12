using System;
using System.Windows;
using System.Windows.Forms.Integration;
using RevitGeoExporter.Export;
using WinForms = System.Windows.Forms;

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
            FormBorderStyle = WinForms.FormBorderStyle.None,
            Dock = WinForms.DockStyle.Fill,
        };

        WinForms.Panel hostPanel = new()
        {
            Dock = WinForms.DockStyle.Fill,
        };
        hostPanel.Controls.Add(_embeddedForm);
        _embeddedForm.Show();

        WindowsFormsHost host = new()
        {
            Child = hostPanel,
        };

        _window = new Window
        {
            Title = "Export Preview",
            Width = 1380,
            Height = 860,
            MinWidth = 980,
            MinHeight = 680,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = host,
        };

        _embeddedForm.FormClosed += (_, _) =>
        {
            if (_window.IsVisible)
            {
                _window.Close();
            }
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

