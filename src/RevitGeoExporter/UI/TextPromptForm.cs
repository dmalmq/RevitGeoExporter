using System;
using System.Drawing;
using System.Windows.Forms;

namespace RevitGeoExporter.UI;

public sealed class TextPromptForm : Form
{
    private readonly TextBox _textBox = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();

    public TextPromptForm(string title, string prompt, string initialValue = "")
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("A prompt is required.", nameof(prompt));
        }

        Text = title.Trim();
        Width = 420;
        Height = 160;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        Controls.Add(root);

        Label promptLabel = new()
        {
            Dock = DockStyle.Fill,
            Text = prompt.Trim(),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        root.Controls.Add(promptLabel, 0, 0);

        _textBox.Dock = DockStyle.Fill;
        _textBox.Text = initialValue ?? string.Empty;
        root.Controls.Add(_textBox, 0, 1);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };

        _cancelButton.Text = "Cancel";
        _cancelButton.Width = 90;
        _cancelButton.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(_cancelButton);

        _okButton.Text = "OK";
        _okButton.Width = 90;
        _okButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_textBox.Text))
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.Add(_okButton);

        root.Controls.Add(actions, 0, 2);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    public string Value => (_textBox.Text ?? string.Empty).Trim();
}
