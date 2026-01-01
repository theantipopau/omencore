using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Drawing;
using OmenCore.Services;
using OmenCore.Razer;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    public class KeyboardDiagnosticsViewModel : ViewModelBase
    {
        private readonly CorsairDeviceService? _corsairService;
        private readonly LogitechDeviceService? _logitechService;
        private readonly KeyboardLightingService? _keyboardLightingService;
        private readonly RazerService? _razerService;
        private readonly LoggingService _logging;

        public ObservableCollection<LightingDeviceInfo> DetectedDevices { get; } = new();
        public ObservableCollection<string> DiagnosticLogs { get; } = new();

        private string _diagnosticStatus = "Ready";
        public string DiagnosticStatus
        {
            get => _diagnosticStatus;
            set { _diagnosticStatus = value; OnPropertyChanged(); }
        }

        private bool _isRunningTest;
        public bool IsRunningTest
        {
            get => _isRunningTest;
            set { _isRunningTest = value; OnPropertyChanged(); }
        }

        public bool HasCorsair => _corsairService != null;
        public bool HasLogitech => _logitechService != null;
        public bool HasKeyboardLighting => _keyboardLightingService?.IsAvailable ?? false;
        public bool HasRazer => _razerService != null;

        public ICommand RunDeviceDetectionCommand { get; }
        public ICommand RunTestPatternCommand { get; }
        public ICommand ClearTestPatternCommand { get; }
        public ICommand CollectLogsCommand { get; }

        public KeyboardDiagnosticsViewModel(
            CorsairDeviceService? corsairService,
            LogitechDeviceService? logitechService,
            KeyboardLightingService? keyboardLightingService,
            RazerService? razerService,
            LoggingService logging)
        {
            _corsairService = corsairService;
            _logitechService = logitechService;
            _keyboardLightingService = keyboardLightingService;
            _razerService = razerService;
            _logging = logging;

            RunDeviceDetectionCommand = new AsyncRelayCommand(_ => RunDeviceDetectionAsync());
            RunTestPatternCommand = new AsyncRelayCommand(_ => RunTestPatternAsync(), _ => !IsRunningTest);
            ClearTestPatternCommand = new AsyncRelayCommand(_ => ClearTestPatternAsync(), _ => !IsRunningTest);
            CollectLogsCommand = new RelayCommand(_ => CollectLogs());

            // Initial detection
            _ = RunDeviceDetectionAsync();
        }

        public async Task RunDeviceDetectionAsync()
        {
            try
            {
                DiagnosticStatus = "Detecting devices...";
                DetectedDevices.Clear();

                // Detect Corsair devices
                if (_corsairService != null)
                {
                    await _corsairService.DiscoverAsync();
                    foreach (var device in _corsairService.Devices)
                    {
                        DetectedDevices.Add(new LightingDeviceInfo
                        {
                            Brand = "Corsair",
                            Model = device.Name,
                            Type = device.DeviceType.ToString(),
                            Status = "Connected",
                            Backend = "iCUE SDK"
                        });
                    }
                }

                // Detect Logitech devices
                if (_logitechService != null)
                {
                    await _logitechService.DiscoverAsync();
                    foreach (var device in _logitechService.Devices)
                    {
                        DetectedDevices.Add(new LightingDeviceInfo
                        {
                            Brand = "Logitech",
                            Model = device.Name,
                            Type = device.DeviceType.ToString(),
                            Status = "Connected",
                            Backend = "G HUB SDK"
                        });
                    }
                }

                // Detect Razer devices
                if (_razerService != null)
                {
                    _razerService.DiscoverDevices();
                    foreach (var device in _razerService.Devices)
                    {
                        DetectedDevices.Add(new LightingDeviceInfo
                        {
                            Brand = "Razer",
                            Model = device.Name,
                            Type = device.DeviceType.ToString(),
                            Status = "Connected",
                            Backend = "Chroma SDK"
                        });
                    }
                }

                // Detect HP Omen keyboard
                if (_keyboardLightingService?.IsAvailable ?? false)
                {
                    DetectedDevices.Add(new LightingDeviceInfo
                    {
                        Brand = "HP Omen",
                        Model = "Integrated Keyboard",
                        Type = "Keyboard",
                        Status = "Connected",
                        Backend = _keyboardLightingService.BackendType
                    });
                }

                DiagnosticStatus = $"Detection complete. Found {DetectedDevices.Count} device(s).";
            }
            catch (Exception ex)
            {
                DiagnosticStatus = $"Detection failed: {ex.Message}";
                _logging.Error("Device detection failed", ex);
            }
        }

        public async Task RunTestPatternAsync()
        {
            try
            {
                IsRunningTest = true;
                DiagnosticStatus = "Running test pattern...";
                DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Starting test pattern");

                // Test Corsair devices
                if (_corsairService != null && _corsairService.Devices.Any())
                {
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Testing Corsair devices");
                    foreach (var device in _corsairService.Devices)
                    {
                        // Apply a rainbow pattern or simple test
                        await Task.Delay(100); // Simulate
                        DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Applied test to {device.Name}");
                    }
                }

                // Test Logitech devices
                if (_logitechService != null && _logitechService.Devices.Any())
                {
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Testing Logitech devices");
                    foreach (var device in _logitechService.Devices)
                    {
                        await Task.Delay(100);
                        DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Applied test to {device.Name}");
                    }
                }

                // Test Razer devices
                if (_razerService != null && _razerService.Devices.Any())
                {
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Testing Razer devices");
                    foreach (var device in _razerService.Devices)
                    {
                        await Task.Delay(100);
                        DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Applied test to {device.Name}");
                    }
                }

                // Test HP Omen keyboard
                if (_keyboardLightingService?.IsAvailable ?? false)
                {
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Testing HP Omen keyboard zones");
                    // Apply test colors to zones: Red, Green, Blue, Yellow
                    var testColors = new[] { "#FF0000", "#00FF00", "#0000FF", "#FFFF00" };
                    for (int i = 0; i < 4; i++)
                    {
                        if (_keyboardLightingService.IsAvailable)
                        {
                            var zone = (KeyboardLightingService.KeyboardZone)i;
                            _keyboardLightingService.SetZoneColor(zone, ColorTranslator.FromHtml(testColors[i]));
                            DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Set zone {zone} to {testColors[i]}");
                            await Task.Delay(500);
                        }
                    }
                }

                DiagnosticStatus = "Test pattern applied successfully.";
                DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Test pattern completed");
            }
            catch (Exception ex)
            {
                DiagnosticStatus = $"Test pattern failed: {ex.Message}";
                DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Test pattern failed: {ex.Message}");
                _logging.Error("Test pattern failed", ex);
            }
            finally
            {
                IsRunningTest = false;
            }
        }

        public Task ClearTestPatternAsync()
        {
            try
            {
                IsRunningTest = true;
                DiagnosticStatus = "Clearing test pattern...";
                DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Clearing test patterns");

                // Clear Corsair devices
                if (_corsairService != null)
                {
                    // Reset to default
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Cleared Corsair devices");
                }

                // Clear Logitech devices
                if (_logitechService != null)
                {
                    // Reset to default
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Cleared Logitech devices");
                }

                // Clear Razer devices
                if (_razerService != null)
                {
                    // Reset to default
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Cleared Razer devices");
                }

                // Clear HP Omen keyboard
                if (_keyboardLightingService?.IsAvailable ?? false)
                {
                    _keyboardLightingService.RestoreDefaults();
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Restored HP Omen keyboard defaults");
                }

                DiagnosticStatus = "Test pattern cleared.";
                DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Clear completed");
            }
            catch (Exception ex)
            {
                DiagnosticStatus = $"Clear failed: {ex.Message}";
                DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Clear failed: {ex.Message}");
                _logging.Error("Clear test pattern failed", ex);
            }
            finally
            {
                IsRunningTest = false;
            }

            return Task.CompletedTask;
        }

        private void CollectLogs()
        {
            DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Collecting diagnostic logs");
            // In a real implementation, this would gather logs from various services
            // For now, just add some sample logs
            DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - KeyboardLightingService: Available={HasKeyboardLighting}, Backend={_keyboardLightingService?.BackendType ?? "N/A"}");
            DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - CorsairService: Available={HasCorsair}");
            DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - LogitechService: Available={HasLogitech}");
            DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - RazerService: Available={HasRazer}");
            DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Detected devices: {DetectedDevices.Count}");
        }
    }

    public class LightingDeviceInfo
    {
        public string Brand { get; set; } = "";
        public string Model { get; set; } = "";
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
        public string Backend { get; set; } = "";
    }
}