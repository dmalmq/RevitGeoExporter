using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Assignments;

public sealed class PreviewCategoryAssignmentSession
{
    private readonly Dictionary<string, string> _savedOverrides;
    private readonly Dictionary<string, string> _pendingOverrides;
    private readonly HashSet<string> _pendingClears;

    public PreviewCategoryAssignmentSession(IReadOnlyDictionary<string, string>? savedOverrides = null)
    {
        _savedOverrides = CopyOverrides(savedOverrides);
        _pendingOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        _pendingClears = new HashSet<string>(StringComparer.Ordinal);
    }

    public bool HasPendingChanges => _pendingOverrides.Count > 0 || _pendingClears.Count > 0;

    public IReadOnlyDictionary<string, string> SavedOverrides => CopyOverrides(_savedOverrides);

    public IReadOnlyDictionary<string, string> GetEffectiveOverrides()
    {
        Dictionary<string, string> effective = CopyOverrides(_savedOverrides);
        foreach (string key in _pendingClears)
        {
            effective.Remove(key);
        }

        foreach (KeyValuePair<string, string> entry in _pendingOverrides)
        {
            effective[entry.Key] = entry.Value;
        }

        return effective;
    }

    public void StageOverride(string key, string category)
    {
        string normalizedKey = NormalizeRequiredValue(key, nameof(key));
        string normalizedCategory = NormalizeRequiredValue(category, nameof(category));
        _pendingClears.Remove(normalizedKey);
        _pendingOverrides[normalizedKey] = normalizedCategory;
    }

    public void StageClearOverride(string key)
    {
        string normalizedKey = NormalizeRequiredValue(key, nameof(key));
        _pendingOverrides.Remove(normalizedKey);
        _pendingClears.Add(normalizedKey);
    }

    public void DiscardPendingChanges()
    {
        _pendingOverrides.Clear();
        _pendingClears.Clear();
    }

    public IReadOnlyDictionary<string, string> ApplyPendingChanges()
    {
        foreach (string key in _pendingClears)
        {
            _savedOverrides.Remove(key);
        }

        foreach (KeyValuePair<string, string> entry in _pendingOverrides)
        {
            _savedOverrides[entry.Key] = entry.Value;
        }

        DiscardPendingChanges();
        return SavedOverrides;
    }

    private static Dictionary<string, string> CopyOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        Dictionary<string, string> copied = new(StringComparer.Ordinal);
        if (overrides == null)
        {
            return copied;
        }

        foreach (KeyValuePair<string, string> entry in overrides)
        {
            string key = NormalizeRequiredValue(entry.Key, nameof(overrides));
            string category = NormalizeRequiredValue(entry.Value, nameof(overrides));
            copied[key] = category;
        }

        return copied;
    }

    private static string NormalizeRequiredValue(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }
}
