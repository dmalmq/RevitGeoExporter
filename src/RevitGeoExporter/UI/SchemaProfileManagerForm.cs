using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;

namespace RevitGeoExporter.UI;

public sealed class SchemaProfileManagerForm : Form
{
    private readonly UiLanguage _language;
    private readonly List<SchemaProfile> _profiles;
    private readonly ListBox _profileList = new();
    private readonly TextBox _nameTextBox = new();
    private readonly DataGridView _mappingGrid = new();
    private readonly Label _helpLabel = new();
    private int _selectedProfileIndex = -1;

    public SchemaProfileManagerForm(IReadOnlyList<SchemaProfile>? profiles, UiLanguage language)
    {
        _language = language;
        _profiles = SchemaProfile.NormalizeProfiles(profiles)
            .Select(profile => profile.Clone())
            .ToList();

        InitializeComponents();
        PopulateProfileList();
        if (_profiles.Count > 0)
        {
            _profileList.SelectedIndex = 0;
        }
    }

    public IReadOnlyList<SchemaProfile> Profiles { get; private set; } = new List<SchemaProfile>();

    private void InitializeComponents()
    {
        Text = T("Schema Profiles", "スキーマ プロファイル");
        Width = 1220;
        Height = 720;
        MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterParent;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        Controls.Add(root);

        root.Controls.Add(BuildProfilesPanel(), 0, 0);
        root.Controls.Add(BuildEditorPanel(), 1, 0);
        root.Controls.Add(BuildActionsPanel(), 0, 1);
        root.SetColumnSpan(root.GetControlFromPosition(0, 1), 2);
    }

