using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Models;

public static class OpeningFamilyRules
{
    private static readonly HashSet<string> AcceptedElevatorDoorFamilyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "\u0045\u0056\u6249",
        };

    public static bool IsAcceptedElevatorDoorFamilyName(string? familyName)
    {
        string normalized = (familyName ?? string.Empty).Trim();
        return normalized.Length > 0 && AcceptedElevatorDoorFamilyNames.Contains(normalized);
    }
}
