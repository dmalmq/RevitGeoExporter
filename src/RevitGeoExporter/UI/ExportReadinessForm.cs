using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.UI;

public sealed class ExportReadinessForm : IDisposable
{
    private readonly ExportReadinessSummary _summary;
    private readonly SharedCoordinateValidationResult _coordinateValidation;
    private readonly UiLanguage _language;
    private readonly bool _canResolveIssues;
    private readonly Window _window;

    public ExportReadinessForm(
        ExportReadinessSummary summary,
        SharedCoordinateValidationResult coordinateValidation,
        UiLanguage language,
        bool canResolveIssues)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _coordinateValidation = coordinateValidation ?? throw new ArgumentNullException(nameof(coordinateValidation));
        _language = language;
        _canResolveIssues = canResolveIssues;
        Outcome = ExportReadinessOutcome.Cancel;

        _window = new Window
        {
            Title = T("Readiness.Title", "Export Readiness"),
            Width = 1080,
            Height = 720,
            MinWidth = 900,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = BuildLayout(),
        };
    }

    public ExportReadinessOutcome Outcome { get; private set; }

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
        Grid root = new() { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel header = new() { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = _summary.BlockingIssueCount > 0 || _summary.WarningIssueCount > 0
                ? T(
                    "Readiness.ReviewMessage",
                    "Review export readiness before continuing. Resolve issues now when possible.")
                : T("Readiness.NoProblemsMessage", "The exporter did not find any obvious readiness problems."),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Text = $"{T("Validation.Errors", "Errors")}: {_summary.BlockingIssueCount}    " +
                   $"{T("Validation.Warnings", "Warnings")}: {_summary.WarningIssueCount}    " +
                   $"{T("Readiness.UnmappedSuggestions", "Unmapped suggestions")}: {_summary.MappingSuggestions.Count}",
        });
        root.Children.Add(header);

        TabControl tabs = new();
        tabs.Items.Add(BuildOverviewTab());
        tabs.Items.Add(BuildGeoreferenceTab());
        tabs.Items.Add(BuildMappingsTab());
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

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
            Outcome = ExportReadinessOutcome.Cancel;
            _window.DialogResult = false;
            _window.Close();
        };

        Button openMappingsButton = new()
        {
            Content = T("Readiness.OpenMappings", "Open Mappings"),
            Width = 140,
            Margin = new Thickness(8, 0, 0, 0),
        };
        openMappingsButton.Click += (_, _) =>
        {
            Outcome = ExportReadinessOutcome.OpenMappings;
            _window.DialogResult = true;
            _window.Close();
        };

        Button resolveButton = new()
        {
            Content = T("Validation.ResolveIssues", "Resolve Issues..."),
            Width = 140,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = _canResolveIssues,
        };
        resolveButton.Click += (_, _) =>
        {
            Outcome = ExportReadinessOutcome.ResolveIssues;
            _window.DialogResult = true;
            _window.Close();
        };

        Button continueButton = new()
        {
            Content = T("Readiness.ContinueToValidation", "Continue to Validation"),
            Width = 170,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };
        continueButton.Click += (_, _) =>
        {
            Outcome = ExportReadinessOutcome.ContinueToValidation;
            _window.DialogResult = true;
            _window.Close();
        };

        actions.Children.Add(continueButton);
        actions.Children.Add(resolveButton);
        actions.Children.Add(openMappingsButton);
        actions.Children.Add(cancelButton);

        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        return root;
    }

    private TabItem BuildOverviewTab()
    {
        StackPanel panel = new() { Margin = new Thickness(8) };
        panel.Children.Add(CreateMetricRow(T("Readiness.UnitsWithName", "Units with IMDF_Name"), $"{_summary.UnitsWithNameCount} / {_summary.TotalUnitCount}"));
        panel.Children.Add(CreateMetricRow(T("Readiness.UnitsMissingName", "Units missing IMDF_Name"), _summary.UnitsMissingNameCount.ToString()));
        panel.Children.Add(CreateMetricRow(T("Readiness.UnitsWithAltName", "Units with IMDF_AltName"), $"{_summary.UnitsWithAltNameCount} / {_summary.TotalUnitCount}"));
        panel.Children.Add(CreateMetricRow(T("Readiness.UnitsMissingAltName", "Units missing IMDF_AltName"), _summary.UnitsMissingAltNameCount.ToString()));
        panel.Children.Add(CreateMetricRow(T("Readiness.MissingStableIds", "Missing stable IDs"), _summary.MissingStableIdCount.ToString()));
        panel.Children.Add(CreateMetricRow(T("Readiness.DuplicateStableIds", "Duplicate stable IDs"), _summary.DuplicateStableIdCount.ToString()));
        panel.Children.Add(CreateMetricRow(T("Readiness.UnsupportedOpeningFamilies", "Unsupported opening families"), _summary.UnsupportedOpeningFamilyCount.ToString()));
        panel.Children.Add(CreateMetricRow(T("Readiness.UnmappedAssignments", "Unmapped floor / room values"), _summary.UnmappedAssignmentCount.ToString()));

        if (_summary.MappingSuggestions.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 16, 0, 6),
                Text = T("Readiness.TopSuggestions", "Top suggested mapping fixes"),
                FontWeight = FontWeights.SemiBold,
            });

            foreach (ValidationMappingSuggestion suggestion in _summary.MappingSuggestions.Take(5))
            {
                string suggestedCategory = string.IsNullOrWhiteSpace(suggestion.SuggestedCategory)
                    ? T("Readiness.NoCategorySuggestion", "no category suggestion")
                    : suggestion.SuggestedCategory!;
                panel.Children.Add(new TextBlock
                {
                    Text = $"- {suggestion.MappingKey} -> {suggestedCategory} ({suggestion.OccurrenceCount})",
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        return new TabItem
        {
            Header = T("Readiness.OverviewTab", "Overview"),
            Content = new ScrollViewer { Content = panel },
        };
    }

    private TabItem BuildGeoreferenceTab()
    {
        StackPanel panel = new() { Margin = new Thickness(8) };
        panel.Children.Add(CreateMetricRow(
            T("Readiness.ActiveProjectLocation", "Active project location"),
            ValueOrFallback(_coordinateValidation.ActiveProjectLocationName)));
        panel.Children.Add(CreateMetricRow(
            T("Readiness.SharedCoordinateSummary", "Shared coordinate summary"),
            ValueOrFallback(_coordinateValidation.SharedCoordinateSummary)));
        panel.Children.Add(CreateMetricRow(
            T("Readiness.SourceCrs", "Source CRS / EPSG"),
            FormatSourceCoordinateSystem()));
        panel.Children.Add(CreateMetricRow(
            T("Readiness.SurveyPoint", "Survey point (shared/projected)"),
            FormatSurveyPoint(_coordinateValidation.SurveyPointSharedCoordinates)));

        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 16, 0, 6),
            Text = T("Readiness.GeoreferenceFindings", "Georeference findings"),
            FontWeight = FontWeights.SemiBold,
        });

        foreach (SharedCoordinateValidationFinding finding in _coordinateValidation.Findings)
        {
            panel.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8),
                BorderBrush = GetFindingBrush(finding.Severity),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = $"[{finding.Severity}] {finding.Message}",
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        }

        return new TabItem
        {
            Header = T("Readiness.GeoreferenceTab", "Georeference"),
            Content = new ScrollViewer { Content = panel },
        };
    }

    private TabItem BuildMappingsTab()
    {
        if (_summary.MappingSuggestions.Count == 0)
        {
            return new TabItem
            {
                Header = T("Readiness.MappingsTab", "Mappings"),
                Content = new TextBlock
                {
                    Margin = new Thickness(12),
                    Text = T(
                        "Readiness.NoUnmappedAssignments",
                        "No unmapped floor or room values were found in this export set."),
                    TextWrapping = TextWrapping.Wrap,
                },
            };
        }

        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = _summary.MappingSuggestions.Select(suggestion => new MappingRow
            {
                SourceKind = suggestion.SourceKind,
                MappingKey = suggestion.MappingKey,
                ParameterName = suggestion.ParameterName ?? string.Empty,
                SuggestedCategory = suggestion.SuggestedCategory ?? string.Empty,
                Count = suggestion.OccurrenceCount,
            }).ToList(),
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Readiness.Column.Source", "Source"),
            Binding = new Binding(nameof(MappingRow.SourceKind)),
            Width = new DataGridLength(0.14, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Readiness.Column.Value", "Value"),
            Binding = new Binding(nameof(MappingRow.MappingKey)),
            Width = new DataGridLength(0.36, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Readiness.Column.Parameter", "Parameter"),
            Binding = new Binding(nameof(MappingRow.ParameterName)),
            Width = new DataGridLength(0.18, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Readiness.Column.SuggestedCategory", "Suggested Category"),
            Binding = new Binding(nameof(MappingRow.SuggestedCategory)),
            Width = new DataGridLength(0.22, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Readiness.Column.Count", "Count"),
            Binding = new Binding(nameof(MappingRow.Count)),
            Width = new DataGridLength(0.1, DataGridLengthUnitType.Star),
        });

        return new TabItem
        {
            Header = T("Readiness.MappingsTab", "Mappings"),
            Content = grid,
        };
    }

    private static FrameworkElement CreateMetricRow(string label, string value)
    {
        Grid row = new() { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        TextBlock labelBlock = new()
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 12, 0),
        };
        row.Children.Add(labelBlock);

        TextBlock valueBlock = new()
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);
        return row;
    }

    private string FormatSourceCoordinateSystem()
    {
        string label = ValueOrFallback(_coordinateValidation.ResolvedSourceLabel);
        return _coordinateValidation.ResolvedSourceEpsg.HasValue
            ? $"{label} (EPSG:{_coordinateValidation.ResolvedSourceEpsg.Value})"
            : label;
    }

    private string ValueOrFallback(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? T("Readiness.NotAvailable", "Not available")
            : value!;
    }

    private string FormatSurveyPoint(Point2D? point)
    {
        if (point == null)
        {
            return T("Readiness.NotAvailable", "Not available");
        }

        return $"{point.Value.X:0.###}, {point.Value.Y:0.###}";
    }

    private static Brush GetFindingBrush(ValidationSeverity severity)
    {
        return severity switch
        {
            ValidationSeverity.Error => Brushes.IndianRed,
            ValidationSeverity.Warning => Brushes.Goldenrod,
            _ => Brushes.Gainsboro,
        };
    }

    private string T(string key, string fallback) => UiLanguageText.Get(_language, key, fallback);

    private sealed class MappingRow
    {
        public string SourceKind { get; init; } = string.Empty;
        public string MappingKey { get; init; } = string.Empty;
        public string ParameterName { get; init; } = string.Empty;
        public string SuggestedCategory { get; init; } = string.Empty;
        public int Count { get; init; }
    }
}
