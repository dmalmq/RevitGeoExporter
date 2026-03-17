using System;
using System.Collections.Generic;

namespace RevitGeoExporter.UI;

public sealed class SettingsImportResult
{
    public SettingsImportResult(
        SettingsScope scope,
        bool succeeded,
        IEnumerable<SettingsStatusEntry>? statuses = null)
    {
        Scope = scope;
        Succeeded = succeeded;
        Statuses = (statuses ?? Array.Empty<SettingsStatusEntry>()) as IReadOnlyList<SettingsStatusEntry>
            ?? new List<SettingsStatusEntry>(statuses ?? Array.Empty<SettingsStatusEntry>());
    }

    public SettingsScope Scope { get; }

    public bool Succeeded { get; }

    public IReadOnlyList<SettingsStatusEntry> Statuses { get; }
}
