using System;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter;

internal static class OpeningFamilyClassifier
{
    public static bool IsAcceptedOpening(FamilyInstance instance)
    {
        if (instance == null)
        {
            return false;
        }

        return IsDoorOrWindow(instance) || IsAcceptedElevatorDoorFamily(instance);
    }

    public static bool IsAcceptedElevatorDoorFamily(FamilyInstance instance)
    {
        string familyName = GetFamilyName(instance);
        return OpeningFamilyRules.IsAcceptedElevatorDoorFamilyName(familyName);
    }

    public static string GetFamilyName(FamilyInstance instance)
    {
        if (instance == null)
        {
            return string.Empty;
        }

        return instance.Symbol?.FamilyName?.Trim() ??
               instance.Name?.Trim() ??
               string.Empty;
    }

    private static bool IsDoorOrWindow(FamilyInstance instance)
    {
        Category? category = instance.Category;
        if (category == null)
        {
            return false;
        }

        BuiltInCategory categoryId = (BuiltInCategory)(int)category.Id.Value;
        return categoryId == BuiltInCategory.OST_Doors || categoryId == BuiltInCategory.OST_Windows;
    }
}
