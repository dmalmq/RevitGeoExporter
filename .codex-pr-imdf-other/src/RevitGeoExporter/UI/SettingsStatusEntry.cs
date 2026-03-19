using System;

namespace RevitGeoExporter.UI;

public sealed class SettingsStatusEntry
{
    public SettingsStatusEntry(SettingsScope scope, string message, string? sourcePath = null)
    {
        string trimmedMessage = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("A message is required.", nameof(message))
            : message.Trim();
        string? trimmedSourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath?.Trim();
        Scope = scope;
        Message = trimmedMessage;
        SourcePath = trimmedSourcePath;
    }

    public SettingsScope Scope { get; }

    public string Message { get; }

    public string? SourcePath { get; }
}
