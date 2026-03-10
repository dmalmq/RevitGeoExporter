using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;

namespace RevitGeoExporter.Core;

public sealed class SharedParameterManager
{
    public const string ImdfIdParameterName = "IMDF_Id";
    public const string ImdfLevelIdParameterName = "IMDF_LevelId";
    public const string ImdfNameParameterName = "IMDF_Name";
    public const string ImdfAltNameParameterName = "IMDF_AltName";

    private const string SharedParameterGroupName = "RevitGeoExporter";

    private static readonly BuiltInCategory[] UnitIdCategories =
    {
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Rooms,
        BuiltInCategory.OST_Stairs,
        BuiltInCategory.OST_GenericModel,
        BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_Windows,
    };

    private static readonly BuiltInCategory[] UnitNameCategories =
    {
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Rooms,
        BuiltInCategory.OST_Stairs,
        BuiltInCategory.OST_GenericModel,
    };

    private static readonly BuiltInCategory[] LevelCategories =
    {
        BuiltInCategory.OST_Levels,
    };

    private readonly Document _document;
    private readonly Autodesk.Revit.ApplicationServices.Application _application;

    public SharedParameterManager(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _application = _document.Application;
    }

    public Document Document => _document;

    public void EnsureParameters(ICollection<string> warnings)
    {
        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        string sharedParameterFile = GetDefaultSharedParameterFilePath();
        string originalPath = _application.SharedParametersFilename;
        EnsureSharedParameterFileExists(sharedParameterFile);

        _application.SharedParametersFilename = sharedParameterFile;
        try
        {
            DefinitionFile? file = _application.OpenSharedParameterFile();
            if (file == null)
            {
                warnings.Add($"Unable to open shared parameter file at '{sharedParameterFile}'.");
                return;
            }

            DefinitionGroup? group = file.Groups.get_Item(SharedParameterGroupName) ??
                                     file.Groups.Create(SharedParameterGroupName);
            EnsureStringParameterBinding(group, ImdfIdParameterName, UnitIdCategories, warnings);
            EnsureStringParameterBinding(group, ImdfLevelIdParameterName, LevelCategories, warnings);
            EnsureStringParameterBinding(group, ImdfNameParameterName, UnitNameCategories, warnings);
            EnsureStringParameterBinding(group, ImdfAltNameParameterName, UnitNameCategories, warnings);
        }
        finally
        {
            _application.SharedParametersFilename = originalPath;
        }
    }

    public void EnsureLevelIds(IEnumerable<Level> levels, ICollection<string> warnings)
    {
        if (levels is null)
        {
            throw new ArgumentNullException(nameof(levels));
        }

        foreach (Level level in levels)
        {
            _ = GetOrCreateStringParameter(level, ImdfLevelIdParameterName, warnings);
        }
    }

    public void EnsureElementIds(IEnumerable<Element> elements, ICollection<string> warnings)
    {
        if (elements is null)
        {
            throw new ArgumentNullException(nameof(elements));
        }

        foreach (Element element in elements)
        {
            _ = GetOrCreateStringParameter(element, ImdfIdParameterName, warnings);
        }
    }

    public string GetOrCreateElementId(Element element, ICollection<string> warnings)
    {
        return GetOrCreateStringParameter(element, ImdfIdParameterName, warnings);
    }

    public string GetOrCreateLevelId(Level level, ICollection<string> warnings)
    {
        return GetOrCreateStringParameter(level, ImdfLevelIdParameterName, warnings);
    }

    public string? GetOptionalStringParameter(Element element, string parameterName)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (string.IsNullOrWhiteSpace(parameterName))
        {
            throw new ArgumentException("Parameter name is required.", nameof(parameterName));
        }

        Parameter? parameter = element.LookupParameter(parameterName);
        if (parameter == null || parameter.StorageType != StorageType.String)
        {
            return null;
        }

        string? value = parameter.AsString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string RegenerateElementId(Element element, ICollection<string> warnings)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        string newValue = Guid.NewGuid().ToString();
        return TrySetStringParameter(element, ImdfIdParameterName, newValue, warnings)
            ? newValue
            : string.Empty;
    }

    private static string GetDefaultSharedParameterFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string directory = Path.Combine(appData, "RevitGeoExporter");
        return Path.Combine(directory, "RevitGeoExporter.SharedParameters.txt");
    }

    private static void EnsureSharedParameterFileExists(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(filePath))
        {
            File.WriteAllText(filePath, string.Empty);
        }
    }

    private void EnsureStringParameterBinding(
        DefinitionGroup group,
        string parameterName,
        IReadOnlyList<BuiltInCategory> categories,
        ICollection<string> warnings)
    {
        Definition? definition = group.Definitions.get_Item(parameterName);
        if (definition == null)
        {
            ExternalDefinitionCreationOptions options =
                new(parameterName, SpecTypeId.String.Text)
                {
                    UserModifiable = true,
                    Visible = true,
                };
            definition = group.Definitions.Create(options);
        }

        CategorySet categorySet = _application.Create.NewCategorySet();
        for (int i = 0; i < categories.Count; i++)
        {
            Category? category = Category.GetCategory(_document, categories[i]);
            if (category != null)
            {
                categorySet.Insert(category);
            }
        }

        if (categorySet.Size == 0)
        {
            warnings.Add($"No valid categories found for shared parameter '{parameterName}'.");
            return;
        }

        Binding binding = _application.Create.NewInstanceBinding(categorySet);
        BindingMap map = _document.ParameterBindings;
        bool inserted = map.Insert(definition, binding, GroupTypeId.Data);
        if (!inserted)
        {
            map.ReInsert(definition, binding, GroupTypeId.Data);
        }
    }

    private string GetOrCreateStringParameter(
        Element element,
        string parameterName,
        ICollection<string> warnings)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        Parameter? parameter = element.LookupParameter(parameterName);
        if (parameter == null || parameter.StorageType != StorageType.String)
        {
            string generated = Guid.NewGuid().ToString();
            warnings.Add(
                $"Element {element.Id.Value} is missing string parameter '{parameterName}'. Generated non-persisted ID '{generated}'.");
            return generated;
        }

        string? currentValue = parameter.AsString();
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            return currentValue.Trim();
        }

        if (parameter.IsReadOnly)
        {
            string generated = Guid.NewGuid().ToString();
            warnings.Add(
                $"Element {element.Id.Value} parameter '{parameterName}' is read-only. Generated non-persisted ID '{generated}'.");
            return generated;
        }

        string newValue = Guid.NewGuid().ToString();
        bool setResult = parameter.Set(newValue);
        if (!setResult)
        {
            warnings.Add(
                $"Failed to write parameter '{parameterName}' for element {element.Id.Value}. Generated non-persisted ID '{newValue}'.");
        }

        return newValue;
    }

    private static bool TrySetStringParameter(
        Element element,
        string parameterName,
        string value,
        ICollection<string> warnings)
    {
        Parameter? parameter = element.LookupParameter(parameterName);
        if (parameter == null || parameter.StorageType != StorageType.String)
        {
            warnings.Add($"Element {element.Id.Value} is missing string parameter '{parameterName}', so the value could not be updated.");
            return false;
        }

        if (parameter.IsReadOnly)
        {
            warnings.Add($"Element {element.Id.Value} parameter '{parameterName}' is read-only, so the value could not be updated.");
            return false;
        }

        if (!parameter.Set(value))
        {
            warnings.Add($"Failed to update parameter '{parameterName}' for element {element.Id.Value}.");
            return false;
        }

        return true;
    }
}