using System;
using System.Windows;
using System.Windows.Controls;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

public sealed class ExportProgressForm : IDisposable
{
    private readonly Window _window;
    private readonly TextBlock _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _countLabel;

    public ExportProgressForm()
    {
        _statusLabel = new TextBlock
        {
            Text = "Preparing export...",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
        };

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Height = 18,
            Value = 0,
        };

        _countLabel = new TextBlock
        {
            Text = "0 / 1",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };

        Grid root = new()
        {
            Margin = new Thickness(16),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(_statusLabel);
        Grid.SetRow(_progressBar, 1);
        _progressBar.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(_progressBar);
        Grid.SetRow(_countLabel, 2);
        _countLabel.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_countLabel);

        _window = new Window
        {
            Title = "Exporting GeoPackages",
            Width = 540,
            Height = 160,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Content = root,
        };
    }

    public void Show()
    {
        _window.Show();
    }

    public void Refresh()
    {
        _window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
    }

    public void Close()
    {
        _window.Close();
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

    public void Dispose()
    {
        if (_window.IsVisible)
        {
            _window.Close();
        }
    }
}
