using System;
using System.Diagnostics;
using System.Threading;
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
    private readonly TextBlock _timingLabel;
    private readonly Button _cancelButton;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Stopwatch _stopwatch = new();

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
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };

        _timingLabel = new TextBlock
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = WpfDialogChrome.MutedTextBrush,
        };

        _cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _cancelButton.Click += (_, _) =>
        {
            _cancellationTokenSource.Cancel();
            _cancelButton.IsEnabled = false;
            _cancelButton.Content = "Cancelling...";
            Refresh();
        };

        Grid root = new()
        {
            Margin = new Thickness(16),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(_statusLabel);
        Grid.SetRow(_progressBar, 1);
        _progressBar.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(_progressBar);

        Grid infoRow = new();
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoRow.Children.Add(_countLabel);
        Grid.SetColumn(_timingLabel, 1);
        infoRow.Children.Add(_timingLabel);
        infoRow.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(infoRow, 2);
        root.Children.Add(infoRow);

        Grid.SetRow(_cancelButton, 3);
        root.Children.Add(_cancelButton);

        _window = new Window
        {
            Title = "Exporting GeoPackages",
            Width = 540,
            Height = 200,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Content = root,
        };
    }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public void Show()
    {
        _stopwatch.Start();
        _window.Show();
    }

    public void Refresh()
    {
        _window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
    }

    public void Close()
    {
        _stopwatch.Stop();
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
        _timingLabel.Text = FormatTiming(completed, total);
        Refresh();
    }

    public void Dispose()
    {
        if (_window.IsVisible)
        {
            _window.Close();
        }

        _cancellationTokenSource.Dispose();
    }

    private string FormatTiming(int completed, int total)
    {
        TimeSpan elapsed = _stopwatch.Elapsed;
        string elapsedText = FormatTimeSpan(elapsed);

        if (completed < 2 || completed >= total)
        {
            return $"Elapsed: {elapsedText}";
        }

        double secondsPerStep = elapsed.TotalSeconds / completed;
        int remainingSteps = total - completed;
        TimeSpan estimated = TimeSpan.FromSeconds(secondsPerStep * remainingSteps);
        return $"Elapsed: {elapsedText} | Remaining: ~{FormatTimeSpan(estimated)}";
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        return timeSpan.TotalHours >= 1
            ? $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}"
            : $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";
    }
}
