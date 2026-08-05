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
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Refresh the adapter panel when the tab is shown. Read-only WMI query, and doing it here
        /// rather than on a timer means the cost is paid only when someone is looking at it - while
        /// still being current, since the value changes only on a physical plug/unplug.
        /// </summary>
        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Dispatched at Background priority rather than run inline: GetAdapter() is a WMI round
            // trip, and on a board that does not implement the command it falls through to the legacy
            // System.Management path, which has no timeout at all. Inline, that turns clicking this
            // tab into a multi-second freeze on a machine with a degraded WMI repository. Let the tab
            // paint first.
            Dispatcher.InvokeAsync(
                () => (DataContext as ViewModels.MainViewModel)?.RefreshPowerAdapterStatus(),
                System.Windows.Threading.DispatcherPriority.Background);
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
