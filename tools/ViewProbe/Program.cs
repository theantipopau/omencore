using System.Reflection;
using System.IO;
using System.Windows;
using System.Text;

namespace ViewProbe;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "view-probe-output.txt");
        var output = new StringBuilder();
        var app = new OmenCore.App();
        app.InitializeComponent();

        string[] views =
        {
            "OmenCore.Views.MainWindow",
            "OmenCore.Views.FanControlView",
            "OmenCore.Views.SystemControlView",
            "OmenCore.Views.MemoryOptimizerView",
            "OmenCore.Views.SystemOptimizerView",
            "OmenCore.Views.BloatwareManagerView",
            "OmenCore.Views.LightingView",
            "OmenCore.Views.SettingsView",
            "OmenCore.Views.TuningView",
            "OmenCore.Views.GameLibraryView"
        };

        var assembly = typeof(OmenCore.App).Assembly;

        foreach (var viewName in views)
        {
            output.AppendLine($"TEST {viewName}");
            try
            {
                var type = assembly.GetType(viewName, throwOnError: true)!;
                _ = Activator.CreateInstance(type);
                output.AppendLine("  OK");
            }
            catch (Exception ex)
            {
                PrintException(output, ex, "  ");
            }
        }

        File.WriteAllText(outputPath, output.ToString());

        return 0;
    }

    private static void PrintException(StringBuilder output, Exception ex, string indent)
    {
        output.AppendLine($"{indent}FAIL: {ex.GetType().FullName} :: {ex.Message}");
        if (ex.InnerException != null)
        {
            PrintException(output, ex.InnerException, indent + "  ");
        }
    }
}