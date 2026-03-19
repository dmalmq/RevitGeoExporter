using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RevitGeoExporter.Export;
using RevitGeoExporter.Help;

namespace RevitGeoExporter.UI;

public sealed class ExportResultForm : IDisposable
{
    private readonly FloorGeoPackageExportResult _result;
    private readonly string _outputDirectory;
    private readonly UiLanguage _language;
    private readonly Window _window;

    public ExportResultForm(FloorGeoPackageExportResult result, string outputDirectory, UiLanguage language)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _outputDirectory = ResolveOutputDirectory(result, outputDirectory);
        _language = Enum.IsDefined(typeof(UiLanguage), language) ? language : UiLanguage.English;

        _window = new Window
        {
            Title = T("Export Results", "エクスポート結果"),
            Width = 1080,
            Height = 680,
            MinWidth = 920,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = BuildLayout(),
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

    private UIElement BuildLayout()
    {
        int viewCount = _result.ArtifactResults.SelectMany(x => x.ContributingViewNames).Distinct(StringComparer.Ordinal).Count();
        int fileCount = _result.ArtifactResults.Count;
        int warningCount = _result.Warnings.Count;
        int featureCount = _result.ArtifactResults.Sum(x => x.FeatureCount);
        int writtenArtifacts = _result.ArtifactResults.Count(x => x.Disposition == ArtifactDisposition.Written);
        int reusedArtifacts = _result.ArtifactResults.Count(x => x.Disposition == ArtifactDisposition.ReusedFromBaseline);

        Grid root = new() { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel labels = new();
        labels.Children.Add(new TextBlock
        {
            Text = warningCount > 0
                ? T("GeoPackage export completed with warnings.", "警告付きでGeoPackageのエクスポートが完了しました。")
                : T("GeoPackage export completed.", "GeoPackageのエクスポートが完了しました。"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        });
        labels.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Text = _language == UiLanguage.Japanese
                ? $"出力ビュー: {viewCount}    出力ファイル: {fileCount}    総フィーチャ数: {featureCount}    警告: {warningCount}"
                : $"Views: {viewCount}    Artifacts: {fileCount}    Written: {writtenArtifacts}    Reused: {reusedArtifacts}    Features: {featureCount}    Warnings: {warningCount}",
        });
        labels.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Text = string.IsNullOrWhiteSpace(_outputDirectory)
                ? T("Output directory: (not available)", "出力フォルダー: （利用できません）")
                : _language == UiLanguage.Japanese
                    ? $"出力フォルダー: {_outputDirectory}"
                    : $"Output directory: {_outputDirectory}",
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        header.Children.Add(labels);

        Button openFolderButton = new()
        {
            Content = T("Open Folder", "フォルダーを開く"),
            Width = 112,
            Height = 30,
            Margin = new Thickness(12, 0, 0, 0),
            IsEnabled = !string.IsNullOrWhiteSpace(_outputDirectory) && Directory.Exists(_outputDirectory),
        };
        openFolderButton.Click += (_, _) => OpenOutputDirectory();
        Grid.SetColumn(openFolderButton, 1);
        header.Children.Add(openFolderButton);

        root.Children.Add(header);

        TabControl tabs = new();
        tabs.Items.Add(BuildFilesTab(fileCount));
        tabs.Items.Add(BuildStringTab(
            _result.Warnings.Count == 0 ? new[] { T("No warnings.", "警告はありません。") } : _result.Warnings,
            _language == UiLanguage.Japanese ? $"警告 ({warningCount})" : $"Warnings ({warningCount})"));

        IReadOnlyList<string> changeLines = _result.ChangeSummary == null || !_result.ChangeSummary.HasChanges
            ? new[] { T("No change summary available.", "変更サマリーはありません。") }
            : _result.ChangeSummary.Lines;
        tabs.Items.Add(BuildStringTab(changeLines, T("Changes", "Changes")));

        List<string> packageLines = new();
        if (!string.IsNullOrWhiteSpace(_result.PackageDirectoryPath))
        {
            packageLines.Add($"Package directory: {_result.PackageDirectoryPath}");
        }

        if (!string.IsNullOrWhiteSpace(_result.PackageManifestPath))
        {
            packageLines.Add($"Manifest: {_result.PackageManifestPath}");
        }

        if (_result.PackageValidationResult != null)
        {
            int errorCount = _result.PackageValidationResult.Issues.Count(issue => issue.Severity == RevitGeoExporter.Core.Diagnostics.PackageValidationSeverity.Error);
            int packageWarningCount = _result.PackageValidationResult.Issues.Count(issue => issue.Severity == RevitGeoExporter.Core.Diagnostics.PackageValidationSeverity.Warning);
            packageLines.Add($"Validation: {errorCount} error(s), {packageWarningCount} warning(s)");
        }

        if (packageLines.Count == 0)
        {
            packageLines.Add(T("No package output was written for this export.", "このエクスポートではパッケージ出力は作成されていません。"));
        }

        tabs.Items.Add(BuildStringTab(packageLines, T("Package", "Package")));

        Grid.SetRow(tabs, 1);
        tabs.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(tabs);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };

        Button closeButton = new() { Content = T("Close", "閉じる"), Width = 96, IsDefault = true };
        closeButton.Click += (_, _) =>
        {
            _window.DialogResult = true;
            _window.Close();
        };

        Button helpButton = new() { Content = T("Help", "ヘルプ"), Width = 96, Margin = new Thickness(8, 0, 0, 0) };
        helpButton.Click += (_, _) => HelpLauncher.Show(null, HelpTopic.TroubleshootingFaq, _language, _window.Title);

        actions.Children.Add(helpButton);
        actions.Children.Add(closeButton);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        return root;
    }

