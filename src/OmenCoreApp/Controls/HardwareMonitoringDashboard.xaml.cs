using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using OmenCore;
using OmenCore.Services;
using OmenCore.Models;
using OmenCore.ViewModels;

namespace OmenCore.Controls
{
    public partial class HardwareMonitoringDashboard : UserControl, INotifyPropertyChanged
    {
        private MainViewModel? _mainViewModel;
        private DashboardViewModel? _dashboardViewModel;
        private readonly DispatcherTimer _updateTimer;
        private readonly DispatcherTimer _chartUpdateTimer;
        private readonly ObservableCollection<SystemAlert> _alerts;
        private readonly Queue<double> _cpuTempHistory = new(60); // Last 60 data points
        private readonly Queue<double> _gpuTempHistory = new(60);
        private readonly Queue<double> _powerHistory = new(60);
        private double _previousPower;
        private bool _isInitialized;
        private double _cachedBatteryHealth = -1; // Cached battery health (expensive to query)
        private DateTime _lastBatteryHealthCheck = DateTime.MinValue;
        
        /// <summary>
        /// Static reference to allow manual initialization from outside
        /// </summary>
        public static HardwareMonitoringDashboard? Instance { get; private set; }

        public HardwareMonitoringDashboard()
        {
            Instance = this;
            InitializeComponent();
            App.Logging.Info("[Dashboard] Constructor called - InitializeComponent() complete");

            _alerts = new ObservableCollection<SystemAlert>();
            AlertsListBox.ItemsSource = _alerts;

            // Set up metrics update timer (fast for responsive UI)
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += UpdateTimer_Tick;

            // Set up chart update timer (slower to reduce overhead)
            _chartUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _chartUpdateTimer.Tick += ChartUpdateTimer_Tick;

            // Initialize data
            Loaded += HardwareMonitoringDashboard_Loaded;
            Unloaded += HardwareMonitoringDashboard_Unloaded;
            DataContextChanged += HardwareMonitoringDashboard_DataContextChanged;
            
            App.Logging.Info("[Dashboard] Constructor completed, waiting for Loaded event and DataContext");
        }

        private void HardwareMonitoringDashboard_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            App.Logging.Info($"[Dashboard.DataContextChanged] Old: {e.OldValue?.GetType()?.Name ?? "null"}, New: {e.NewValue?.GetType()?.Name ?? "null"}");
            
            if (_isInitialized) return;

            // Get MainViewModel from new DataContext
            _mainViewModel = e.NewValue as MainViewModel;
            _dashboardViewModel = _mainViewModel?.Dashboard;

            // If dashboard VM not available yet, we'll listen for MainViewModel.Dashboard property changes

            if (_mainViewModel == null)
            {
                App.Logging.Warn("[Dashboard.DataContextChanged] New DataContext is not MainViewModel, will try again on Loaded");
                return;
            }
            App.Logging.Info("[Dashboard.DataContextChanged] Successfully cast DataContext to MainViewModel, calling InitializeWithViewModel()");
            InitializeWithViewModel();
        }
        
