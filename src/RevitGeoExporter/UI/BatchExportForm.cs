using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Newtonsoft.Json;
using RevitGeoExporter.Export;
using WinForms = System.Windows.Forms;

namespace RevitGeoExporter.UI;

internal sealed class BatchExportForm : IDisposable
{
    private readonly UiLanguage _language;
    private readonly Window _window;
    private readonly ObservableCollection<BatchJobRow> _jobs = new();
    private readonly ComboBox _profileComboBox = new();
    private readonly TextBox _outputOverrideTextBox = new();
    private readonly DataGrid _jobsGrid = new();

    public BatchExportForm(IReadOnlyList<ExportProfile> profiles, UiLanguage language)
    {
        _language = language;
        Profiles = (profiles ?? Array.Empty<ExportProfile>())
            .Where(profile => profile != null)
            .OrderBy(profile => profile.Scope)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _window = new Window
        {
            Width = 900,
            Height = 560,
            MinWidth = 760,
            MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Title = T("Batch Export", "バッチ出力"),
            Content = BuildLayout(),
        };
    }

    public IReadOnlyList<ExportProfile> Profiles { get; }

    public ExportJobManifest? Result { get; private set; }

    public WinForms.DialogResult ShowDialog(WinForms.IWin32Window? owner = null)
    {
        if (owner != null)
        {
            new WindowInteropHelper(_window).Owner = owner.Handle;
        }

        bool? result = _window.ShowDialog();
        return result == true ? WinForms.DialogResult.OK : WinForms.DialogResult.Cancel;
    }

    public void Dispose()
    {
        if (_window.IsVisible)
        {
            _window.Close();
        }
    }

