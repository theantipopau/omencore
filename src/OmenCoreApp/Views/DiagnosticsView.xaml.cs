using System.Windows.Controls;

namespace OmenCore.Views
{
    /// <summary>
    /// Diagnostics view containing fan and keyboard testing tools.
    /// GitHub #48 - kg290: Moved diagnostics to separate tab, combined side-by-side.
    /// </summary>
    public partial class DiagnosticsView : UserControl
    {
        public DiagnosticsView()
        {
            InitializeComponent();
        }

        private void OpenDiagnosticsFolder_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var logDir = App.Logging.LogDirectory;
            var target = System.IO.Directory.Exists(logDir) ? logDir : System.IO.Path.GetTempPath();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }

        private void OpenOmenCoreDataFolder_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dataDir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "OmenCore");
            var target = System.IO.Directory.Exists(dataDir) ? dataDir : App.Logging.LogDirectory;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
    }
}
