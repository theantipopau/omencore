using System.ComponentModel;
using System.Windows.Controls;

namespace OmenCore.Views
{
    /// <summary>
    /// Interaction logic for MemoryOptimizerView.xaml
    /// </summary>
    public partial class MemoryOptimizerView : UserControl
    {
        private ViewModels.MemoryOptimizerViewModel? _subscribedVm;

        public MemoryOptimizerView()
        {
            InitializeComponent();

            // Update memory bar width when loaded and when data changes
            Loaded += (_, _) => { UpdateMemoryBar(); NotifyPageActive(true); };
            Unloaded += (_, _) => NotifyPageActive(false);
            SizeChanged += (_, _) => UpdateMemoryBar();
            IsVisibleChanged += (_, e) => NotifyPageActive((bool)e.NewValue);

            AttachViewModel(DataContext);
            DataContextChanged += (_, e) => AttachViewModel(e.NewValue);
        }

        // Unsubscribes the previous VM's handler before attaching the new one - the
        // constructor's initial DataContext check and DataContextChanged used to subscribe
        // independently with no unsubscribe, so a DataContext reassignment (or the initial
        // subscription plus a later change) left the old VM's PropertyChanged handler live,
        // each accumulating another UpdateMemoryBar() call per MemoryBarWidth change.
        private void AttachViewModel(object? dataContext)
        {
            if (_subscribedVm != null)
            {
                _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _subscribedVm = dataContext as ViewModels.MemoryOptimizerViewModel;

            if (_subscribedVm != null)
            {
                _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
                UpdateMemoryBar();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.MemoryOptimizerViewModel.MemoryBarWidth))
            {
                UpdateMemoryBar();
            }
        }

        private void NotifyPageActive(bool active)
        {
            if (DataContext is ViewModels.MemoryOptimizerViewModel vm)
                vm.SetPageActive(active);
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