    private UIElement BuildLayout()
    {
        Grid root = new() { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(BuildAddRow());

        Grid queueGrid = new();
        queueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        queueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(queueGrid, 1);

        _jobsGrid.AutoGenerateColumns = false;
        _jobsGrid.CanUserAddRows = false;
        _jobsGrid.CanUserDeleteRows = false;
        _jobsGrid.ItemsSource = _jobs;
        _jobsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Profile", "プロファイル"),
            Binding = new System.Windows.Data.Binding(nameof(BatchJobRow.ProfileName)),
            Width = new DataGridLength(0.45, DataGridLengthUnitType.Star),
            IsReadOnly = true,
        });
        _jobsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Output Override", "出力先上書き"),
            Binding = new System.Windows.Data.Binding(nameof(BatchJobRow.OutputDirectoryOverride))
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged,
            },
            Width = new DataGridLength(0.55, DataGridLengthUnitType.Star),
        });
        queueGrid.Children.Add(_jobsGrid);

        StackPanel actions = new()
        {
            Margin = new Thickness(12, 0, 0, 0),
        };
        Button upButton = new() { Content = T("Move Up", "上へ"), Width = 110, Margin = new Thickness(0, 0, 0, 8) };
        upButton.Click += (_, _) => MoveSelectedJob(-1);
        actions.Children.Add(upButton);
        Button downButton = new() { Content = T("Move Down", "下へ"), Width = 110, Margin = new Thickness(0, 0, 0, 8) };
        downButton.Click += (_, _) => MoveSelectedJob(1);
        actions.Children.Add(downButton);
        Button removeButton = new() { Content = T("Remove", "削除"), Width = 110 };
        removeButton.Click += (_, _) => RemoveSelectedJob();
        actions.Children.Add(removeButton);
        Grid.SetColumn(actions, 1);
        queueGrid.Children.Add(actions);

        root.Children.Add(queueGrid);

        UIElement footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private UIElement BuildAddRow()
    {
        Grid row = new() { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.32, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.48, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _profileComboBox.ItemsSource = Profiles.Select(profile => profile.Name).ToList();
        if (_profileComboBox.Items.Count > 0)
        {
            _profileComboBox.SelectedIndex = 0;
        }
        row.Children.Add(CreateField(T("Saved Profile", "保存済みプロファイル"), _profileComboBox, 0));

        Grid overrideGrid = new();
        overrideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        overrideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        overrideGrid.Children.Add(_outputOverrideTextBox);
        Button browseButton = new() { Content = T("Browse...", "参照..."), Margin = new Thickness(8, 18, 0, 0), Width = 96 };
        browseButton.Click += (_, _) => BrowseForOverride();
        Grid.SetColumn(browseButton, 1);
        overrideGrid.Children.Add(browseButton);
        Grid.SetColumn(overrideGrid, 1);
        row.Children.Add(CreateField(T("Output Override", "出力先上書き"), overrideGrid, 1));

        Button addButton = new() { Content = T("Add Job", "追加"), Width = 96, Margin = new Thickness(12, 18, 0, 0) };
        addButton.Click += (_, _) => AddJob();
        Grid.SetColumn(addButton, 2);
        row.Children.Add(addButton);

        return row;
    }

    private UIElement BuildFooter()
    {
        DockPanel footer = new() { Margin = new Thickness(0, 12, 0, 0) };

        StackPanel left = new()
        {
            Orientation = Orientation.Horizontal,
        };
        Button importButton = new() { Content = T("Import Queue...", "キュー読込..."), Width = 120 };
        importButton.Click += (_, _) => ImportQueue();
        left.Children.Add(importButton);
        Button exportButton = new() { Content = T("Export Queue...", "キュー保存..."), Width = 120, Margin = new Thickness(8, 0, 0, 0) };
        exportButton.Click += (_, _) => ExportQueue();
        left.Children.Add(exportButton);
        DockPanel.SetDock(left, Dock.Left);
        footer.Children.Add(left);

        StackPanel right = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Button closeButton = new() { Content = T("Close", "閉じる"), Width = 96 };
        closeButton.Click += (_, _) =>
        {
            _window.DialogResult = false;
            _window.Close();
        };
        right.Children.Add(closeButton);

        Button runButton = new() { Content = T("Run Batch", "バッチ実行"), Width = 110, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        runButton.Click += (_, _) => ConfirmRun();
        right.Children.Add(runButton);
        DockPanel.SetDock(right, Dock.Right);
        footer.Children.Add(right);

        return footer;
    }

    private FrameworkElement CreateField(string label, UIElement control, int column)
    {
        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 4),
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(control);
        Grid.SetColumn(panel, column);
        return panel;
    }

    private void AddJob()
    {
        string profileName = _profileComboBox.SelectedItem?.ToString()?.Trim() ?? string.Empty;
        if (profileName.Length == 0)
        {
            return;
        }

        _jobs.Add(new BatchJobRow
        {
            ProfileName = profileName,
            OutputDirectoryOverride = (_outputOverrideTextBox.Text ?? string.Empty).Trim(),
        });
        _outputOverrideTextBox.Clear();
    }

    private void RemoveSelectedJob()
    {
        if (_jobsGrid.SelectedItem is not BatchJobRow row)
        {
            return;
        }

        _jobs.Remove(row);
    }

    private void MoveSelectedJob(int delta)
    {
        if (_jobsGrid.SelectedItem is not BatchJobRow row)
        {
            return;
        }

        int index = _jobs.IndexOf(row);
        int targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= _jobs.Count)
        {
            return;
        }

        _jobs.Move(index, targetIndex);
        _jobsGrid.SelectedItem = row;
    }

    private void BrowseForOverride()
    {
        using WinForms.FolderBrowserDialog dialog = new()
        {
            ShowNewFolderButton = true,
            SelectedPath = _outputOverrideTextBox.Text,
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _outputOverrideTextBox.Text = dialog.SelectedPath;
        }
    }

    private void ImportQueue()
    {
        using WinForms.OpenFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        ExportJobManifest? manifest = JsonConvert.DeserializeObject<ExportJobManifest>(File.ReadAllText(dialog.FileName));
        _jobs.Clear();
        foreach (ExportJobManifestItem item in manifest?.Jobs ?? new List<ExportJobManifestItem>())
        {
            _jobs.Add(new BatchJobRow
            {
                ProfileName = item.ProfileName?.Trim() ?? string.Empty,
                OutputDirectoryOverride = item.OutputDirectoryOverride?.Trim() ?? string.Empty,
            });
        }
    }

    private void ExportQueue()
    {
        using WinForms.SaveFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            AddExtension = true,
            FileName = "batch-export-queue.json",
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(BuildManifest(), Formatting.Indented));
    }

    private void ConfirmRun()
    {
        if (_jobs.Count == 0)
        {
            MessageBox.Show(
                _window,
                T("Add at least one saved profile to the batch queue.", "バッチ キューに少なくとも 1 つの保存済みプロファイルを追加してください。"),
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Result = BuildManifest();
        _window.DialogResult = true;
        _window.Close();
    }

    private ExportJobManifest BuildManifest()
    {
        return new ExportJobManifest
        {
            Jobs = _jobs.Select(job => new ExportJobManifestItem
            {
                ProfileName = job.ProfileName?.Trim() ?? string.Empty,
                OutputDirectoryOverride = string.IsNullOrWhiteSpace(job.OutputDirectoryOverride)
                    ? null
                    : job.OutputDirectoryOverride.Trim(),
            }).ToList(),
        };
    }

    private string T(string english, string japanese) => UiLanguageText.Select(_language, english, japanese);

    private sealed class BatchJobRow
    {
        public string ProfileName { get; set; } = string.Empty;

        public string OutputDirectoryOverride { get; set; } = string.Empty;
    }
}
