using System.Threading;
using System.Windows.Forms;

namespace RevitGeoExporter.UI;

internal static class WinFormsVisualStyleBootstrapper
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
}
