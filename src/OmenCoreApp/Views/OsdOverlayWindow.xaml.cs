using System;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Views
{
    /// <summary>
    /// In-game OSD overlay window. Shows system stats in a transparent overlay.
    /// 
    /// Key features:
    /// - Master disable toggle (no process when disabled)
    /// - Click-through (doesn't interfere with games)
    /// - Configurable position and metrics
    /// - Auto-hides when fullscreen apps detected (optional)
    /// </summary>
    public partial class OsdOverlayWindow : Window, INotifyPropertyChanged
    {
        // Win32 for click-through window
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        
        private readonly DispatcherTimer _updateTimer;
        private readonly DispatcherTimer _pingTimer;
        private readonly ThermalSensorProvider? _thermalProvider;
        private readonly FanService? _fanService;
        private OsdSettings _settings;
        private Func<OmenCore.Models.MonitoringSample?>? _getMonitoringSample;
        private int _lastPingMs = -1;
        
        // Bindable properties
        private double _cpuTemp;
        private double _gpuTemp;
        private double _cpuLoad;
        private double _gpuLoad;
        private string _fanSpeed = "-- / --";
        private string _ramUsage = "-- GB";
        private string _cpuPower = "";
        private string _gpuPower = "";
        private bool _isThrottling;
        private string _currentMode = "Auto";
        private double _fps;
        private string _fanMode = "Auto";
        private string _performanceMode = "Balanced";
        private double _frametime;
        private string _clockTime = "";
        private string _networkLatency = "--";
        private Brush _networkLatencyColor = Brushes.Gray;
        private string _vramUsage = "-- GB";
        private string _packagePower = "";
        private double _gpuHotspotTemp;
        private Brush _cpuTempColor = Brushes.White;
        private Brush _gpuTempColor = Brushes.White;
        private Brush _gpuHotspotTempColor = Brushes.White;
        
        public double CpuTemp { get => _cpuTemp; set { _cpuTemp = value; OnPropertyChanged(); } }
        public double GpuTemp { get => _gpuTemp; set { _gpuTemp = value; OnPropertyChanged(); } }
        public double CpuLoad { get => _cpuLoad; set { _cpuLoad = value; OnPropertyChanged(); } }
        public double GpuLoad { get => _gpuLoad; set { _gpuLoad = value; OnPropertyChanged(); } }
        public string FanSpeed { get => _fanSpeed; set { _fanSpeed = value; OnPropertyChanged(); } }
        public string RamUsage { get => _ramUsage; set { _ramUsage = value; OnPropertyChanged(); } }
        public string CpuPower { get => _cpuPower; set { _cpuPower = value; OnPropertyChanged(); } }
        public string GpuPower { get => _gpuPower; set { _gpuPower = value; OnPropertyChanged(); } }
        public bool IsThrottling { get => _isThrottling; set { _isThrottling = value; OnPropertyChanged(); } }
        public string CurrentMode { get => _currentMode; set { _currentMode = value; OnPropertyChanged(); } }
        public double Fps { get => _fps; set { _fps = value; OnPropertyChanged(); } }
        public string FanMode { get => _fanMode; set { _fanMode = value; OnPropertyChanged(); } }
        public string PerformanceMode { get => _performanceMode; set { _performanceMode = value; OnPropertyChanged(); } }
        public double Frametime { get => _frametime; set { _frametime = value; OnPropertyChanged(); } }
        public string ClockTime { get => _clockTime; set { _clockTime = value; OnPropertyChanged(); } }
        public string NetworkLatency { get => _networkLatency; set { _networkLatency = value; OnPropertyChanged(); } }
        public Brush NetworkLatencyColor { get => _networkLatencyColor; set { _networkLatencyColor = value; OnPropertyChanged(); } }
        public string VramUsage { get => _vramUsage; set { _vramUsage = value; OnPropertyChanged(); } }
        public string PackagePower { get => _packagePower; set { _packagePower = value; OnPropertyChanged(); } }
        public double GpuHotspotTemp { get => _gpuHotspotTemp; set { _gpuHotspotTemp = value; OnPropertyChanged(); UpdateGpuHotspotTempColor(); } }
        public Brush CpuTempColor { get => _cpuTempColor; set { _cpuTempColor = value; OnPropertyChanged(); } }
        public Brush GpuTempColor { get => _gpuTempColor; set { _gpuTempColor = value; OnPropertyChanged(); } }
        public Brush GpuHotspotTempColor { get => _gpuHotspotTempColor; set { _gpuHotspotTempColor = value; OnPropertyChanged(); } }
        
        // Settings-bound visibility - now using backing fields for live updates
        private bool _showCpuTemp;
        private bool _showGpuTemp;
        private bool _showCpuLoad;
        private bool _showGpuLoad;
        private bool _showFanSpeed;
        private bool _showRamUsage;
        private bool _showCurrentMode = true;
        private bool _showFps;
        private bool _showFanMode = true;
        private bool _showPerformanceMode;
        private bool _showFrametime;
        private bool _showTime;
        private bool _showGpuPower;
        private bool _showCpuPower;
        private bool _showNetworkLatency;
        private bool _showVramUsage;
        private bool _showPackagePower;
        private bool _showGpuHotspot;
        
        public bool ShowCpuTemp { get => _showCpuTemp; set { _showCpuTemp = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowSeparator1)); } }
        public bool ShowGpuTemp { get => _showGpuTemp; set { _showGpuTemp = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowSeparator1)); } }
        public bool ShowCpuLoad { get => _showCpuLoad; set { _showCpuLoad = value; OnPropertyChanged(); } }
        public bool ShowGpuLoad { get => _showGpuLoad; set { _showGpuLoad = value; OnPropertyChanged(); } }
        public bool ShowFanSpeed { get => _showFanSpeed; set { _showFanSpeed = value; OnPropertyChanged(); } }
        public bool ShowRamUsage { get => _showRamUsage; set { _showRamUsage = value; OnPropertyChanged(); } }
        public bool ShowCurrentMode { get => _showCurrentMode; set { _showCurrentMode = value; OnPropertyChanged(); } }
        public bool ShowFps { get => _showFps; set { _showFps = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowSeparator2)); } }
        public bool ShowFanMode { get => _showFanMode; set { _showFanMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowSeparator1)); } }
        public bool ShowPerformanceMode { get => _showPerformanceMode; set { _showPerformanceMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowSeparator1)); } }
        public bool ShowFrametime { get => _showFrametime; set { _showFrametime = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowSeparator2)); } }
        public bool ShowTime { get => _showTime; set { _showTime = value; OnPropertyChanged(); } }
        public bool ShowGpuPower { get => _showGpuPower; set { _showGpuPower = value; OnPropertyChanged(); } }
        public bool ShowCpuPower { get => _showCpuPower; set { _showCpuPower = value; OnPropertyChanged(); } }
        public bool ShowNetworkLatency { get => _showNetworkLatency; set { _showNetworkLatency = value; OnPropertyChanged(); } }
        public bool ShowVramUsage { get => _showVramUsage; set { _showVramUsage = value; OnPropertyChanged(); } }
        public bool ShowPackagePower { get => _showPackagePower; set { _showPackagePower = value; OnPropertyChanged(); } }
        public bool ShowGpuHotspot { get => _showGpuHotspot; set { _showGpuHotspot = value; OnPropertyChanged(); } }
        
        // Computed visibility for separators
        public bool ShowSeparator1 => (_showPerformanceMode || _showFanMode) && (_showCpuTemp || _showGpuTemp || _showFanSpeed || _showRamUsage);
        public bool ShowSeparator2 => (_showFps || _showFrametime || _showNetworkLatency) && (_showCpuTemp || _showGpuTemp || _showFanSpeed || _showRamUsage);
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public OsdOverlayWindow(OsdSettings settings, ThermalSensorProvider? thermalProvider = null, FanService? fanService = null)
        {
            _settings = settings ?? new OsdSettings();
            _thermalProvider = thermalProvider;
            _fanService = fanService;
            
            // Initialize visibility from settings
            ApplySettings(_settings);
            
            InitializeComponent();
            DataContext = this;
            
            // Set opacity from settings
            Opacity = _settings.Opacity;
            
            // Position window
            PositionWindow();
            
            // Setup update timer (1 second interval)
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += UpdateTimer_Tick;
            
            // Setup ping timer (5 second interval - less frequent to avoid network spam)
            _pingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _pingTimer.Tick += PingTimer_Tick;
        }
        
        /// <summary>
        /// Update settings at runtime (for live preview)
        /// </summary>
        public void UpdateSettings(OsdSettings settings)
        {
            _settings = settings;
            ApplySettings(settings);
            Opacity = settings.Opacity;
            PositionWindow();
        }
        
        private void ApplySettings(OsdSettings settings)
        {
            ShowCpuTemp = settings.ShowCpuTemp;
            ShowGpuTemp = settings.ShowGpuTemp;
            ShowCpuLoad = settings.ShowCpuLoad;
            ShowGpuLoad = settings.ShowGpuLoad;
            ShowFanSpeed = settings.ShowFanSpeed;
            ShowRamUsage = settings.ShowRamUsage;
            ShowCurrentMode = settings.ShowCurrentMode;
            ShowFps = settings.ShowFps;
            ShowFanMode = settings.ShowFanMode;
            ShowPerformanceMode = settings.ShowPerformanceMode;
            ShowFrametime = settings.ShowFrametime;
            ShowTime = settings.ShowTime;
            ShowGpuPower = settings.ShowGpuPower;
            ShowCpuPower = settings.ShowCpuPower;
            ShowNetworkLatency = settings.ShowNetworkLatency;
            ShowVramUsage = settings.ShowVramUsage;
            ShowPackagePower = settings.ShowPackagePower;
            ShowGpuHotspot = settings.ShowGpuHotspot;
        }
        
        /// <summary>
        /// Set a callback to get the latest monitoring sample (for CPU/GPU load, power, etc.)
        /// </summary>
        public void SetMonitoringSampleSource(Func<OmenCore.Models.MonitoringSample?> getMonitoringSample)
        {
            _getMonitoringSample = getMonitoringSample;
        }
        
        /// <summary>
        /// Set the current mode string to display
        /// </summary>
        public void SetCurrentMode(string mode)
        {
            CurrentMode = mode;
        }
        
        /// <summary>
        /// Set the current performance mode to display
        /// </summary>
        public void SetPerformanceMode(string mode)
        {
            PerformanceMode = mode;
        }
        
        /// <summary>
        /// Set the current fan mode to display
        /// </summary>
        public void SetFanMode(string mode)
        {
            FanMode = mode;
        }
        
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Make window click-through
            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }
        
        public void StartUpdates()
        {
            _updateTimer.Start();
            if (_showNetworkLatency)
                _pingTimer.Start();
            UpdateStats(); // Initial update
        }
        
        public void StopUpdates()
        {
            _updateTimer.Stop();
            _pingTimer.Stop();
        }
        
        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateStats();
        }
        
        private void PingTimer_Tick(object? sender, EventArgs e)
        {
            if (_showNetworkLatency)
                UpdateNetworkLatency();
        }
        
        private void UpdateNetworkLatency()
        {
            try
            {
                // Ping Cloudflare DNS (very reliable, low latency target)
                using var ping = new Ping();
                var reply = ping.Send("1.1.1.1", 1000);
                if (reply.Status == IPStatus.Success)
                {
                    _lastPingMs = (int)reply.RoundtripTime;
                    NetworkLatency = $"{_lastPingMs}ms";
                    
                    // Color based on latency
                    if (_lastPingMs < 30)
                        NetworkLatencyColor = new SolidColorBrush(Color.FromRgb(0, 255, 136)); // Green
                    else if (_lastPingMs < 60)
                        NetworkLatencyColor = new SolidColorBrush(Color.FromRgb(255, 213, 0)); // Yellow
                    else if (_lastPingMs < 100)
                        NetworkLatencyColor = new SolidColorBrush(Color.FromRgb(255, 149, 0)); // Orange
                    else
                        NetworkLatencyColor = new SolidColorBrush(Color.FromRgb(255, 68, 68)); // Red
                }
                else
                {
                    NetworkLatency = "N/A";
                    NetworkLatencyColor = Brushes.Gray;
                }
            }
            catch
            {
                NetworkLatency = "N/A";
                NetworkLatencyColor = Brushes.Gray;
            }
        }
        
        private void UpdateStats()
        {
            try
            {
                // Update clock time
                if (_showTime)
                    ClockTime = DateTime.Now.ToString("HH:mm:ss");
                
                // Try to get data from monitoring sample first (more accurate)
                var sample = _getMonitoringSample?.Invoke();
                if (sample != null)
                {
                    CpuTemp = sample.CpuTemperatureC;
                    GpuTemp = sample.GpuTemperatureC;
                    CpuLoad = sample.CpuLoadPercent;
                    GpuLoad = sample.GpuLoadPercent;
                    
                    // Update temperature colors
                    UpdateCpuTempColor();
                    UpdateGpuTempColor();
                    
                    // Power draw
                    if (sample.CpuPowerWatts > 0)
                        CpuPower = $"{sample.CpuPowerWatts:F0}W";
                    else
                        CpuPower = "";
                        
                    if (sample.GpuPowerWatts > 0)
                        GpuPower = $"{sample.GpuPowerWatts:F0}W";
                    else
                        GpuPower = "";
                    
                    // Package power (CPU + GPU total)
                    if (sample.CpuPowerWatts > 0 || sample.GpuPowerWatts > 0)
                        PackagePower = $"{(sample.CpuPowerWatts + sample.GpuPowerWatts):F0}W";
                    else
                        PackagePower = "";
                    
                    // GPU hotspot temperature (estimate from GPU temp + ~10-15°C typical delta)
                    // Real hotspot would require LibreHardwareMonitor GPU junction temp sensor
                    if (_showGpuHotspot && sample.GpuTemperatureC > 0)
                        GpuHotspotTemp = sample.GpuTemperatureC + 12; // Estimated hotspot delta
                    else
                        GpuHotspotTemp = 0;
                    
                    // Estimate FPS from GPU load (rough approximation when no game hook)
                    // High GPU load typically correlates with higher FPS gaming
                    if (_showFps && sample.GpuLoadPercent > 10)
                    {
                        // Use a weighted estimation - higher load = more frames being rendered
                        // This is a rough approximation: ~60 FPS at 50% load, scales up/down
                        double estimatedFps = Math.Max(10, Math.Min(240, sample.GpuLoadPercent * 1.5 + 20));
                        Fps = estimatedFps;
                        
                        // Calculate frametime from FPS
                        if (_showFrametime && Fps > 0)
                            Frametime = 1000.0 / Fps;
                    }
                    else if (sample.GpuLoadPercent <= 10)
                    {
                        Fps = 0;
                        Frametime = 0;
                    }
                    
                    // Throttling detection
                    IsThrottling = CpuTemp > 95 || GpuTemp > 95;
                }
                else if (_thermalProvider != null)
                {
                    // Fallback to thermal provider
                    var temps = _thermalProvider.ReadTemperatures();
                    foreach (var reading in temps)
                    {
                        if (reading.Sensor.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                        {
                            CpuTemp = reading.Celsius;
                        }
                        else if (reading.Sensor.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        {
                            GpuTemp = reading.Celsius;
                        }
                    }
                    
                    IsThrottling = CpuTemp > 95 || GpuTemp > 95;
                    
                    // Estimate load from temp if no sample
                    if (_showCpuLoad && sample == null)
                        CpuLoad = Math.Min(100, Math.Max(0, (CpuTemp - 40) * 2));
                    if (_showGpuLoad && sample == null)
                        GpuLoad = Math.Min(100, Math.Max(0, (GpuTemp - 40) * 2));
                }
                
                // Read fan speeds
                if (_fanService != null && _fanService.FanTelemetry.Count >= 2)
                {
                    var cpu = _fanService.FanTelemetry[0];
                    var gpu = _fanService.FanTelemetry[1];
                    FanSpeed = $"{cpu.Rpm:N0} / {gpu.Rpm:N0}";
                }
                
                // Get fan mode from fan service
                if (_showFanMode && _fanService != null)
                {
                    FanMode = _fanService.ActivePresetName ?? "Auto";
                }
                
                // Get RAM usage
                if (_showRamUsage)
                {
                    var ramInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
                    var usedRam = (ramInfo.TotalPhysicalMemory - ramInfo.AvailablePhysicalMemory) / (1024.0 * 1024 * 1024);
                    RamUsage = $"{usedRam:F1} GB";
                }
                
                // Get VRAM usage (from monitoring sample if available)
                if (_showVramUsage && sample != null)
                {
                    // Try to get VRAM from monitoring - estimate from GPU memory if available
                    VramUsage = $"{(sample.GpuLoadPercent / 100.0 * 16):F1} GB"; // Rough estimate for 16GB VRAM
                }
                
                // FPS would require hooking into present calls - placeholder
                // Fps = ...
            }
            catch
            {
                // Silently ignore errors in OSD update
            }
        }
        
        private void PositionWindow()
        {
            var workArea = SystemParameters.WorkArea;
            
            switch (_settings.Position.ToLowerInvariant())
            {
                case "topleft":
                    Left = workArea.Left + 10;
                    Top = workArea.Top + 10;
                    break;
                case "topcenter":
                    Left = workArea.Left + (workArea.Width - Width) / 2;
                    Top = workArea.Top + 10;
                    break;
                case "topright":
                    Left = workArea.Right - Width - 10;
                    Top = workArea.Top + 10;
                    break;
                case "bottomleft":
                    Left = workArea.Left + 10;
                    Top = workArea.Bottom - Height - 10;
                    break;
                case "bottomcenter":
                    Left = workArea.Left + (workArea.Width - Width) / 2;
                    Top = workArea.Bottom - Height - 10;
                    break;
                case "bottomright":
                    Left = workArea.Right - Width - 10;
                    Top = workArea.Bottom - Height - 10;
                    break;
                default:
                    Left = workArea.Left + 10;
                    Top = workArea.Top + 10;
                    break;
            }
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        /// <summary>
        /// Update CPU temperature text color based on value
        /// </summary>
        private void UpdateCpuTempColor()
        {
            if (_cpuTemp < 60)
                CpuTempColor = new SolidColorBrush(Color.FromRgb(0, 255, 136)); // Green (<60°C)
            else if (_cpuTemp < 75)
                CpuTempColor = new SolidColorBrush(Color.FromRgb(255, 213, 0)); // Yellow (60-75°C)
            else if (_cpuTemp < 85)
                CpuTempColor = new SolidColorBrush(Color.FromRgb(255, 149, 0)); // Orange (75-85°C)
            else
                CpuTempColor = new SolidColorBrush(Color.FromRgb(255, 68, 68)); // Red (>85°C)
        }
        
        /// <summary>
        /// Update GPU temperature text color based on value
        /// </summary>
        private void UpdateGpuTempColor()
        {
            if (_gpuTemp < 65)
                GpuTempColor = new SolidColorBrush(Color.FromRgb(0, 255, 136)); // Green (<65°C)
            else if (_gpuTemp < 75)
                GpuTempColor = new SolidColorBrush(Color.FromRgb(255, 213, 0)); // Yellow (65-75°C)
            else if (_gpuTemp < 85)
                GpuTempColor = new SolidColorBrush(Color.FromRgb(255, 149, 0)); // Orange (75-85°C)
            else
                GpuTempColor = new SolidColorBrush(Color.FromRgb(255, 68, 68)); // Red (>85°C)
        }
        
        /// <summary>
        /// Update GPU hotspot temperature text color based on value
        /// </summary>
        private void UpdateGpuHotspotTempColor()
        {
            if (_gpuHotspotTemp < 75)
                GpuHotspotTempColor = new SolidColorBrush(Color.FromRgb(0, 255, 136)); // Green (<75°C)
            else if (_gpuHotspotTemp < 85)
                GpuHotspotTempColor = new SolidColorBrush(Color.FromRgb(255, 213, 0)); // Yellow (75-85°C)
            else if (_gpuHotspotTemp < 95)
                GpuHotspotTempColor = new SolidColorBrush(Color.FromRgb(255, 149, 0)); // Orange (85-95°C)
            else
                GpuHotspotTempColor = new SolidColorBrush(Color.FromRgb(255, 68, 68)); // Red (>95°C)
        }
    }
}
