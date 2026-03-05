using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace RevitGeoExporter;

/// <summary>
/// Placeholder entry file for the Revit add-in project.
/// Phase 1 wiring to Revit API commands is added after core export logic is stable.
/// </summary>
internal static class ProjectInfo
{
    public const string Name = "RevitGeoExporter";
    public const string RibbonTabName = "GeoExporter";
    public const int DefaultTargetEpsg = 6677;

    private static string? _displayVersion;

    public static string DisplayVersion => _displayVersion ??= ResolveDisplayVersion();

    public static string VersionTag => $"v{DisplayVersion}";

    private static string ResolveDisplayVersion()
    {
        try
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string? assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            if (!string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                string versionFilePath = Path.Combine(assemblyDirectory, "version.txt");
                if (File.Exists(versionFilePath))
                {
                    string versionFromFile = (File.ReadAllText(versionFilePath) ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(versionFromFile))
                    {
                        return versionFromFile;
                    }
                }
            }

            string? registryVersion = TryGetInstalledDisplayVersionFromRegistry();
            if (!string.IsNullOrWhiteSpace(registryVersion))
            {
                return registryVersion;
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(assemblyPath);
            string version = (info.ProductVersion ?? info.FileVersion ?? string.Empty).Trim();
            int plusIndex = version.IndexOf('+');
            if (plusIndex >= 0)
            {
                version = version.Substring(0, plusIndex).Trim();
            }

            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }
        catch (Exception)
        {
            // Fallback below.
        }

        return "unknown";
    }

    private static string? TryGetInstalledDisplayVersionFromRegistry()
    {
        string[] uninstallRoots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        for (int i = 0; i < uninstallRoots.Length; i++)
        {
            string rootPath = uninstallRoots[i];

            string? fromMachine = TryFindDisplayVersionInHive(Registry.LocalMachine, rootPath);
            if (!string.IsNullOrWhiteSpace(fromMachine))
            {
                return fromMachine;
            }

            string? fromUser = TryFindDisplayVersionInHive(Registry.CurrentUser, rootPath);
            if (!string.IsNullOrWhiteSpace(fromUser))
            {
                return fromUser;
            }
        }

        return null;
    }

    private static string? TryFindDisplayVersionInHive(RegistryKey hive, string uninstallRootPath)
    {
        try
        {
            using RegistryKey? root = hive.OpenSubKey(uninstallRootPath);
            if (root == null)
            {
                return null;
            }

            string[] subKeyNames = root.GetSubKeyNames();
            for (int i = 0; i < subKeyNames.Length; i++)
            {
                using RegistryKey? appKey = root.OpenSubKey(subKeyNames[i]);
                string displayName = (appKey?.GetValue("DisplayName") as string ?? string.Empty).Trim();
                if (!displayName.Equals(Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string displayVersion = (appKey?.GetValue("DisplayVersion") as string ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(displayVersion))
                {
                    return displayVersion;
                }
            }
        }
        catch (Exception)
        {
            // Ignore registry access errors and continue fallback chain.
        }

        return null;
    }
}
