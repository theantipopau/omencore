using System.Windows;
using System.Windows.Controls;
using OmenCore.ViewModels;

namespace OmenCore.Views
{
    /// <summary>
    /// Interaction logic for BloatwareManagerView.xaml
    /// </summary>
    public partial class BloatwareManagerView : UserControl
    {
        public BloatwareManagerView()
        {
            InitializeComponent();
            
            // v2.7.1: Auto-scan when view is loaded
            Loaded += OnLoaded;
        }
        
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Only scan once on first load
            Loaded -= OnLoaded;
            
            if (DataContext is BloatwareManagerViewModel vm && vm.TotalCount == 0)
            {
                await vm.ScanAsync();
            }
        }
    }
}
