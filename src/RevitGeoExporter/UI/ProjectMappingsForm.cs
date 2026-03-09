using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.UI;

public sealed class ProjectMappingsForm : Form
{
    private readonly string _projectKey;
    private readonly MappingRuleStore _mappingRuleStore;
    private readonly IReadOnlyList<string> _categories;

    private readonly DataGridView _floorGrid = new();
    private readonly DataGridView _familyGrid = new();
    private readonly DataGridView _openingGrid = new();
    private readonly Button _importButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _cancelButton = new();

    public ProjectMappingsForm(
        string projectKey,
        ZoneCatalog zoneCatalog,
        MappingRuleStore mappingRuleStore)
    {
        _projectKey = string.IsNullOrWhiteSpace(projectKey)
            ? throw new ArgumentException("A project key is required.", nameof(projectKey))
            : projectKey.Trim();
        _mappingRuleStore = mappingRuleStore ?? throw new ArgumentNullException(nameof(mappingRuleStore));
        _categories = (zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog)))
            .GetKnownCategories()
            .ToList();

        InitializeComponents();
        LoadMappings();
    }

    private void InitializeComponents()
    {
        Text = "Project Mappings";
        Width = 980;
        Height = 680;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        Controls.Add(root);

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill,
        };
        TabPage floorTab = new("Floor Overrides");
        TabPage familyTab = new("Family Categories");
        TabPage openingTab = new("Accepted Openings");

        ConfigureMappingGrid(_floorGrid, "Floor Type Name");
        floorTab.Controls.Add(_floorGrid);
        ConfigureMappingGrid(_familyGrid, "Family Name");
        familyTab.Controls.Add(_familyGrid);
        ConfigureOpeningGrid();
        openingTab.Controls.Add(_openingGrid);

        tabs.TabPages.Add(floorTab);
        tabs.TabPages.Add(familyTab);
        tabs.TabPages.Add(openingTab);
        root.Controls.Add(tabs, 0, 0);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
        };

        _cancelButton.Text = "Cancel";
        _cancelButton.Width = 96;
        _cancelButton.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(_cancelButton);

        _saveButton.Text = "Save";
        _saveButton.Width = 96;
        _saveButton.Click += (_, _) => SaveMappings();
        actions.Controls.Add(_saveButton);

        _exportButton.Text = "Export...";
        _exportButton.Width = 96;
        _exportButton.Click += (_, _) => ExportMappings();
        actions.Controls.Add(_exportButton);

        _importButton.Text = "Import...";
        _importButton.Width = 96;
        _importButton.Click += (_, _) => ImportMappings();
        actions.Controls.Add(_importButton);

        root.Controls.Add(actions, 0, 1);
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void ConfigureMappingGrid(DataGridView grid, string keyHeader)
    {
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = true;
        grid.AllowUserToDeleteRows = true;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Key",
            HeaderText = keyHeader,
            FillWeight = 55f,
        });

        DataGridViewComboBoxColumn categoryColumn = new()
        {
            Name = "Category",
            HeaderText = "Category",
            FillWeight = 45f,
            FlatStyle = FlatStyle.Flat,
        };
        foreach (string category in _categories)
        {
            categoryColumn.Items.Add(category);
        }

        grid.Columns.Add(categoryColumn);
    }

    private void ConfigureOpeningGrid()
    {
        _openingGrid.Dock = DockStyle.Fill;
        _openingGrid.AutoGenerateColumns = false;
        _openingGrid.AllowUserToAddRows = true;
        _openingGrid.AllowUserToDeleteRows = true;
        _openingGrid.RowHeadersVisible = false;
        _openingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _openingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FamilyName",
            HeaderText = "Accepted Opening Family Name",
            FillWeight = 100f,
        });
    }

    private void LoadMappings()
    {
        LoadResult<ProjectMappingRules> result = _mappingRuleStore.LoadWithDiagnostics(_projectKey);
        PopulateRules(result.Value);
        ShowWarnings(result.Warnings);
    }

    private static void PopulateMappingGrid(DataGridView grid, IReadOnlyDictionary<string, string> mappings)
    {
        grid.Rows.Clear();
        foreach (KeyValuePair<string, string> entry in mappings.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            grid.Rows.Add(entry.Key, entry.Value);
        }
    }

    private void SaveMappings()
    {
        _mappingRuleStore.Save(_projectKey, BuildRulesFromGrid());
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ImportMappings()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Import Project Mappings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        LoadResult<ProjectMappingRules> result = _mappingRuleStore.ImportFromFile(dialog.FileName);
        PopulateRules(result.Value);
        ShowWarnings(result.Warnings);
    }

    private void ExportMappings()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Export Project Mappings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            AddExtension = true,
            FileName = "project-mappings.json",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        _mappingRuleStore.ExportToFile(_projectKey, BuildRulesFromGrid(), dialog.FileName);
    }

    private void PopulateRules(ProjectMappingRules rules)
    {
        PopulateMappingGrid(_floorGrid, rules.FloorCategoryOverrides);
        PopulateMappingGrid(_familyGrid, rules.FamilyCategoryOverrides);

        _openingGrid.Rows.Clear();
        foreach (string familyName in rules.AcceptedOpeningFamilies)
        {
            _openingGrid.Rows.Add(familyName);
        }
    }

    private ProjectMappingRules BuildRulesFromGrid()
    {
        return ProjectMappingRules.Create(
            ReadMappings(_floorGrid),
            ReadMappings(_familyGrid),
            ReadFamilies(_openingGrid));
    }

    private void ShowWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings == null || warnings.Count == 0)
        {
            return;
        }

        MessageBox.Show(
            this,
            string.Join(Environment.NewLine, warnings),
            Text,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static Dictionary<string, string> ReadMappings(DataGridView grid)
    {
        Dictionary<string, string> mappings = new(StringComparer.Ordinal);
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            string key = row.Cells[0].Value?.ToString()?.Trim() ?? string.Empty;
            string category = row.Cells[1].Value?.ToString()?.Trim() ?? string.Empty;
            if (key.Length == 0 || category.Length == 0)
            {
                continue;
            }

            mappings[key] = category;
        }

        return mappings;
    }

    private static List<string> ReadFamilies(DataGridView grid)
    {
        return grid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow)
            .Select(row => row.Cells[0].Value?.ToString()?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
