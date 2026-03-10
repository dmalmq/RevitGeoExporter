using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RevitGeoExporter.Core.Validation;
using RevitGeoExporter.Help;

namespace RevitGeoExporter.UI;

public sealed class ExportValidationForm : IDisposable
{
    private readonly ExportValidationResult _result;
    private readonly UiLanguage _language;
    private readonly bool _canResolveIssues;
    private readonly Window _window;

    public ExportValidationForm(ExportValidationResult result, UiLanguage language, bool canResolveIssues)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _language = language;
        _canResolveIssues = canResolveIssues;
        Outcome = ExportValidationOutcome.Cancel;

        _window = new Window
        {
            Title = T("Validation Results", "検証結果"),
            Width = 1080,
            Height = 680,
            MinWidth = 860,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = BuildLayout(),
        };
    }

    public ExportValidationOutcome Outcome { get; private set; }

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
        int errorCount = _result.Issues.Count(issue => issue.Severity == ValidationSeverity.Error);
        int warningCount = _result.Issues.Count(issue => issue.Severity == ValidationSeverity.Warning);
        int infoCount = _result.Issues.Count(issue => issue.Severity == ValidationSeverity.Info);

        Grid root = new() { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel header = new() { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = _result.HasErrors
                ? T(
                    "Validation found issues that may affect export. Resolve them now or continue anyway.",
                    "書き出しに影響する可能性のある問題が見つかりました。ここで解消するか、そのまま続行できます。")
                : T("Review validation results before export.", "書き出し前に検証結果を確認してください。"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        });
        header.Children.Add(new TextBlock
        {
            Text = _language == UiLanguage.Japanese
                ? $"エラー: {errorCount}    警告: {warningCount}    情報: {infoCount}"
                : $"Errors: {errorCount}    Warnings: {warningCount}    Info: {infoCount}",
            Margin = new Thickness(0, 6, 0, 0),
        });

        root.Children.Add(header);

        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = BuildRows(),
        };
        grid.Columns.Add(new DataGridTextColumn { Header = T("Severity", "重要度"), Binding = new System.Windows.Data.Binding(nameof(ValidationRow.Severity)), Width = new DataGridLength(0.12, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Code", "コード"), Binding = new System.Windows.Data.Binding(nameof(ValidationRow.Code)), Width = new DataGridLength(0.14, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("View", "ビュー"), Binding = new System.Windows.Data.Binding(nameof(ValidationRow.View)), Width = new DataGridLength(0.2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Feature Type", "フィーチャ種別"), Binding = new System.Windows.Data.Binding(nameof(ValidationRow.FeatureType)), Width = new DataGridLength(0.16, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Message", "メッセージ"), Binding = new System.Windows.Data.Binding(nameof(ValidationRow.Message)), Width = new DataGridLength(0.38, DataGridLengthUnitType.Star) });

        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };

        Button cancelButton = new() { Content = T("Cancel", "キャンセル"), Width = 110, Margin = new Thickness(8, 0, 0, 0) };
        cancelButton.Click += (_, _) =>
        {
            Outcome = ExportValidationOutcome.Cancel;
            _window.DialogResult = false;
            _window.Close();
        };

        Button helpButton = new() { Content = T("Help", "ヘルプ"), Width = 110, Margin = new Thickness(8, 0, 0, 0) };
        helpButton.Click += (_, _) => HelpLauncher.Show(null, HelpTopic.ValidationAndDiagnostics, _language, _window.Title);

        Button resolveButton = new()
        {
            Content = T("Resolve Issues...", "問題を解消..."),
            Width = 130,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = _canResolveIssues,
        };
        resolveButton.Click += (_, _) =>
        {
            Outcome = ExportValidationOutcome.ResolveIssues;
            _window.DialogResult = true;
            _window.Close();
        };

        Button continueButton = new()
        {
            Content = _result.HasErrors ? T("Continue Anyway", "このまま続行") : T("Continue Export", "書き出しを続行"),
            Width = 140,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };
        continueButton.Click += (_, _) => ContinueExport();

        actions.Children.Add(continueButton);
        actions.Children.Add(resolveButton);
        actions.Children.Add(helpButton);
        actions.Children.Add(cancelButton);

        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        return root;
    }

    private List<ValidationRow> BuildRows()
    {
        if (_result.Issues.Count == 0)
        {
            return new List<ValidationRow>
            {
                new()
                {
                    Severity = ValidationSeverity.Info.ToString(),
                    Message = T("No validation issues were found.", "検証で問題は見つかりませんでした。"),
                },
            };
        }

        return _result.Issues.Select(issue => new ValidationRow
        {
            Severity = issue.Severity.ToString(),
            Code = issue.Code.ToString(),
            View = issue.ViewName ?? string.Empty,
            FeatureType = issue.FeatureType ?? string.Empty,
            Message = issue.Message,
        }).ToList();
    }

    private void ContinueExport()
    {
        if (_result.HasErrors)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                T(
                    "Validation still contains errors. Export may produce incomplete or inconsistent output. Continue anyway?",
                    "まだエラーが残っています。書き出し結果が不完全または不整合になる可能性があります。このまま続行しますか?"),
                _window.Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Outcome = ExportValidationOutcome.ContinueExport;
        _window.DialogResult = true;
        _window.Close();
    }

    private string T(string english, string japanese) => UiLanguageText.Select(_language, english, japanese);

    private sealed class ValidationRow
    {
        public string Severity { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public string View { get; init; } = string.Empty;
        public string FeatureType { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}
