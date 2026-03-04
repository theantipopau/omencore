using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Views
{
    public partial class OnboardingWindow : Window
    {
        private int _currentPage = 1;
        private readonly AppConfig? _config;
        private readonly ConfigurationService? _configService;

        public OnboardingWindow()
        {
            InitializeComponent();
        }

        public OnboardingWindow(AppConfig config, ConfigurationService configService) : this()
        {
            _config = config;
            _configService = configService;
            PopulateHardwarePage();
        }

        private void PopulateHardwarePage()
        {
            // Fan backend
            var fb = _config?.EcDevicePath;
            if (!string.IsNullOrWhiteSpace(fb))
            {
                FanBackendText.Text = "EC (Embedded Controller)";
                FanBackendDot.Fill = (Brush)FindResource("SuccessBrush");
            }
            else
            {
                FanBackendText.Text = "WMI / BIOS";
                FanBackendDot.Fill = (Brush)FindResource("WarningBrush");
            }

            MonitoringText.Text = "Hardware monitoring active";
            MonitoringDot.Fill = (Brush)FindResource("SuccessBrush");
            DriverText.Text = "Check Settings → Status for driver details";
            DriverDot.Fill = (Brush)FindResource("TextTertiaryBrush");
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void NextButton_Click(object sender, RoutedEventArgs e) => GoToPage(_currentPage + 1);
        private void BackButton_Click(object sender, RoutedEventArgs e) => GoToPage(_currentPage - 1);

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            if (_config != null)
            {
                _config.FirstRunCompleted = true;
                _configService?.Save(_config);
            }
            Close();
        }

        private void GoToPage(int page)
        {
            _currentPage = page;
            Page1.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
            Page2.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
            Page3.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;

            BackButton.Visibility = page > 1 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Visibility = page < 3 ? Visibility.Visible : Visibility.Collapsed;
            FinishButton.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;

            var accent = (Brush)TryFindResource("AccentBrush") ?? Brushes.CornflowerBlue;
            var inactive = (Brush)TryFindResource("SurfaceHighlightBrush") ?? Brushes.Gray;
            Dot1.Fill = page == 1 ? accent : inactive;
            Dot2.Fill = page == 2 ? accent : inactive;
            Dot3.Fill = page == 3 ? accent : inactive;
        }
    }
}
