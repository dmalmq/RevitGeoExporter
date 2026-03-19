using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Assignments;

public sealed class PreviewFloorAssignmentSession
{
    private readonly Dictionary<string, string> _savedOverrides;
    private readonly Dictionary<string, string> _pendingOverrides;
    private readonly HashSet<string> _pendingClears;

    public PreviewFloorAssignmentSession(IReadOnlyDictionary<string, string>? savedOverrides = null)
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
        foreach (string floorTypeName in _pendingClears)
        {
            effective.Remove(floorTypeName);
        }

        foreach (KeyValuePair<string, string> entry in _pendingOverrides)
        {
            effective[entry.Key] = entry.Value;
        }

        return effective;
    }

    public void StageOverride(string floorTypeName, string category)
    {
        string normalizedFloorTypeName = NormalizeFloorTypeName(floorTypeName);
        string normalizedCategory = NormalizeCategory(category);
        _pendingClears.Remove(normalizedFloorTypeName);
        _pendingOverrides[normalizedFloorTypeName] = normalizedCategory;
    }

    public void StageClearOverride(string floorTypeName)
    {
        string normalizedFloorTypeName = NormalizeFloorTypeName(floorTypeName);
        _pendingOverrides.Remove(normalizedFloorTypeName);
        _pendingClears.Add(normalizedFloorTypeName);
    }

    public void DiscardPendingChanges()
    {
        _pendingOverrides.Clear();
        _pendingClears.Clear();
    }

    public IReadOnlyDictionary<string, string> ApplyPendingChanges()
    {
        foreach (string floorTypeName in _pendingClears)
        {
            _savedOverrides.Remove(floorTypeName);
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
            string floorTypeName = NormalizeFloorTypeName(entry.Key);
            string category = NormalizeCategory(entry.Value);
            copied[floorTypeName] = category;
        }

        return copied;
    }

    private static string NormalizeFloorTypeName(string? floorTypeName)
    {
        if (string.IsNullOrWhiteSpace(floorTypeName))
        {
            throw new ArgumentException("Floor type name is required.", nameof(floorTypeName));
        }

        return floorTypeName!.Trim();
    }

    private static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category is required.", nameof(category));
        }

        return category!.Trim();
    }
}