    private TabItem BuildFilesTab(int fileCount)
    {
        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = _result.ViewResults.Select(export => new FileRow
            {
                ViewName = export.ViewName,
                LevelName = export.LevelName,
                FeatureType = export.FeatureType,
                FeatureCount = export.FeatureCount,
                OutputFilePath = export.OutputFilePath,
            }).ToList(),
        };

        grid.Columns.Add(new DataGridTextColumn { Header = T("View", "ビュー"), Binding = new System.Windows.Data.Binding(nameof(FileRow.ViewName)), Width = new DataGridLength(0.22, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Level", "レベル"), Binding = new System.Windows.Data.Binding(nameof(FileRow.LevelName)), Width = new DataGridLength(0.18, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Feature Type", "フィーチャ種別"), Binding = new System.Windows.Data.Binding(nameof(FileRow.FeatureType)), Width = new DataGridLength(0.14, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Features", "フィーチャ数"), Binding = new System.Windows.Data.Binding(nameof(FileRow.FeatureCount)), Width = new DataGridLength(0.1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Output File", "出力ファイル"), Binding = new System.Windows.Data.Binding(nameof(FileRow.OutputFilePath)), Width = new DataGridLength(0.36, DataGridLengthUnitType.Star) });

        return new TabItem
        {
            Header = _language == UiLanguage.Japanese ? $"出力ファイル ({fileCount})" : $"Exported Files ({fileCount})",
            Content = grid,
        };
    }

    private static TabItem BuildStringTab(IEnumerable<string> lines, string title)
    {
        ListBox list = new();
        foreach (string line in lines)
        {
            list.Items.Add(line);
        }

        return new TabItem
        {
            Header = title,
            Content = list,
        };
    }

    private void OpenOutputDirectory()
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory) || !Directory.Exists(_outputDirectory))
        {
            MessageBox.Show(
                T("The output directory was not found.", "出力フォルダーが見つかりませんでした。"),
                ProjectInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _outputDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _language == UiLanguage.Japanese
                    ? $"出力フォルダーを開けませんでした。\n\n{ex.Message}"
                    : $"Unable to open the output directory.\n\n{ex.Message}",
                ProjectInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private string T(string english, string japanese) => UiLanguageText.Select(_language, english, japanese);

    private static string ResolveOutputDirectory(FloorGeoPackageExportResult result, string outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            return outputDirectory.Trim();
        }

        string? firstPath = result.ViewResults
            .Select(x => x.OutputFilePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        return firstPath is null
            ? string.Empty
            : Path.GetDirectoryName(firstPath) ?? string.Empty;
    }

    private sealed class FileRow
    {
        public string ViewName { get; init; } = string.Empty;
        public string LevelName { get; init; } = string.Empty;
        public string FeatureType { get; init; } = string.Empty;
        public int FeatureCount { get; init; }
        public string OutputFilePath { get; init; } = string.Empty;
    }
}
