using System.Windows.Controls;

namespace OmenCore.Controls
{
    /// <summary>
    /// Loading overlay with animated spinner.
    /// Displays when IsLoading is true in the DataContext.
    /// </summary>
    public partial class LoadingOverlay : UserControl
    {
        public LoadingOverlay()
        {
            InitializeComponent();
        }
    }
}
