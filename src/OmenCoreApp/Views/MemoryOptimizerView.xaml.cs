using System.Windows.Controls;

namespace OmenCore.Views
{
    /// <summary>
    /// Interaction logic for MemoryOptimizerView.xaml
    /// </summary>
    public partial class MemoryOptimizerView : UserControl
    {
        public MemoryOptimizerView()
        {
            InitializeComponent();

            // Update memory bar width when loaded and when data changes
            Loaded += (_, _) => UpdateMemoryBar();
            SizeChanged += (_, _) => UpdateMemoryBar();

            if (DataContext is ViewModels.MemoryOptimizerViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(vm.MemoryBarWidth))
                        UpdateMemoryBar();
                };
            }

            DataContextChanged += (_, _) =>
            {
                if (DataContext is ViewModels.MemoryOptimizerViewModel newVm)
                {
                    newVm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(newVm.MemoryBarWidth))
                            UpdateMemoryBar();
                    };
                    UpdateMemoryBar();
                }
            };
        }

        private void UpdateMemoryBar()
        {
            if (DataContext is ViewModels.MemoryOptimizerViewModel vm && MemoryBar != null)
            {
                var parent = MemoryBar.Parent as System.Windows.Controls.Grid;
                if (parent != null && parent.ActualWidth > 0)
                {
                    MemoryBar.Width = parent.ActualWidth * vm.MemoryBarWidth;
                }
            }
        }
    }
}
