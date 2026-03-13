using System.Windows.Forms;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Help;

public static class HelpLauncher
{
    public static void Show(IWin32Window? owner, HelpTopic topic, UiLanguage language, string? contextLabel = null)
    {
        HelpContentProvider provider = new();
        using HelpViewerWindow viewer = new(provider, topic, HelpContentProvider.FromUiLanguage(language), contextLabel, owner);
        _ = viewer.ShowDialog();
    }
}