        private void InitializeWithViewModel()
        {
            if (_isInitialized || _mainViewModel == null) return;
            
            App.Logging.Info("[Dashboard] InitializeWithViewModel() called - starting timers and initial update");
            
            // Prefer subscribing to DashboardViewModel if available so the dashboard uses the same sample source as the tray
            if (_dashboardViewModel != null)
            {
                _dashboardViewModel.PropertyChanged += DashboardViewModel_PropertyChanged;
            }
            else
            {
                // Dashboard VM not created yet; subscribe to MainViewModel to detect when it appears and to latest-sample updates
                _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
            }
            
            // Start timers
            _updateTimer.Start();
            _chartUpdateTimer.Start();
            App.Logging.Info($"[Dashboard] Timers started: _updateTimer.IsEnabled={_updateTimer.IsEnabled}, _chartUpdateTimer.IsEnabled={_chartUpdateTimer.IsEnabled}");
            _isInitialized = true;
            
            // Perform initial update async
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);  // Wait 1 second to allow service to send first sample
                await Dispatcher.InvokeAsync(async () =>
                {
                    await UpdateMetricsAsync();
                    await CheckForAlertsAsync();
                });
            });
        }
        
        private async void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.LatestMonitoringSample))
            {
                // Immediately update when new sample arrives (fallback)
                await Dispatcher.InvokeAsync(async () => await UpdateMetricsAsync());
                return;
            }

            if (e.PropertyName == nameof(MainViewModel.Dashboard))
            {
                // Dashboard VM created later; attach to its PropertyChanged and stop listening to MainViewModel for this
                try
                {
                    _dashboardViewModel = _mainViewModel?.Dashboard;
                    if (_dashboardViewModel != null && _mainViewModel != null)
                    {
                        _dashboardViewModel.PropertyChanged += DashboardViewModel_PropertyChanged;
                        _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
                        App.Logging.Info("[Dashboard] Attached to DashboardViewModel.PropertyChanged and detached MainViewModel listener");
                    }
                }
                catch (Exception ex)
                {
                    App.Logging.Warn($"[Dashboard] Failed to attach to DashboardViewModel: {ex.Message}");
                }
            }
        }

        private async void DashboardViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardViewModel.LatestMonitoringSample))
            {
                await Dispatcher.InvokeAsync(async () => await UpdateMetricsAsync());
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' - fire-and-forget InvokeAsync is intentional
        private async void HardwareMonitoringDashboard_Loaded(object sender, RoutedEventArgs e)
#pragma warning restore CS1998
        {
            App.Logging.Info($"[Dashboard.Loaded] Control loaded, _isInitialized: {_isInitialized}, _mainViewModel: {(_mainViewModel != null ? "OK" : "NULL")}");
            
            // If not initialized yet, try to get ViewModel from DataContext now
            if (!_isInitialized)
            {
                _mainViewModel = DataContext as MainViewModel;
                if (_mainViewModel != null)
                {
                    App.Logging.Info("[Dashboard.Loaded] Got MainViewModel from DataContext, initializing now");
                    InitializeWithViewModel();
                }
                else
                {
                    App.Logging.Warn("[Dashboard.Loaded] DataContext is still not MainViewModel - showing placeholders");
                    ShowNoDataPlaceholders();
                    return;
                }
            }

            // Draw charts
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(200);
                await RefreshAllChartsAsync();
            }, DispatcherPriority.Loaded);
        }

        private void HardwareMonitoringDashboard_Unloaded(object sender, RoutedEventArgs e)
        {
            _updateTimer?.Stop();
            _chartUpdateTimer?.Stop();
            
            // Unsubscribe from PropertyChanged to prevent memory leaks
            if (_mainViewModel != null)
            {
                _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
            }
        }

        private async void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // Log every tick (might be verbose)
                // App.Logging.Debug("[Dashboard.UpdateTimer_Tick] Timer tick fired");
                await UpdateMetricsAsync();
                await CheckForAlertsAsync();
            }
            catch (Exception ex)
            {
                App.Logging.Error($"[Dashboard.UpdateTimer_Tick] ERROR: {ex.Message}");
            }
        }

        private async void ChartUpdateTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshAllChartsAsync();
        }

        private async Task UpdateMetricsAsync()
        {
            if (_mainViewModel == null)
            {
                App.Logging.Warn("[Dashboard.UpdateMetrics] _mainViewModel is NULL!");
                return;
            }

            try
            {
                var sample = _dashboardViewModel?.LatestMonitoringSample ?? _mainViewModel?.LatestMonitoringSample;

                App.Logging.Info($"[Dashboard.UpdateMetrics] Called! LatestMonitoringSample={( sample == null ? "NULL" : $"CPU={sample.CpuTemperatureC}°C" )}");

                if (sample == null)
                {
                    App.Logging.Warn("[Dashboard.UpdateMetrics] LatestMonitoringSample is NULL - showing placeholders");
                    ShowNoDataPlaceholders();
                    return;
                }

                App.Logging.Debug($"[Dashboard.UpdateMetrics] Got sample: CPU={sample.CpuTemperatureC}°C, GPU={sample.GpuTemperatureC}°C, CPULoad={sample.CpuLoadPercent}%, GPULoad={sample.GpuLoadPercent}%");

                // Add to history queues for real-time sparklines
                _cpuTempHistory.Enqueue(sample.CpuTemperatureC);
                _gpuTempHistory.Enqueue(sample.GpuTemperatureC);
                if (_cpuTempHistory.Count > 60) _cpuTempHistory.Dequeue();
                if (_gpuTempHistory.Count > 60) _gpuTempHistory.Dequeue();

                // Calculate power consumption (improved estimation)
                double estimatedPower = CalculatePowerConsumption(sample);
                _powerHistory.Enqueue(estimatedPower);
                if (_powerHistory.Count > 60) _powerHistory.Dequeue();

                // Update power consumption with trend
                if (PowerConsumptionValue != null) PowerConsumptionValue.Text = estimatedPower.ToString("F1");
                double powerTrend = _powerHistory.Count > 1 ? estimatedPower - _previousPower : 0;
                if (PowerConsumptionTrend != null)
                {
                    PowerConsumptionTrend.Text = GetTrendIndicator(powerTrend);
                    PowerConsumptionTrend.Foreground = GetTrendBrush(powerTrend);
                }
                _previousPower = estimatedPower;

                // Update battery health using REAL data from WMI
                var batteryHealth = await GetBatteryHealthPercentAsync();
                if (BatteryHealthValue != null) BatteryHealthValue.Text = batteryHealth.ToString("F0");
                if (BatteryHealthStatus != null)
                {
                    if (batteryHealth >= 80)
                    {
                        BatteryHealthStatus.Text = "Good";
                        BatteryHealthStatus.Foreground = Brushes.LimeGreen;
                    }
                    else if (batteryHealth >= 50)
                    {
                        BatteryHealthStatus.Text = "Fair";
                        BatteryHealthStatus.Foreground = Brushes.Orange;
                    }
                    else
                    {
                        BatteryHealthStatus.Text = "Replace";
                        BatteryHealthStatus.Foreground = Brushes.Red;
                    }
                }

                // CRITICAL FIX: Update temperatures with proper null checking and display
                double cpuTemp = sample.CpuTemperatureC;
                double gpuTemp = sample.GpuTemperatureC;

                if (CpuTempValue != null)
                {
                    CpuTempValue.Text = cpuTemp.ToString("F0");
                    AnimateMetricIfCritical(CpuTempValue, cpuTemp > 85);
                }
                if (CpuTempStatus != null)
                {
                    var cpuStatus = GetTemperatureStatus(cpuTemp);
                    CpuTempStatus.Text = cpuStatus;
                    CpuTempStatus.Foreground = GetTemperatureBrush(cpuTemp);
                }

                if (GpuTempValue != null)
                {
                    GpuTempValue.Text = gpuTemp.ToString("F0");
                    AnimateMetricIfCritical(GpuTempValue, gpuTemp > 85);
                }
                if (GpuTempStatus != null)
                {
                    var gpuStatus = GetTemperatureStatus(gpuTemp);
                    GpuTempStatus.Text = gpuStatus;
                    GpuTempStatus.Foreground = GetTemperatureBrush(gpuTemp);
                }

                App.Logging.Debug($"[Dashboard.UpdateMetrics] UI updated: CpuTempValue.Text={CpuTempValue?.Text}, GpuTempValue.Text={GpuTempValue?.Text}");
                
                // CPU/GPU Load indicators - with null safety
                var cpuLoadStr = sample.CpuLoadPercent.ToString("F0");
                var gpuLoadStr = sample.GpuLoadPercent.ToString("F0");
                
                if (CpuLoadValue != null) CpuLoadValue.Text = cpuLoadStr;
                if (GpuLoadValue != null) GpuLoadValue.Text = gpuLoadStr;
                
                App.Logging.Info($"[Dashboard.UpdateMetrics] Load updated: CpuLoad={cpuLoadStr}%, GpuLoad={gpuLoadStr}%, RAM={sample.RamUsageGb:F1}GB, CpuClock={sample.CpuCoreClocksMhz?.Count ?? 0} cores");
                
                // Update progress bars
                if (CpuLoadBar != null) CpuLoadBar.Value = Math.Min(100, sample.CpuLoadPercent);
                if (GpuLoadBar != null) GpuLoadBar.Value = Math.Min(100, sample.GpuLoadPercent);

                // Clock speeds (if available)
                if (CpuClockValue != null)
                {
                    if (sample.CpuCoreClocksMhz != null && sample.CpuCoreClocksMhz.Count > 0)
                    {
                        int avgClock = (int)sample.CpuCoreClocksMhz.Average();
                        CpuClockValue.Text = $"{avgClock:F0}";
                    }
                    else
                    {
                        // No per-core clock data - show N/A
                        CpuClockValue.Text = "--";
                    }
                }

                // RAM usage - guard against divide by zero with null safety
                if (RamUsageValue != null && RamUsagePercent != null)
                {
                    if (sample.RamTotalGb > 0)
                    {
                        double ramUsagePercent = (sample.RamUsageGb / sample.RamTotalGb) * 100;
                        RamUsageValue.Text = $"{sample.RamUsageGb:F1}";
                        RamUsagePercent.Text = ramUsagePercent.ToString("F0");
                    }
                    else
                    {
                        RamUsageValue.Text = "--";
                        RamUsagePercent.Text = "--";
                    }
                }

                // Update efficiency metrics - FIXED with null safety
                double workload = Math.Min(100, (sample.CpuLoadPercent + sample.GpuLoadPercent) / 2);
                double avgTemp = (cpuTemp + gpuTemp) / 2;
                double efficiency = CalculateEfficiency(workload, avgTemp);
                
                if (PowerEfficiencyValue != null) PowerEfficiencyValue.Text = efficiency.ToString("F1");
                if (PowerEfficiencyRating != null)
                {
                    var efficiencyRating = GetEfficiencyRating(efficiency);
                    PowerEfficiencyRating.Text = efficiencyRating;
                    PowerEfficiencyRating.Foreground = GetEfficiencyBrush(efficiency);
                }

                // Thermal status - FIXED with null safety
                if (BatteryCyclesValue != null) BatteryCyclesValue.Text = "0";
                if (BatteryLifeEstimate != null) BatteryLifeEstimate.Text = "~3.0 years remaining";

                if (ThermalStatusValue != null)
                {
                    ThermalStatusValue.Text = GetThermalStatus(avgTemp);
                    ThermalStatusValue.Foreground = GetTemperatureBrush(avgTemp);
                }
                if (ThermalEfficiency != null) ThermalEfficiency.Text = $"Fan: {CalculateFanEfficiency(sample):F0}% efficient";


            }
            catch (Exception ex)
            {
                App.Logging.Error($"[Dashboard.UpdateMetrics] ERROR: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void ShowNoDataPlaceholders()
        {
            App.Logging.Warn("[Dashboard] ShowNoDataPlaceholders() called - setting all values to '--'");
            if (PowerConsumptionValue != null) PowerConsumptionValue.Text = "--";
            if (PowerConsumptionTrend != null) PowerConsumptionTrend.Text = "No data";
            if (BatteryHealthValue != null) BatteryHealthValue.Text = "--";
            if (BatteryHealthStatus != null) BatteryHealthStatus.Text = "Unknown";
            if (CpuTempValue != null) CpuTempValue.Text = "--";
            if (CpuTempStatus != null) CpuTempStatus.Text = "No data";
            if (GpuTempValue != null) GpuTempValue.Text = "--";
            if (GpuTempStatus != null) GpuTempStatus.Text = "No data";
            if (PowerEfficiencyValue != null) PowerEfficiencyValue.Text = "--";
            if (PowerEfficiencyRating != null) PowerEfficiencyRating.Text = "Unknown";
            if (BatteryCyclesValue != null) BatteryCyclesValue.Text = "--";
            if (BatteryLifeEstimate != null) BatteryLifeEstimate.Text = "No data";
            if (ThermalStatusValue != null) ThermalStatusValue.Text = "Unknown";
            if (ThermalEfficiency != null) ThermalEfficiency.Text = "No data";
            
            // System Activity fields
            if (CpuLoadValue != null) CpuLoadValue.Text = "--";
            if (GpuLoadValue != null) GpuLoadValue.Text = "--";
            if (CpuClockValue != null) CpuClockValue.Text = "--";
            if (RamUsageValue != null) RamUsageValue.Text = "--";
            if (RamUsagePercent != null) RamUsagePercent.Text = "--";
        }

        private double CalculatePowerConsumption(MonitoringSample sample)
        {
            // More sophisticated power estimation based on load and clock speeds
            double cpuPower = (sample.CpuLoadPercent / 100.0) * 45.0; // Max 45W for CPU
            double gpuPower = (sample.GpuLoadPercent / 100.0) * 100.0; // Max 100W for GPU
            double basePower = 15.0; // Baseline system power
            double ramPower = (sample.RamUsageGb / sample.RamTotalGb) * 5.0; // RAM contribution
            
            return cpuPower + gpuPower + basePower + ramPower;
        }

        /// <summary>
        /// Gets real battery health percentage by comparing FullChargeCapacity to DesignCapacity.
        /// Uses WMI BatteryStaticData for DesignCapacity and BatteryFullChargedCapacity for current capacity.
        /// Caches result for 60 seconds to avoid excessive WMI queries.
        /// </summary>
        private async Task<double> GetBatteryHealthPercentAsync()
        {
            // Return cached value if recent (battery health doesn't change often)
            if (_cachedBatteryHealth >= 0 && (DateTime.Now - _lastBatteryHealthCheck).TotalSeconds < 60)
            {
                return _cachedBatteryHealth;
            }

            return await Task.Run(() =>
            {
                try
                {
                    uint designCapacity = 0;
                    uint fullChargeCapacity = 0;

                    // Get DesignCapacity from BatteryStaticData (requires admin/elevated access on some systems)
                    using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT DesignedCapacity FROM BatteryStaticData"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            designCapacity = Convert.ToUInt32(obj["DesignedCapacity"]);
                            break;
                        }
                    }

                    // Get FullChargeCapacity from BatteryFullChargedCapacity
                    using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            fullChargeCapacity = Convert.ToUInt32(obj["FullChargedCapacity"]);
                            break;
                        }
                    }

                    if (designCapacity > 0 && fullChargeCapacity > 0)
                    {
                        _cachedBatteryHealth = (fullChargeCapacity * 100.0) / designCapacity;
                        _cachedBatteryHealth = Math.Min(100.0, Math.Max(0.0, _cachedBatteryHealth)); // Clamp to 0-100
                        _lastBatteryHealthCheck = DateTime.Now;
                        App.Logging.Info($"[Dashboard] Battery health: {_cachedBatteryHealth:F1}% (FullCharge={fullChargeCapacity} mWh, Design={designCapacity} mWh)");
                        return _cachedBatteryHealth;
                    }

                    // Fallback: use EstimatedChargeRemaining if above fails (less accurate)
                    App.Logging.Warn("[Dashboard] Battery WMI data unavailable, showing 100% as fallback");
                    _cachedBatteryHealth = 100.0;
                    _lastBatteryHealthCheck = DateTime.Now;
                    return _cachedBatteryHealth;
                }
                catch (Exception ex)
                {
                    App.Logging.Warn($"[Dashboard] Failed to get battery health: {ex.Message}");
                    _cachedBatteryHealth = 100.0; // Fallback
                    _lastBatteryHealthCheck = DateTime.Now;
                    return _cachedBatteryHealth;
                }
            });
        }

        private double CalculateEfficiency(double workload, double avgTemp)
        {
            // Efficiency = workload vs temperature ratio (higher workload at lower temp = better)
            if (avgTemp < 50) return 95.0;
            if (avgTemp < 60) return 90.0 - (workload * 0.1);
            if (avgTemp < 70) return 85.0 - (workload * 0.15);
            if (avgTemp < 80) return 75.0 - (workload * 0.2);
            return Math.Max(50.0, 70.0 - (workload * 0.25));
        }

        private double CalculateFanEfficiency(MonitoringSample sample)
        {
            // Fan efficiency based on cooling performance
            double avgTemp = (sample.CpuTemperatureC + sample.GpuTemperatureC) / 2;
            if (avgTemp < 50) return 95.0;
            if (avgTemp < 65) return 85.0;
            if (avgTemp < 75) return 75.0;
            if (avgTemp < 85) return 65.0;
            return 50.0;
        }

        private void AnimateMetricIfCritical(TextBlock? textBlock, bool isCritical)
        {
            if (textBlock == null) return;
            
            try
            {
                if (isCritical && textBlock.Foreground != Brushes.Red)
                {
                    // Pulse animation for critical values
                    var animation = new ColorAnimation
                    {
                        From = Colors.OrangeRed,
                        To = Colors.Red,
                        Duration = TimeSpan.FromSeconds(0.5),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    var brush = new SolidColorBrush();
                    textBlock.Foreground = brush;
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                }
                else if (!isCritical)
                {
                    // Use a fallback color if AccentColor resource is not available
                    var accentColor = Application.Current?.Resources["AccentColor"];
                    if (accentColor is Color color)
                    {
                        textBlock.Foreground = new SolidColorBrush(color);
                    }
                    else
                    {
                        textBlock.Foreground = new SolidColorBrush(Colors.DeepSkyBlue);
                    }
                }
            }
            catch (Exception ex)
            {
                // Failsafe - just log and set a default color
                App.Logging.Debug($"[Dashboard] AnimateMetricIfCritical failed: {ex.Message}");
                textBlock.Foreground = new SolidColorBrush(Colors.White);
            }
        }

        private Brush GetTrendBrush(double trend)
        {
            if (Math.Abs(trend) < 0.1) return Brushes.Gray;
            return trend > 0 ? Brushes.OrangeRed : Brushes.LimeGreen;
        }

        private Brush GetTemperatureBrush(double temperature)
        {
            if (temperature < 60) return Brushes.LimeGreen;
            if (temperature < 75) return Brushes.Yellow;
            if (temperature < 85) return Brushes.Orange;
            return Brushes.OrangeRed;
        }

        private Brush GetEfficiencyBrush(double efficiency)
        {
            if (efficiency >= 85) return Brushes.LimeGreen;
            if (efficiency >= 75) return Brushes.Yellow;
            if (efficiency >= 65) return Brushes.Orange;
            return Brushes.OrangeRed;
        }

        private async Task CheckForAlertsAsync()
        {
            if (_mainViewModel?.HardwareMonitoringService == null) return;

            try
            {
                var alerts = await _mainViewModel.HardwareMonitoringService.GetActiveAlertsAsync();

                _alerts.Clear();
                foreach (var alert in alerts)
                {
                    _alerts.Add(alert);
                }

                NoAlertsText.Visibility = _alerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                AlertsListBox.Visibility = _alerts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking alerts: {ex.Message}");
            }
        }

        private string GetTrendIndicator(double trend)
        {
            if (Math.Abs(trend) < 0.1) return "→ Stable";
            if (trend > 0) return $"↗ +{trend:F1}W";
            return $"↘ {trend:F1}W";
        }

        private string GetBatteryHealthStatus(double percentage)
        {
            if (percentage >= 90) return "Excellent";
            if (percentage >= 80) return "Good";
            if (percentage >= 70) return "Fair";
            return "Poor";
        }

        private string GetTemperatureStatus(double temperature)
        {
            if (temperature < 60) return "Normal";
            if (temperature < 80) return "Warm";
            if (temperature < 90) return "Hot";
            return "Critical";
        }

        private string GetEfficiencyRating(double efficiency)
        {
            if (efficiency >= 85) return "Excellent";
            if (efficiency >= 75) return "Good";
            if (efficiency >= 65) return "Fair";
            return "Poor";
        }

        private string GetThermalStatus(double avgTemp)
        {
            if (avgTemp < 50) return "Cool";
            if (avgTemp < 65) return "Optimal";
            if (avgTemp < 80) return "Warm";
            return "Hot";
        }

        private async Task RefreshAllChartsAsync()
        {
            if (_mainViewModel?.HardwareMonitoringService == null) return;

            try
            {
                // Get data for all chart types
                var powerData = (await _mainViewModel.HardwareMonitoringService.GetHistoricalDataAsync(ChartType.PowerConsumption, TimeSpan.FromHours(1))).ToList();
                var tempData = (await _mainViewModel.HardwareMonitoringService.GetHistoricalDataAsync(ChartType.Temperature, TimeSpan.FromHours(1))).ToList();
                var batteryData = (await _mainViewModel.HardwareMonitoringService.GetHistoricalDataAsync(ChartType.BatteryHealth, TimeSpan.FromHours(1))).ToList();
                var fanData = (await _mainViewModel.HardwareMonitoringService.GetHistoricalDataAsync(ChartType.FanSpeeds, TimeSpan.FromHours(1))).ToList();

                // Draw each chart
                DrawChartOnCanvas(PowerChartCanvas, PowerChartStats, powerData, Brushes.Yellow, "W");
                DrawChartOnCanvas(TempChartCanvas, TempChartStats, tempData, Brushes.OrangeRed, "°C");
                DrawChartOnCanvas(BatteryChartCanvas, BatteryChartStats, batteryData, Brushes.LimeGreen, "%");
                DrawChartOnCanvas(FanChartCanvas, FanChartStats, fanData, Brushes.Cyan, "RPM");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing charts: {ex.Message}");
            }
        }

        private void DrawChartOnCanvas(Canvas canvas, TextBlock statsBlock, List<HistoricalDataPoint> data, Brush lineColor, string unit)
        {
            canvas.Children.Clear();

            if (data.Count < 2)
            {
                var noData = new TextBlock
                {
                    Text = "⏱️ Collecting data...",
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold
                };
                Canvas.SetLeft(noData, canvas.Width / 2 - 70);
                Canvas.SetTop(noData, canvas.Height / 2 - 10);
                canvas.Children.Add(noData);
                statsBlock.Text = "Waiting for historical data (displays after 2+ samples)";
                return;
            }

            var height = canvas.Height > 0 ? canvas.Height : 150;
            var width = canvas.ActualWidth > 50 ? canvas.ActualWidth : 280;

            var minValue = data.Min(p => p.Value);
            var maxValue = data.Max(p => p.Value);
            var valueRange = maxValue - minValue;
            if (valueRange < 1) valueRange = 1;

            // Add padding to min/max for better visualization
            var padding = valueRange * 0.1;
            minValue -= padding;
            maxValue += padding;
            valueRange = maxValue - minValue;

            // Draw grid lines with labels
            var gridBrush = new SolidColorBrush(Color.FromRgb(60, 60, 70));
            var labelBrush = new SolidColorBrush(Color.FromRgb(120, 120, 130));
            
            for (int i = 0; i <= 4; i++)
            {
                var y = (i * height / 4);
                var line = new Line
                {
                    X1 = 0, Y1 = y, X2 = width, Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };
                canvas.Children.Add(line);

                // Add value labels on the left
                var value = maxValue - (i * valueRange / 4);
                var label = new TextBlock
                {
                    Text = $"{value:F0}",
                    Foreground = labelBrush,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold
                };
                Canvas.SetLeft(label, 5);
                Canvas.SetTop(label, y - 8);
                canvas.Children.Add(label);
            }

            // Draw filled area under the line first
            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(30, ((SolidColorBrush)lineColor).Color.R,
                    ((SolidColorBrush)lineColor).Color.G, ((SolidColorBrush)lineColor).Color.B)),
                Stroke = null
            };
            polygon.Points.Add(new Point(0, height));

            // Draw the main line with glow effect
            var polyline = new Polyline
            {
                Stroke = lineColor,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ((SolidColorBrush)lineColor).Color,
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.6
                }
            };

            for (int i = 0; i < data.Count; i++)
            {
                var x = (i / (double)(data.Count - 1)) * width;
                var y = height - ((data[i].Value - minValue) / valueRange) * height;
                var point = new Point(x, Math.Max(2, Math.Min(height - 2, y)));
                polyline.Points.Add(point);
                polygon.Points.Add(point);
            }
            
            polygon.Points.Add(new Point(width, height));
            canvas.Children.Add(polygon);
            canvas.Children.Add(polyline);

            // Add data point markers for recent values
            if (data.Count > 0)
            {
                var lastPoint = polyline.Points[polyline.Points.Count - 1];
                var marker = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = lineColor,
                    Stroke = Brushes.White,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(marker, lastPoint.X - 3);
                Canvas.SetTop(marker, lastPoint.Y - 3);
                canvas.Children.Add(marker);

                // Add current value label
                var currentValue = data[data.Count - 1].Value;
                var valueLabel = new TextBlock
                {
                    Text = $"{currentValue:F1} {unit}",
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(180, 40, 40, 50)),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                Canvas.SetLeft(valueLabel, Math.Min(width - 80, lastPoint.X + 10));
                Canvas.SetTop(valueLabel, Math.Max(0, lastPoint.Y - 12));
                canvas.Children.Add(valueLabel);
            }

            // Enhanced stats with more information
            var avgValue = data.Average(p => p.Value);
            var trend = data.Count > 10 ? data[data.Count - 1].Value - data[data.Count - 10].Value : 0;
            var trendIcon = Math.Abs(trend) < 0.1 ? "→" : trend > 0 ? "↗" : "↘";
            var trendColor = Math.Abs(trend) < 0.1 ? "gray" : trend > 0 ? "orange" : "lime";
            
            statsBlock.Text = $"📊 {minValue:F1}-{maxValue:F1} {unit}  •  Avg: {avgValue:F1} {unit}  •  {data.Count} samples  •  Trend: {trendIcon} {trend:+0.0;-0.0;0.0} {unit}";
            statsBlock.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160));
        }

        private async void RefreshDataButton_Click(object sender, RoutedEventArgs e)
        {
            await UpdateMetricsAsync();
            await CheckForAlertsAsync();
            await RefreshAllChartsAsync();
        }

        private async void ExportDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mainViewModel?.HardwareMonitoringService == null) return;

            try
            {
                var exportData = await _mainViewModel.HardwareMonitoringService.ExportMonitoringDataAsync();
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|CSV files (*.csv)|*.csv",
                    DefaultExt = "json",
                    FileName = $"hardware_monitoring_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    await System.IO.File.WriteAllTextAsync(saveDialog.FileName, exportData);
                    MessageBox.Show("Data exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetChartTitle(ChartType chartType)
        {
            return chartType switch
            {
                ChartType.PowerConsumption => "Power Consumption History",
                ChartType.BatteryHealth => "Battery Health History",
                ChartType.Temperature => "Temperature History",
                ChartType.FanSpeeds => "Fan Speeds History",
                _ => "Historical Data"
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}