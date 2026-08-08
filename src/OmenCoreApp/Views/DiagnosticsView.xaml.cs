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

        /// <summary>
        /// Confirm before restarting the GPU, because the cost is immediate and visible and the
        /// benefit is not. The dialog is here rather than in the view-model so the command stays
        /// callable without a window behind it.
        /// </summary>
        private void ApplyAdapterOverride_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;

            // Names both halves, because they carry different costs and only one of them is
            // visible while it happens. A dialog that mentioned only the GPU restart would be
            // asking consent for the part that is obvious and not for the part that writes to
            // the SMU every thirty seconds afterwards.
            var cpuHalf = vm.CanOfferApuClampLift
                ? $"It then raises the four CPU power limits to {vm.ApuClampTargetDescription}, and puts " +
                  "them back after a resume or a change of supply - the events that take them away. " +
                  "OmenCore cannot read them back, so that half is what the SMU accepted rather than " +
                  "what it is running.\n\n"
                : string.Empty;

            var answer = System.Windows.MessageBox.Show(
                "This disables and re-enables the discrete GPU.\n\n" +
                "Your screens will go black for a few seconds, and any game, render or compute job " +
                "using the GPU will lose it and may crash.\n\n" +
                cpuHalf +
                "Neither figure is an overclock. The GPU lands on the limit its own driver reports as " +
                "default, and the CPU limits are set to the number this machine's firmware states for " +
                "a loaded CPU and GPU together. The clamp is a policy the firmware applies to a supply " +
                "it does not rate highly enough, not a measurement of what that supply can deliver.\n\n" +
                "Nothing here survives a reboot: the GPU limit holds until the firmware tells the " +
                "driver about the adapter again - changing supply does that - and the CPU limits " +
                "until you stop holding them.\n\n" +
                "Drop the clamp now?",
                "Drop the adapter power clamp?",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);

            if (answer != System.Windows.MessageBoxResult.Yes) return;

            if (vm.ApplyAdapterOverrideCommand.CanExecute(null))
            {
                vm.ApplyAdapterOverrideCommand.Execute(null);
            }
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
