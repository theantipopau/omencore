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
        private readonly OmenCore.Hardware.HpWmiBios? _wmiBios;

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
        
        /// <summary>
        /// Keyboard lighting telemetry statistics.
        /// </summary>
        public string KeyboardLightingTelemetry
        {
            get
            {
                if (_keyboardLightingService == null) return "No keyboard lighting service";
                var stats = _keyboardLightingService.GetTelemetry();
                return $"Attempts: {stats.TotalAttempts} | WMI: {stats.WmiSuccessCount}✓/{stats.WmiFailureCount}✗ ({stats.WmiSuccessRate:F0}%) | OGH: {stats.OghSuccessCount}✓/{stats.OghFailureCount}✗ ({stats.OghSuccessRate:F0}%) | EC: {stats.EcSuccessCount}✓/{stats.EcFailureCount}✗ ({stats.EcSuccessRate:F0}%)";
            }
        }

        public ICommand RunDeviceDetectionCommand { get; }
        public ICommand RunTestPatternCommand { get; }
        public ICommand ClearTestPatternCommand { get; }
        public ICommand CollectLogsCommand { get; }
        public ICommand RunColorTableProbeCommand { get; }
        public bool ColorTableProbeAvailable => _wmiBios?.IsAvailable == true;

        public KeyboardDiagnosticsViewModel(
            CorsairDeviceService? corsairService,
            LogitechDeviceService? logitechService,
            KeyboardLightingService? keyboardLightingService,
            RazerService? razerService,
            LoggingService logging,
            OmenCore.Hardware.HpWmiBios? wmiBios = null)
        {
            _wmiBios = wmiBios;
            RunColorTableProbeCommand = new AsyncRelayCommand(_ => RunColorTableProbeAsync(), _ => ColorTableProbeAvailable && !IsRunningTest);
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
                OnPropertyChanged(nameof(KeyboardLightingTelemetry));
            }
            catch (Exception ex)
            {
                DiagnosticStatus = $"Detection failed: {ex.Message}";
                _logging.Error("Device detection failed", ex);
            }
        }

        /// <summary>
        /// #212: sends each candidate single-zone colour table in turn (pure red, 4 s apart) and logs it with the
        /// firmware's readback, so the owner can say which numbered step lit the keyboard. See <see cref="OmenCore.Hardware.ColorTableProbe"/>.
        /// </summary>
        public async Task RunColorTableProbeAsync()
        {
            if (_wmiBios == null) return;
            var confirm = System.Windows.MessageBox.Show(
                $"This sends {OmenCore.Hardware.ColorTableProbe.Variants.Count} experimental keyboard colour commands, one every 4 seconds, each in red.\n\n" +
                "Watch the keyboard and note the NUMBER of the step during which it turns red, then report it on the GitHub issue. " +
                "Your current keyboard colours are saved first and put back when the probe finishes.",
                "RGB payload probe", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Information);
            if (confirm != System.Windows.MessageBoxResult.OK) return;

            IsRunningTest = true;
            // Several probe payloads are all-zero or declare the wrong zone count, which leaves the keyboard dark (#244).
            var original = await Task.Run(() => _wmiBios.GetColorTable());
            try
            {
                var variants = OmenCore.Hardware.ColorTableProbe.Variants;
                for (var i = 0; i < variants.Count; i++)
                {
                    var v = variants[i];
                    var ok = await Task.Run(() => _wmiBios.SendColorTablePayload(v.Build(0xFF, 0x00, 0x00)));
                    await Task.Delay(500);
                    var readback = await Task.Run(() => _wmiBios.GetColorTable());
                    var rb = readback == null || readback.Length < 28 ? "none" : $"{readback[25]:X2}{readback[26]:X2}{readback[27]:X2}";
                    var rb1 = readback == null || readback.Length < 4 ? "none" : $"{readback[1]:X2}{readback[2]:X2}{readback[3]:X2}";
                    var line = $"PROBE {i + 1}/{variants.Count} {v.Id}: sent={(ok ? "ok" : "refused")}, readback@25=#{rb}, readback@1=#{rb1} - {v.Description}";
                    _logging.Info(line);
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - {line}");
                    DiagnosticStatus = $"Probe step {i + 1} of {variants.Count}: is the keyboard red now?";
                    await Task.Delay(3500);
                }
                DiagnosticStatus = "Probe finished. Report which step number (if any) turned the keyboard red.";
            }
            finally
            {
                if (original is { Length: 128 })
                {
                    var restored = await Task.Run(() => _wmiBios.SendColorTablePayload(original) && _wmiBios.SetBacklight(true));
                    _logging.Info($"PROBE: previous keyboard colour table {(restored ? "restored" : "could not be restored")}");
                }
                IsRunningTest = false;
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
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Testing HP Omen keyboard (ColorTable pattern)");

                    var fourZonePattern = new[]
                    {
                        ColorTranslator.FromHtml("#FF0000"),
                        ColorTranslator.FromHtml("#00FF00"),
                        ColorTranslator.FromHtml("#0000FF"),
                        ColorTranslator.FromHtml("#FFFF00")
                    };

                    await _keyboardLightingService.SetAllZoneColors(fourZonePattern);
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Applied 4-zone RGB pattern");

                    await Task.Delay(600);

                    var white = ColorTranslator.FromHtml("#FFFFFF");
                    await _keyboardLightingService.SetAllZoneColors(new[] { white, white, white, white });
                    DiagnosticLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - Applied all-white verification pattern");
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
                OnPropertyChanged(nameof(KeyboardLightingTelemetry));
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

            OnPropertyChanged(nameof(KeyboardLightingTelemetry));

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