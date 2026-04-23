using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OmenCore.ViewModels;

namespace OmenCore.Views
{
    /// <summary>
    /// Interaction logic for SystemOptimizerView.xaml
    /// </summary>
    public partial class SystemOptimizerView : UserControl
    {
        public SystemOptimizerView()
        {
            InitializeComponent();
        }

        private async void OnToggleClicked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.Tag is OptimizationItem item)
            {
                var desiredState = toggle.IsChecked == true;

                // Keep the UI on the last authoritative state until the async apply/revert completes.
                toggle.IsChecked = item.IsEnabled;
                await item.Toggle(desiredState);
            }
        }
    }
}
