using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.UI;

public sealed class ValidationPolicyManagerForm : Form
{
    private readonly UiLanguage _language;
    private readonly List<ValidationPolicyProfile> _profiles;
    private readonly ListBox _profileList = new();
    private readonly TextBox _nameTextBox = new();
    private readonly DataGridView _settingsGrid = new();
    private readonly Label _helpLabel = new();
    private int _selectedProfileIndex = -1;

    public ValidationPolicyManagerForm(IReadOnlyList<ValidationPolicyProfile>? profiles, UiLanguage language)
    {
        _language = language;
        _profiles = ValidationPolicyProfile.NormalizeProfiles(profiles)
            .Select(profile => profile.Clone())
            .ToList();

        InitializeComponents();
        PopulateProfileList();
        if (_profiles.Count > 0)
        {
            _profileList.SelectedIndex = 0;
        }
    }

    public IReadOnlyList<ValidationPolicyProfile> Profiles { get; private set; } = new List<ValidationPolicyProfile>();

    private void InitializeComponents()
    {
        Text = T("Validation Policies", "検証ポリシー");
        Width = 980;
        Height = 680;
        MinimumSize = new Size(860, 560);
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
            Text = T("Policies", "ポリシー"),
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
        namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140f));
        namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        namePanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = T("Policy name", "ポリシー名"),
        }, 0, 0);
        _nameTextBox.Dock = DockStyle.Fill;
        namePanel.Controls.Add(_nameTextBox, 1, 0);
        panel.Controls.Add(namePanel, 0, 0);

        _helpLabel.Dock = DockStyle.Fill;
        _helpLabel.TextAlign = ContentAlignment.MiddleLeft;
        _helpLabel.Text = T(
            "Choose how each validation target should be surfaced during readiness checks and export validation.",
            "各検証項目を、準備チェックと出力検証でどの重要度として扱うかを設定します。");
        panel.Controls.Add(_helpLabel, 0, 1);

        ConfigureSettingsGrid();
        panel.Controls.Add(_settingsGrid, 0, 2);
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

    private void ConfigureSettingsGrid()
    {
        _settingsGrid.Dock = DockStyle.Fill;
        _settingsGrid.AutoGenerateColumns = false;
        _settingsGrid.AllowUserToAddRows = false;
        _settingsGrid.AllowUserToDeleteRows = false;
        _settingsGrid.RowHeadersVisible = false;
        _settingsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _settingsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Target",
            HeaderText = T("Validation Target", "検証項目"),
            ReadOnly = true,
            FillWeight = 62f,
        });
        _settingsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Severity",
            HeaderText = T("Severity", "重要度"),
            DataSource = Enum.GetValues(typeof(ValidationSeverity)),
            FillWeight = 38f,
        });
    }

    private void PopulateProfileList()
    {
        _profileList.Items.Clear();
        foreach (ValidationPolicyProfile profile in _profiles)
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
            _settingsGrid.Rows.Clear();
            return;
        }

        ValidationPolicyProfile profile = _profiles[_selectedProfileIndex];
        _nameTextBox.Text = profile.Name;
        _settingsGrid.Rows.Clear();
        foreach (ValidationPolicySetting setting in profile.Settings.OrderBy(setting => setting.Target))
        {
            int rowIndex = _settingsGrid.Rows.Add(GetTargetDisplayName(setting.Target), setting.Severity);
            _settingsGrid.Rows[rowIndex].Tag = setting.Target;
        }
    }

    private void CommitCurrentProfile()
    {
        if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _profiles.Count)
        {
            return;
        }

        List<ValidationPolicySetting> settings = new();
        foreach (DataGridViewRow row in _settingsGrid.Rows)
        {
            if (row.Tag is not ValidationPolicyTarget target)
            {
                continue;
            }

            ValidationSeverity severity = row.Cells["Severity"].Value is ValidationSeverity value
                ? value
                : ValidationSeverity.Warning;
            settings.Add(new ValidationPolicySetting(target, severity));
        }

        _profiles[_selectedProfileIndex] = new ValidationPolicyProfile(
            (_nameTextBox.Text ?? string.Empty).Trim(),
            settings);
        _profileList.Items[_selectedProfileIndex] = _profiles[_selectedProfileIndex].Name;
    }

    private void AddProfile()
    {
        CommitCurrentProfile();
        ValidationPolicyProfile profile = ValidationPolicyProfile.CreateRecommendedProfile();
        profile.Name = CreateUniqueProfileName(T("Custom Policy", "カスタム ポリシー"));
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

        int nextIndex = Math.Max(0, _selectedProfileIndex - 1);
        _profiles.RemoveAt(_selectedProfileIndex);
        PopulateProfileList();

        if (_profiles.Count == 0)
        {
            _selectedProfileIndex = -1;
            _nameTextBox.Text = string.Empty;
            _settingsGrid.Rows.Clear();
            return;
        }

        _profileList.SelectedIndex = Math.Min(nextIndex, _profiles.Count - 1);
    }

    private void SaveAndClose()
    {
        CommitCurrentProfile();

        List<string> duplicateNames = _profiles
            .Select(profile => (profile.Name ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateNames.Count > 0)
        {
            MessageBox.Show(
                this,
                T("Each validation policy must have a unique name.", "各検証ポリシーには一意の名前が必要です。"),
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        Profiles = ValidationPolicyProfile.NormalizeProfiles(_profiles)
            .Select(profile => profile.Clone())
            .ToList();
        DialogResult = DialogResult.OK;
        Close();
    }

    private string CreateUniqueProfileName(string baseName)
    {
        string trimmedBaseName = string.IsNullOrWhiteSpace(baseName) ? "Policy" : baseName.Trim();
        string candidate = trimmedBaseName;
        int suffix = 2;
        while (_profiles.Any(profile => string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{trimmedBaseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private string GetTargetDisplayName(ValidationPolicyTarget target)
    {
        return target switch
        {
            ValidationPolicyTarget.MissingNames => T("Missing names", "名前の欠落"),
            ValidationPolicyTarget.UnmappedCategories => T("Unmapped categories", "未割り当てカテゴリ"),
            ValidationPolicyTarget.DuplicateStableIds => T("Duplicate stable IDs", "重複した安定 ID"),
            ValidationPolicyTarget.LinkedFallbackIds => T("Linked fallback IDs", "リンクのフォールバック ID"),
            ValidationPolicyTarget.UnsupportedOpeningFamilies => T("Unsupported opening families", "未対応の開口ファミリ"),
            ValidationPolicyTarget.GeoreferenceWarnings => T("Georeference warnings", "ジオリファレンス警告"),
            ValidationPolicyTarget.UnsnappedOpenings => T("Unsnapped openings", "スナップされていない開口"),
            _ => target.ToString(),
        };
    }

    private string T(string english, string japanese)
    {
        return UiLanguageText.Select(_language, english, japanese);
    }
}
