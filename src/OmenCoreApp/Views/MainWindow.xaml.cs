using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OmenCore.Controls;
using OmenCore.ViewModels;

namespace OmenCore.Views
{
    public partial class MainWindow : Window
    {
        private bool _forceClose = false; // Flag for actual shutdown vs hide-to-tray
        
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            SystemParameters.StaticPropertyChanged += SystemParametersOnStaticPropertyChanged;
            
            // Apply Stay on Top setting from config
            Topmost = App.Configuration.Config.StayOnTop;
            
            // Listen for LogBuffer changes to auto-scroll the system log
            if (viewModel is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += OnViewModelPropertyChanged;
            }
        }
        
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.LogBuffer))
            {
                // Auto-scroll the system log to the latest entry
                Dispatcher.InvokeAsync(() =>
                {
                    if (SystemLogScrollViewer != null)
                    {
                        SystemLogScrollViewer.ScrollToEnd();
                    }
                });
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            (DataContext as MainViewModel)?.DiscoverCorsairCommand.Execute(null);
            UpdateMaximizedBounds();
            
            // Initialize global hotkeys
            var windowHandle = new WindowInteropHelper(this).Handle;
            (DataContext as MainViewModel)?.InitializeHotkeys(windowHandle);
            
            // CRITICAL FIX: Create and inject Dashboard to display monitoring data
            // Using Dispatcher to ensure this happens after window layout is complete
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Ensure MainWindow's DataContext is properly set
                    var mainViewModel = this.DataContext as MainViewModel;
                    if (mainViewModel == null)
                    {
                        App.Logging.Error("[MainWindow] DataContext is not MainViewModel!");
                        return;
                    }
                    
                    // Create the Dashboard with the MainViewModel
                    var dashboard = new HardwareMonitoringDashboard
                    {
                        DataContext = mainViewModel
                    };
                    
                    // Inject it into the Monitoring tab
                    if (this.MonitoringTabItem != null)
                    {
                        this.MonitoringTabItem.Content = dashboard;
                        App.Logging.Info("[MainWindow] Created HardwareMonitoringDashboard with MainViewModel and injected into Monitoring tab");
                        
                        // Force the Dashboard to apply its template and render
                        dashboard.ApplyTemplate();
                        dashboard.UpdateLayout();
                        App.Logging.Info("[MainWindow] Called ApplyTemplate() and UpdateLayout() on Dashboard");
                    }
                    else
                    {
                        App.Logging.Error("[MainWindow] MonitoringTabItem not found!");
                    }
                    
                    // Select the General tab to make it visible and force rendering
                    if (this.TabControlMain != null)
                    {
                        this.TabControlMain.SelectedIndex = 0;
                        this.TabControlMain.UpdateLayout();
                        App.Logging.Info("[MainWindow] Set TabControl.SelectedIndex = 0 (General tab) and called UpdateLayout()");
                    }
                }
                catch (Exception ex)
                {
                    App.Logging.Error($"[MainWindow] ERROR creating Dashboard: {ex.Message}\n{ex.StackTrace}");
                }
            }, System.Windows.Threading.DispatcherPriority.Normal);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _ = sender;
            
            // Check if we should minimize to tray instead of closing
            bool minimizeToTray = App.Configuration.Config.Monitoring?.MinimizeToTrayOnClose ?? true;
            
            if (minimizeToTray && !_forceClose)
            {
                // Cancel the close and hide to tray instead
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                App.Logging.Debug("Window hidden to tray (close cancelled)");
                return;
            }
            
            // Actual close - clean up
            SystemParameters.StaticPropertyChanged -= SystemParametersOnStaticPropertyChanged;
            (DataContext as MainViewModel)?.Dispose();
        }

        private void SystemParametersOnStaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea))
            {
                UpdateMaximizedBounds();
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateMaximizeButtonGlyph();
            if (WindowState == WindowState.Maximized)
            {
                UpdateMaximizedBounds();
            }
            // Note: We no longer hide to tray on minimize - that was causing Issue #20
            // Minimize now properly minimizes to taskbar
        }

        private void UpdateMaximizeButtonGlyph()
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        private void UpdateMaximizedBounds()
        {
            var workArea = SystemParameters.WorkArea;
            MaxHeight = workArea.Height + 12;
            MaxWidth = workArea.Width + 12;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks on window control buttons so minimize/maximize/close always work.
            if (IsFromWindowControl(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // Ignore drag failures caused by transient input state races.
                }
            }
        }

        private static bool IsFromWindowControl(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is Button)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            // Minimize to taskbar (normal Windows behavior)
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            
            // Check if we should minimize to tray or actually close
            bool minimizeToTray = App.Configuration.Config.Monitoring?.MinimizeToTrayOnClose ?? true;
            
            if (minimizeToTray)
            {
                // Hide to tray on close button
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                // Actually close the application
                App.Current?.Shutdown();
            }
        }
    }
}
