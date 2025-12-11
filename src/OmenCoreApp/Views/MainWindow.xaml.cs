using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using OmenCore.ViewModels;

namespace OmenCore.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            SystemParameters.StaticPropertyChanged += SystemParametersOnStaticPropertyChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            (DataContext as MainViewModel)?.DiscoverCorsairCommand.Execute(null);
            UpdateMaximizedBounds();
            
            // Initialize global hotkeys
            var windowHandle = new WindowInteropHelper(this).Handle;
            (DataContext as MainViewModel)?.InitializeHotkeys(windowHandle);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
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
            else if (WindowState == WindowState.Minimized)
            {
                // Hide to tray instead of showing in taskbar
                Hide();
            }
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
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            // Minimize to system tray instead of taskbar
            Hide();
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide to tray on close button (user can exit from tray menu)
            Hide();
        }
    }
}
