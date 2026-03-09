using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RevitGeoExporter.Help;
using RevitGeoExporter.Core.Validation;
using WinFormsControl = System.Windows.Forms.Control;
using WinFormsForm = System.Windows.Forms.Form;

namespace RevitGeoExporter.UI;

public sealed class ExportValidationForm : WinFormsForm
{
    private readonly ExportValidationResult _result;
    private readonly UiLanguage _language;

    private readonly Label _titleLabel = new();
    private readonly Label _summaryLabel = new();
    private readonly DataGridView _issuesGrid = new();
    private readonly Button _continueButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _helpButton = new();

    public ExportValidationForm(ExportValidationResult result, UiLanguage language)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _language = language;
        InitializeComponents();
        Populate();
    }

    private void InitializeComponents()
    {
        Text = T("Validation Results", "検証結果");
        Width = 1080;
        Height = 680;
        MinimumSize = new Size(860, 480);
        StartPosition = FormStartPosition.CenterParent;
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
    }

    private WinFormsControl BuildHeader()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Font = new Font(Font, FontStyle.Bold);
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_titleLabel, 0, 0);

        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_summaryLabel, 0, 1);

        return panel;
    }

    private WinFormsControl BuildBody()
    {
        _issuesGrid.Dock = DockStyle.Fill;
        _issuesGrid.ReadOnly = true;
        _issuesGrid.MultiSelect = false;
        _issuesGrid.RowHeadersVisible = false;
        _issuesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _issuesGrid.AllowUserToAddRows = false;
        _issuesGrid.AllowUserToDeleteRows = false;
        _issuesGrid.AllowUserToResizeRows = false;
        _issuesGrid.AutoGenerateColumns = false;
        _issuesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _issuesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Severity",
            HeaderText = T("Severity", "重要度"),
            FillWeight = 10f,
        });
        _issuesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Code",
            HeaderText = T("Code", "コード"),
            FillWeight = 12f,
        });
        _issuesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "View",
            HeaderText = T("View", "ビュー"),
            FillWeight = 16f,
        });
        _issuesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FeatureType",
            HeaderText = T("Feature Type", "フィーチャタイプ"),
            FillWeight = 12f,
        });
        _issuesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Message",
            HeaderText = T("Message", "メッセージ"),
            FillWeight = 50f,
        });

        return _issuesGrid;
    }

    private WinFormsControl BuildActions()
    {
        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
        };

        _cancelButton.Width = 110;
        _cancelButton.Height = 30;
        _cancelButton.Text = T("Cancel", "キャンセル");
        _cancelButton.DialogResult = DialogResult.Cancel;
        panel.Controls.Add(_cancelButton);

        _helpButton.Width = 110;
        _helpButton.Height = 30;
        _helpButton.Text = T("Help", "ヘルプ");
        _helpButton.Click += (_, _) => HelpLauncher.Show(this, HelpTopic.ValidationAndDiagnostics, _language, Text);
        panel.Controls.Add(_helpButton);

        _continueButton.Width = 130;
        _continueButton.Height = 30;
        _continueButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        panel.Controls.Add(_continueButton);

        CancelButton = _cancelButton;
        AcceptButton = _continueButton;
        return panel;
    }

    private void Populate()
    {
        int errorCount = _result.Issues.Count(issue => issue.Severity == ValidationSeverity.Error);
        int warningCount = _result.Issues.Count(issue => issue.Severity == ValidationSeverity.Warning);
        int infoCount = _result.Issues.Count(issue => issue.Severity == ValidationSeverity.Info);

        _titleLabel.Text = _result.HasErrors
            ? T("Export is blocked until validation errors are resolved.", "検証エラーが解消されるまで書き出しは実行できません。")
            : T("Review validation results before export.", "書き出し前に検証結果を確認してください。");
        _summaryLabel.Text = _language == UiLanguage.Japanese
            ? $"エラー: {errorCount}    警告: {warningCount}    情報: {infoCount}"
            : $"Errors: {errorCount}    Warnings: {warningCount}    Info: {infoCount}";

        foreach (ValidationIssue issue in _result.Issues)
        {
            _issuesGrid.Rows.Add(
                issue.Severity.ToString(),
                issue.Code.ToString(),
                issue.ViewName ?? string.Empty,
                issue.FeatureType ?? string.Empty,
                issue.Message);
        }

        if (_result.Issues.Count == 0)
        {
            _issuesGrid.Rows.Add(
                ValidationSeverity.Info.ToString(),
                string.Empty,
                string.Empty,
                string.Empty,
                T("No validation issues were found.", "検証で問題は見つかりませんでした。"));
        }

        if (_result.HasErrors)
        {
            _continueButton.Enabled = false;
            _continueButton.Text = T("Resolve Errors", "エラーを解消");
        }
        else
        {
            _continueButton.Enabled = true;
            _continueButton.Text = T("Continue Export", "書き出しを続行");
        }
    }

    private string T(string english, string japanese)
    {
        return UiLanguageText.Select(_language, english, japanese);
    }
}