    private Control BuildProfilesPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = T("Profiles", "プロファイル"),
        }, 0, 0);

        _profileList.Dock = DockStyle.Fill;
        _profileList.SelectedIndexChanged += (_, _) => OnProfileSelectionChanged();
        panel.Controls.Add(_profileList, 0, 1);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };

        Button addButton = new() { Width = 72, Height = 26, Text = T("Add", "追加") };
        addButton.Click += (_, _) => AddProfile();
        actions.Controls.Add(addButton);

        Button deleteButton = new() { Width = 72, Height = 26, Text = T("Delete", "削除") };
        deleteButton.Click += (_, _) => DeleteSelectedProfile();
        actions.Controls.Add(deleteButton);

        panel.Controls.Add(actions, 0, 2);
        return panel;
    }

    private Control BuildEditorPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8, 0, 0, 0),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        TableLayoutPanel namePanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        namePanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = T("Profile name", "プロファイル名"),
        }, 0, 0);
        _nameTextBox.Dock = DockStyle.Fill;
        namePanel.Controls.Add(_nameTextBox, 1, 0);
        panel.Controls.Add(namePanel, 0, 0);

        _helpLabel.Dock = DockStyle.Fill;
        _helpLabel.TextAlign = ContentAlignment.MiddleLeft;
        _helpLabel.Text = T(
            "Use enum names in Source Key for derived/meta mappings, for example: ExportId, ViewName, SourceDocumentKey.",
            "派生値 / メタデータの Source Key には列挙名を使います。例: ExportId, ViewName, SourceDocumentKey");
        panel.Controls.Add(_helpLabel, 0, 1);

        ConfigureMappingGrid();
        panel.Controls.Add(_mappingGrid, 0, 2);
        return panel;
    }

    private Control BuildActionsPanel()
    {
        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
        };

        Button cancelButton = new()
        {
            Width = 96,
            Height = 28,
            Text = T("Cancel", "キャンセル"),
            DialogResult = DialogResult.Cancel,
        };
        panel.Controls.Add(cancelButton);

        Button saveButton = new()
        {
            Width = 96,
            Height = 28,
            Text = T("Save", "保存"),
        };
        saveButton.Click += (_, _) => SaveAndClose();
        panel.Controls.Add(saveButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        return panel;
    }

    private void ConfigureMappingGrid()
    {
        _mappingGrid.Dock = DockStyle.Fill;
        _mappingGrid.AutoGenerateColumns = false;
        _mappingGrid.AllowUserToAddRows = true;
        _mappingGrid.AllowUserToDeleteRows = true;
        _mappingGrid.RowHeadersVisible = false;
        _mappingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _mappingGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Layer",
            HeaderText = T("Layer", "レイヤー"),
            DataSource = Enum.GetValues(typeof(SchemaLayerType)),
            FillWeight = 14f,
        });
        _mappingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FieldName",
            HeaderText = T("Field Name", "フィールド名"),
            FillWeight = 18f,
        });
        _mappingGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "SourceKind",
            HeaderText = T("Source", "取得元"),
            DataSource = Enum.GetValues(typeof(CustomAttributeSourceKind)),
            FillWeight = 14f,
        });
        _mappingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SourceKey",
            HeaderText = T("Source Key", "Source Key"),
            FillWeight = 18f,
        });
        _mappingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ConstantValue",
            HeaderText = T("Constant", "定数"),
            FillWeight = 14f,
        });
        _mappingGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "TargetType",
            HeaderText = T("Type", "型"),
            DataSource = Enum.GetValues(typeof(ExportAttributeType)),
            FillWeight = 10f,
        });
        _mappingGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "NullBehavior",
            HeaderText = T("Null Handling", "Null の扱い"),
            DataSource = Enum.GetValues(typeof(CustomAttributeNullBehavior)),
            FillWeight = 14f,
        });
        _mappingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DefaultValue",
            HeaderText = T("Default", "既定値"),
            FillWeight = 14f,
        });
    }

    private void PopulateProfileList()
    {
        _profileList.Items.Clear();
        foreach (SchemaProfile profile in _profiles)
        {
            _profileList.Items.Add(profile.Name);
        }
    }

    private void OnProfileSelectionChanged()
    {
        CommitCurrentProfile();
        _selectedProfileIndex = _profileList.SelectedIndex;
        if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _profiles.Count)
        {
            _nameTextBox.Text = string.Empty;
            _mappingGrid.Rows.Clear();
            return;
        }

        LoadProfile(_profiles[_selectedProfileIndex]);
    }

    private void LoadProfile(SchemaProfile profile)
    {
        _nameTextBox.Text = profile.Name;
        _mappingGrid.Rows.Clear();
        foreach (CustomAttributeMapping mapping in profile.Mappings ?? new List<CustomAttributeMapping>())
        {
            _mappingGrid.Rows.Add(
                mapping.Layer,
                mapping.FieldName,
                mapping.SourceKind,
                mapping.SourceKey,
                mapping.ConstantValue,
                mapping.TargetType,
                mapping.NullBehavior,
                mapping.DefaultValue);
        }
    }

    private void CommitCurrentProfile()
    {
        if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _profiles.Count)
        {
            return;
        }

        SchemaProfile profile = _profiles[_selectedProfileIndex];
        profile.Name = BuildUniqueProfileName(_nameTextBox.Text, _selectedProfileIndex);
        profile.Mappings = ReadMappingsFromGrid();
        if (_profileList.Items.Count > _selectedProfileIndex)
        {
            _profileList.Items[_selectedProfileIndex] = profile.Name;
        }
    }

    private List<CustomAttributeMapping> ReadMappingsFromGrid()
    {
        List<CustomAttributeMapping> mappings = new();
        foreach (DataGridViewRow row in _mappingGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            string fieldName = (row.Cells["FieldName"].Value?.ToString() ?? string.Empty).Trim();
            if (fieldName.Length == 0)
            {
                continue;
            }

            mappings.Add(new CustomAttributeMapping
            {
                Layer = ReadEnum<SchemaLayerType>(row.Cells["Layer"].Value, SchemaLayerType.Unit),
                FieldName = fieldName,
                SourceKind = ReadEnum<CustomAttributeSourceKind>(row.Cells["SourceKind"].Value, CustomAttributeSourceKind.RevitParameter),
                SourceKey = (row.Cells["SourceKey"].Value?.ToString() ?? string.Empty).Trim(),
                ConstantValue = (row.Cells["ConstantValue"].Value?.ToString() ?? string.Empty).Trim(),
                TargetType = ReadEnum<ExportAttributeType>(row.Cells["TargetType"].Value, ExportAttributeType.Text),
                NullBehavior = ReadEnum<CustomAttributeNullBehavior>(row.Cells["NullBehavior"].Value, CustomAttributeNullBehavior.PreserveNull),
                DefaultValue = (row.Cells["DefaultValue"].Value?.ToString() ?? string.Empty).Trim(),
            });
        }

        return mappings;
    }

    private void AddProfile()
    {
        CommitCurrentProfile();
        SchemaProfile profile = new()
        {
            Name = BuildUniqueProfileName(T("New schema", "新しいスキーマ"), null),
            Mappings = new List<CustomAttributeMapping>(),
        };
        _profiles.Add(profile);
        PopulateProfileList();
        _profileList.SelectedIndex = _profiles.Count - 1;
    }

    private void DeleteSelectedProfile()
    {
        if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _profiles.Count)
        {
            return;
        }

        _profiles.RemoveAt(_selectedProfileIndex);
        if (_profiles.Count == 0)
        {
            _profiles.Add(SchemaProfile.CreateCoreProfile());
        }

        PopulateProfileList();
        _profileList.SelectedIndex = Math.Min(_selectedProfileIndex, _profiles.Count - 1);
    }

    private void SaveAndClose()
    {
        CommitCurrentProfile();
        Profiles = SchemaProfile.NormalizeProfiles(_profiles)
            .Select(profile => profile.Clone())
            .ToList();
        DialogResult = DialogResult.OK;
        Close();
    }

    private string BuildUniqueProfileName(string? proposedName, int? currentIndex)
    {
        string baseName = string.IsNullOrWhiteSpace(proposedName)
            ? T("Schema", "スキーマ")
            : proposedName.Trim();

        HashSet<string> existingNames = new(
            _profiles
                .Where((_, index) => !currentIndex.HasValue || index != currentIndex.Value)
                .Select(profile => profile.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (int i = 2; i < 1000; i++)
        {
            string candidate = $"{baseName} {i}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {Guid.NewGuid():N}";
    }

    private static TEnum ReadEnum<TEnum>(object? value, TEnum fallback)
        where TEnum : struct
    {
        if (value is TEnum enumValue)
        {
            return enumValue;
        }

        return Enum.TryParse(value?.ToString(), ignoreCase: true, out TEnum parsed) ? parsed : fallback;
    }

    private string T(string english, string japanese)
    {
        return UiLanguageText.Select(_language, english, japanese);
    }
}
