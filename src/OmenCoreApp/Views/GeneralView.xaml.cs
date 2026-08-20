using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OmenCore.Views
{
    /// <summary>
    /// Simplified General view with paired Performance + Fan profiles.
    /// Each profile combines a performance mode with a matching fan configuration.
    /// </summary>
    public partial class GeneralView : UserControl
    {
        public GeneralView()
        {
            InitializeComponent();
            Loaded += GeneralView_Loaded;
            Unloaded += GeneralView_Unloaded;
            IsVisibleChanged += GeneralView_IsVisibleChanged;
            DataContextChanged += GeneralView_DataContextChanged;
        }

        private void GeneralView_Loaded(object sender, RoutedEventArgs e)
        {
            SyncTelemetryProjectionState();
        }

        private void GeneralView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.GeneralViewModel vm)
            {
                vm.SetTelemetryProjectionEnabled(false);
            }
        }

        private void GeneralView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SyncTelemetryProjectionState();
        }

        private void GeneralView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SyncTelemetryProjectionState();
        }

        private void SyncTelemetryProjectionState()
        {
            if (DataContext is ViewModels.GeneralViewModel vm)
            {
                vm.SetTelemetryProjectionEnabled(IsVisible);
            }
        }

        private void Profile_Performance_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.GeneralViewModel vm)
            {
                vm.ApplyPerformanceProfile();
            }
        }

        private void Profile_Balanced_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.GeneralViewModel vm)
            {
                vm.ApplyBalancedProfile();
            }
        }

        private void Profile_Quiet_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.GeneralViewModel vm)
            {
                vm.ApplyQuietProfile();
            }
        }

        private void Profile_Custom_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.GeneralViewModel vm)
            {
                vm.ApplyCustomProfile();
            }
        }

        private void FanQuickMode_Max_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.GeneralViewModel vm)
            {
                vm.ApplyFanQuickMax();
            }
        }

        private void FanQuickMode_Auto_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.GeneralViewModel vm)
            {
                vm.ApplyFanQuickAuto();
            }
        }

        private void FanQuickMode_Custom_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.GeneralViewModel vm)
            {
                vm.ApplyFanQuickCustom();
            }
        }

        // Keyboard counterparts for the profile/fan-mode cards above. These are plain Borders
        // with a mouse handler, not real Button/RadioButton controls, so without this Tab could
        // reach them (once Focusable="True" is set in XAML) but Enter/Space would do nothing -
        // a keyboard-only user could see the card focused and never be able to activate it.
        // ActivateOnEnterOrSpace centralizes the "which key counts as activation" decision so
        // each handler below is one line, matching the shape of the mouse handlers next to them.
        private static void ActivateOnEnterOrSpace(KeyEventArgs e, Action activate)
        {
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            activate();
            e.Handled = true;
        }

        private void Profile_Performance_KeyDown(object sender, KeyEventArgs e) =>
            ActivateOnEnterOrSpace(e, () => (DataContext as ViewModels.GeneralViewModel)?.ApplyPerformanceProfile());

        private void Profile_Balanced_KeyDown(object sender, KeyEventArgs e) =>
            ActivateOnEnterOrSpace(e, () => (DataContext as ViewModels.GeneralViewModel)?.ApplyBalancedProfile());

        private void Profile_Quiet_KeyDown(object sender, KeyEventArgs e) =>
            ActivateOnEnterOrSpace(e, () => (DataContext as ViewModels.GeneralViewModel)?.ApplyQuietProfile());

        private void Profile_Custom_KeyDown(object sender, KeyEventArgs e) =>
            ActivateOnEnterOrSpace(e, () => (DataContext as ViewModels.GeneralViewModel)?.ApplyCustomProfile());

        private void FanQuickMode_Max_KeyDown(object sender, KeyEventArgs e) =>
            ActivateOnEnterOrSpace(e, () => (DataContext as ViewModels.GeneralViewModel)?.ApplyFanQuickMax());

        private void FanQuickMode_Auto_KeyDown(object sender, KeyEventArgs e) =>
            ActivateOnEnterOrSpace(e, () => (DataContext as ViewModels.GeneralViewModel)?.ApplyFanQuickAuto());

        private void FanQuickMode_Custom_KeyDown(object sender, KeyEventArgs e) =>
            ActivateOnEnterOrSpace(e, () => (DataContext as ViewModels.GeneralViewModel)?.ApplyFanQuickCustom());
    }
}
