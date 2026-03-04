using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OmenCore.ViewModels;

namespace OmenCore.Views
{
    public partial class FanControlView : UserControl
    {
        public FanControlView()
        {
            InitializeComponent();
        }

        private void PresetCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string name && DataContext is FanControlViewModel vm)
            {
                if (name == "Custom")
                    vm.ClearHoveredPreset(); // custom curve is already visible
                else
                    vm.SetHoveredPreset(vm.FanPresets.FirstOrDefault(p => p.Name == name));
            }
        }

        private void PresetCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is FanControlViewModel vm)
                vm.ClearHoveredPreset();
        }
    }
}
