using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RevitGeoExporter.Help;
using RevitGeoExporter.Export;
using WinFormsControl = System.Windows.Forms.Control;
using WinFormsForm = System.Windows.Forms.Form;

namespace RevitGeoExporter.UI;

public sealed class ExportResultForm : WinFormsForm
{
    private readonly FloorGeoPackageExportResult _result;
    private readonly string _outputDirectory;
    private readonly UiLanguage _language;

    private readonly Label _titleLabel = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _outputDirectoryLabel = new();
    private readonly Button _openFolderButton = new();
    private readonly Button _closeButton = new();
    private readonly Button _helpButton = new();
    private readonly TabPage _filesTab = new();
    private readonly TabPage _warningsTab = new();
    private readonly TabPage _changesTab = new();
    private readonly TabPage _packageTab = new();
    private readonly DataGridView _filesGrid = new();
    private readonly ListBox _warningsList = new();
    private readonly ListBox _changesList = new();
    private readonly ListBox _packageList = new();

    public ExportResultForm(FloorGeoPackageExportResult result, string outputDirectory, UiLanguage language)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _outputDirectory = ResolveOutputDirectory(result, outputDirectory);
        _language = Enum.IsDefined(typeof(UiLanguage), language) ? language : UiLanguage.English;
        InitializeComponents();
        Populate();
    }

    private void InitializeComponents()
    {
        Text = T("Export Results", "エクスポート結果");
        Width = 1080;
        Height = 680;
        MinimumSize = new Size(920, 520);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = true;
        FormBorderStyle = FormBorderStyle.Sizable;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        Controls.Add(root);

        root.Controls.Add(BuildHeaderPanel(), 0, 0);
        root.Controls.Add(BuildBodyPanel(), 0, 1);
        root.Controls.Add(BuildActionsPanel(), 0, 2);
    }

    private WinFormsControl BuildHeaderPanel()
    {
        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));

        TableLayoutPanel labels = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        labels.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        labels.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        labels.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _titleLabel.Font = new Font(Font, FontStyle.Bold);
        labels.Controls.Add(_titleLabel, 0, 0);

        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        labels.Controls.Add(_summaryLabel, 0, 1);

        _outputDirectoryLabel.Dock = DockStyle.Fill;
        _outputDirectoryLabel.TextAlign = ContentAlignment.MiddleLeft;
        _outputDirectoryLabel.AutoEllipsis = true;
        labels.Controls.Add(_outputDirectoryLabel, 0, 2);

        header.Controls.Add(labels, 0, 0);

        _openFolderButton.Width = 112;
        _openFolderButton.Height = 30;
        _openFolderButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _openFolderButton.Text = T("Open Folder", "フォルダーを開く");
        _openFolderButton.Click += (_, _) => OpenOutputDirectory();
        header.Controls.Add(_openFolderButton, 1, 0);

        return header;
    }

    private WinFormsControl BuildBodyPanel()
    {
        TabControl tabs = new()
        {
            Dock = DockStyle.Fill,
        };

        _filesTab.Padding = new Padding(6);
        _warningsTab.Padding = new Padding(6);

        ConfigureFilesGrid();
        _filesTab.Controls.Add(_filesGrid);

        _warningsList.Dock = DockStyle.Fill;
        _warningsList.HorizontalScrollbar = true;
        _warningsTab.Controls.Add(_warningsList);

        _changesList.Dock = DockStyle.Fill;
        _changesList.HorizontalScrollbar = true;
        _changesTab.Controls.Add(_changesList);

        _packageList.Dock = DockStyle.Fill;
        _packageList.HorizontalScrollbar = true;
        _packageTab.Controls.Add(_packageList);

        tabs.TabPages.Add(_filesTab);
        tabs.TabPages.Add(_warningsTab);
        tabs.TabPages.Add(_changesTab);
        tabs.TabPages.Add(_packageTab);
        return tabs;
    }

    private WinFormsControl BuildActionsPanel()
    {
        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
        };

        _closeButton.Width = 96;
        _closeButton.Height = 30;
        _closeButton.Text = T("Close", "閉じる");
        _closeButton.DialogResult = DialogResult.OK;
        actions.Controls.Add(_closeButton);

        _helpButton.Width = 96;
        _helpButton.Height = 30;
        _helpButton.Text = T("Help", "ヘルプ");
        _helpButton.Click += (_, _) => HelpLauncher.Show(this, HelpTopic.TroubleshootingFaq, _language, Text);
        actions.Controls.Add(_helpButton);

        AcceptButton = _closeButton;
        CancelButton = _closeButton;
        return actions;
    }

    private void ConfigureFilesGrid()
    {
        _filesGrid.Dock = DockStyle.Fill;
        _filesGrid.ReadOnly = true;
        _filesGrid.MultiSelect = false;
        _filesGrid.RowHeadersVisible = false;
        _filesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _filesGrid.AllowUserToAddRows = false;
        _filesGrid.AllowUserToDeleteRows = false;
        _filesGrid.AllowUserToResizeRows = false;
        _filesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _filesGrid.AutoGenerateColumns = false;

        _filesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ViewName",
            HeaderText = T("View", "ビュー"),
            FillWeight = 22f,
        });

        _filesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LevelName",
            HeaderText = T("Level", "レベル"),
            FillWeight = 18f,
        });

        _filesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FeatureType",
            HeaderText = T("Feature Type", "フィーチャ種別"),
            FillWeight = 14f,
        });

        _filesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FeatureCount",
            HeaderText = T("Features", "フィーチャ数"),
            FillWeight = 10f,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
            },
        });

        _filesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "OutputFilePath",
            HeaderText = T("Output File", "出力ファイル"),
            FillWeight = 36f,
        });
    }

    private void Populate()
    {
        int viewCount = _result.ViewResults
            .Select(x => x.ViewName)
            .Distinct(StringComparer.Ordinal)
            .Count();
        int fileCount = _result.ViewResults.Count;
        int warningCount = _result.Warnings.Count;
        int featureCount = _result.ViewResults.Sum(x => x.FeatureCount);

        _titleLabel.Text = warningCount > 0
            ? T("GeoPackage export completed with warnings.", "警告付きでGeoPackageのエクスポートが完了しました。")
            : T("GeoPackage export completed.", "GeoPackageのエクスポートが完了しました。");
        _summaryLabel.Text = _language == UiLanguage.Japanese
            ? $"出力ビュー: {viewCount}    出力ファイル: {fileCount}    総フィーチャ数: {featureCount}    警告: {warningCount}"
            : $"Views exported: {viewCount}    Files exported: {fileCount}    Total features: {featureCount}    Warnings: {warningCount}";

        _outputDirectoryLabel.Text = string.IsNullOrWhiteSpace(_outputDirectory)
            ? T("Output directory: (not available)", "出力フォルダー: （利用できません）")
            : _language == UiLanguage.Japanese
                ? $"出力フォルダー: {_outputDirectory}"
                : $"Output directory: {_outputDirectory}";
        _openFolderButton.Enabled = !string.IsNullOrWhiteSpace(_outputDirectory) && Directory.Exists(_outputDirectory);

        foreach (ViewExportResult export in _result.ViewResults)
        {
            _filesGrid.Rows.Add(
                export.ViewName,
                export.LevelName,
                export.FeatureType,
                export.FeatureCount,
                export.OutputFilePath);
        }

        if (_result.Warnings.Count == 0)
        {
            _warningsList.Items.Add(T("No warnings.", "警告はありません。"));
        }
        else
        {
            foreach (string warning in _result.Warnings)
            {
                _warningsList.Items.Add(warning);
            }
        }

        _filesTab.Text = _language == UiLanguage.Japanese
            ? $"出力ファイル ({fileCount})"
            : $"Exported Files ({fileCount})";
        _warningsTab.Text = _language == UiLanguage.Japanese
            ? $"警告 ({warningCount})"
            : $"Warnings ({warningCount})";
        _changesTab.Text = T("Changes", "Changes");
        _packageTab.Text = T("Package", "Package");

        if (_result.ChangeSummary == null || !_result.ChangeSummary.HasChanges)
        {
            _changesList.Items.Add(T("No change summary available.", "変更サマリーはありません。"));
        }
        else
        {
            foreach (string line in _result.ChangeSummary.Lines)
            {
                _changesList.Items.Add(line);
            }
        }

        if (!string.IsNullOrWhiteSpace(_result.PackageDirectoryPath))
        {
            _packageList.Items.Add($"Package directory: {_result.PackageDirectoryPath}");
        }

        if (!string.IsNullOrWhiteSpace(_result.PackageManifestPath))
        {
            _packageList.Items.Add($"Manifest: {_result.PackageManifestPath}");
        }

        if (_packageList.Items.Count == 0)
        {
            _packageList.Items.Add(T("No package output was written for this export.", "このエクスポートではパッケージ出力は作成されていません。"));
        }
    }

    private void OpenOutputDirectory()
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory) || !Directory.Exists(_outputDirectory))
        {
            MessageBox.Show(
                this,
                T("The output directory was not found.", "出力フォルダーが見つかりませんでした。"),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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
                this,
                _language == UiLanguage.Japanese
                    ? $"出力フォルダーを開けませんでした。\n\n{ex.Message}"
                    : $"Unable to open the output directory.\n\n{ex.Message}",
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private string T(string english, string japanese)
    {
        return UiLanguageText.Select(_language, english, japanese);
    }

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
}
