using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RevitGeoExporter.Core.Validation;
using RevitGeoExporter.Help;

namespace RevitGeoExporter.UI;

public sealed class ExportValidationForm : IDisposable
{
    private readonly ExportValidationResult _result;
    private readonly UiLanguage _language;
    private readonly bool _canResolveIssues;
    private readonly Func<ValidationIssue, string?>? _navigateRequested;
    private readonly Window _window;
    private readonly DataGrid _grid = new();
    private readonly Button _showInRevitButton = new();

    public ExportValidationForm(
        ExportValidationResult result,
        UiLanguage language,
        bool canResolveIssues,
        Func<ValidationIssue, string?>? navigateRequested = null)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _language = language;
        _canResolveIssues = canResolveIssues;
        _navigateRequested = navigateRequested;
        Outcome = ExportValidationOutcome.Cancel;

        _window = new Window
        {
            Title = T("Validation.Title", "Validation Results"),
            Width = 1180,
            Height = 700,
            MinWidth = 920,
            MinHeight = 520,
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
                    "Validation.HasIssuesMessage",
                    "Validation found issues that may affect export. Resolve them now or continue anyway.")
                : T("Validation.ReviewBeforeExport", "Review validation results before export."),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{T("Validation.Errors", "Errors")}: {errorCount}    " +
                   $"{T("Validation.Warnings", "Warnings")}: {warningCount}    " +
                   $"{T("Validation.Info", "Info")}: {infoCount}",
            Margin = new Thickness(0, 6, 0, 0),
        });
        root.Children.Add(header);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _grid.ItemsSource = BuildRows();
        _grid.SelectionChanged += (_, _) => UpdateNavigationButtonState();
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Validation.Column.Severity", "Severity"),
            Binding = new Binding(nameof(ValidationRow.Severity)),
            Width = new DataGridLength(0.1, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Validation.Column.Code", "Code"),
            Binding = new Binding(nameof(ValidationRow.Code)),
            Width = new DataGridLength(0.14, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Validation.Column.View", "View"),
            Binding = new Binding(nameof(ValidationRow.View)),
            Width = new DataGridLength(0.14, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Validation.Column.FeatureType", "Feature Type"),
            Binding = new Binding(nameof(ValidationRow.FeatureType)),
            Width = new DataGridLength(0.12, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Validation.Column.Message", "Message"),
            Binding = new Binding(nameof(ValidationRow.Message)),
            Width = new DataGridLength(0.28, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Validation.Column.RecommendedAction", "Recommended Action"),
            Binding = new Binding(nameof(ValidationRow.RecommendedAction)),
            Width = new DataGridLength(0.22, DataGridLengthUnitType.Star),
        });

        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };

        Button cancelButton = new()
        {
            Content = T("Common.Cancel", "Cancel"),
            Width = 110,
            Margin = new Thickness(8, 0, 0, 0),
        };
        cancelButton.Click += (_, _) =>
        {
            Outcome = ExportValidationOutcome.Cancel;
            _window.DialogResult = false;
            _window.Close();
        };

        Button helpButton = new()
        {
            Content = T("Common.Help", "Help"),
            Width = 110,
            Margin = new Thickness(8, 0, 0, 0),
        };
        helpButton.Click += (_, _) => HelpLauncher.Show(null, HelpTopic.ValidationAndDiagnostics, _language, _window.Title);

        _showInRevitButton.Content = T("Validation.ShowInRevit", "Show in Revit");
        _showInRevitButton.Width = 130;
        _showInRevitButton.Margin = new Thickness(8, 0, 0, 0);
        _showInRevitButton.IsEnabled = false;
        _showInRevitButton.Click += (_, _) => NavigateToSelectedIssue();

        Button resolveButton = new()
        {
            Content = T("Validation.ResolveIssues", "Resolve Issues..."),
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
            Content = _result.HasErrors
                ? T("Validation.ContinueAnyway", "Continue Anyway")
                : T("Validation.Continue", "Continue Export"),
            Width = 140,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };
        continueButton.Click += (_, _) => ContinueExport();

        actions.Children.Add(continueButton);
        actions.Children.Add(resolveButton);
        actions.Children.Add(_showInRevitButton);
        actions.Children.Add(helpButton);
        actions.Children.Add(cancelButton);

        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        UpdateNavigationButtonState();
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
                    Message = T("Validation.NoIssues", "No validation issues were found."),
                    RecommendedAction = T("Validation.NoActionRequired", "No action required."),
                },
            };
        }

        return _result.Issues.Select(issue => new ValidationRow
        {
            Issue = issue,
            Severity = issue.Severity.ToString(),
            Code = issue.Code.ToString(),
            View = issue.ViewName ?? string.Empty,
            FeatureType = issue.FeatureType ?? string.Empty,
            Message = issue.Message,
            RecommendedAction = issue.RecommendedAction ?? string.Empty,
        }).ToList();
    }

    private void ContinueExport()
    {
        if (_result.HasErrors)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                T(
                    "Validation.ContinueWithErrorsConfirmation",
                    "Validation still contains errors. Export may produce incomplete or inconsistent output. Continue anyway?"),
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

    private void NavigateToSelectedIssue()
    {
        if (_navigateRequested == null || _grid.SelectedItem is not ValidationRow row || row.Issue == null)
        {
            return;
        }

        string? failureMessage = _navigateRequested(row.Issue);
        if (string.IsNullOrWhiteSpace(failureMessage))
        {
            return;
        }

        MessageBox.Show(
            failureMessage,
            _window.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UpdateNavigationButtonState()
    {
        _showInRevitButton.IsEnabled = _navigateRequested != null &&
                                       _grid.SelectedItem is ValidationRow row &&
                                       row.Issue?.CanNavigateInRevit == true;
    }

    private string T(string key, string fallback) => UiLanguageText.Get(_language, key, fallback);

    private sealed class ValidationRow
    {
        public ValidationIssue? Issue { get; init; }
        public string Severity { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public string View { get; init; } = string.Empty;
        public string FeatureType { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string RecommendedAction { get; init; } = string.Empty;
    }
}
