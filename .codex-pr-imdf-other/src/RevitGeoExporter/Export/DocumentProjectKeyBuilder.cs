using System;
using System.IO;
using Autodesk.Revit.DB;

namespace RevitGeoExporter.Export;

internal static class DocumentProjectKeyBuilder
{
    public static string Create(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        string path = (document.PathName ?? string.Empty).Trim();
        if (path.Length > 0)
        {
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                // Fall back to the raw document path if normalization fails.
            }

            return $"path:{path.ToUpperInvariant()}";
        }

        string title = (document.Title ?? string.Empty).Trim();
        return title.Length > 0 ? $"title:{title}" : "title:<unsaved>";
    }

    public static string CreateDisplayName(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        string title = (document.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return "Model";
        }

        string withoutExtension = Path.GetFileNameWithoutExtension(title);
        return string.IsNullOrWhiteSpace(withoutExtension) ? title : withoutExtension.Trim();
    }
}
