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
            var temp = System.IO.Path.GetTempPath();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = temp,
                UseShellExecute = true
            });
        }
    }
}
