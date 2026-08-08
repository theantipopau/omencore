using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Corsair;
using OmenCore.Services;
using OmenCore.Services.Rgb;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
    public class LightingViewModelTests
    {
        public LightingViewModelTests()
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        [Fact]
        public async Task ApplyCorsairPresetToSystem_AppliesPresetToAllRegisteredProviders()
        {
            // Arrange
            Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");
            var logging = new LoggingService
            {
                Level = LogLevel.Info
            };

            var corsairStub = new OmenCore.Services.Corsair.CorsairSdkStub(logging);
            var corsairService = new CorsairDeviceService(corsairStub, logging);

            var logitechStub = new OmenCore.Services.Logitech.LogitechSdkStub(logging);
            var logitechService = new OmenCore.Services.LogitechDeviceService(logitechStub, logging);

            var configService = new ConfigurationService();
            var cfg = new OmenCore.Models.AppConfig
            {
                CorsairLightingPresets = new System.Collections.Generic.List<OmenCore.Corsair.CorsairLightingPreset>
            {
                new OmenCore.Corsair.CorsairLightingPreset { Name = "TestPreset", ColorHex = "#112233" }
            }
            };
            configService.Replace(cfg);

            var rgbManager = new RgbManager();
            var testProvider = new TestRgbProvider();
            rgbManager.RegisterProvider(testProvider);

            var vm = new LightingViewModel(corsairService, logitechService, logging, null, configService, null, rgbManager);

            vm.SelectedCorsairPreset = vm.CorsairLightingPresets.First(p => p.Name == "TestPreset");

            // Act
            vm.ApplyCorsairPresetToSystemCommand.Execute(null);
            await Task.Delay(150); // allow async command to complete

            // Assert
            testProvider.LastEffect.Should().Be("preset:TestPreset");
        }

        [Fact]
        public async Task RestoreKeyboardLightingCommand_WhenKeyboardUnavailable_ShowsRecoveryGuidance()
        {
            Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");
            var logging = new LoggingService { Level = LogLevel.Info };
            var configService = new ConfigurationService();
            var vm = new LightingViewModel(null, null, logging, keyboardLightingService: null, configService: configService);

            vm.KeyboardRestoreStatusText.Should().Contain("Ready");

            vm.RestoreKeyboardLightingCommand.Execute(null);
            await Task.Delay(50);

            vm.KeyboardRestoreStatusText.Should().Contain("unavailable");
            vm.KeyboardRestoreStatusText.Should().Contain("Settings");
        }

        [Fact]
        public void SaveRgbSurfaceObservationCommand_PersistsSelectedSurface()
        {
            Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");
            var logging = new LoggingService { Level = LogLevel.Info };
            var configService = new ConfigurationService();
            var vm = new LightingViewModel(null, null, logging, keyboardLightingService: null, configService: configService);

            vm.SelectedObservedRgbSurface = "Light bar changed";

            vm.SaveRgbSurfaceObservationCommand.Execute(null);

            var saved = new ConfigurationService().Load().KeyboardLighting;
            saved.ObservedSurface.Should().Be("Light bar changed");
            saved.ObservedProbeColorHex.Should().Be("#00FF66");
            saved.ObservedBackend.Should().Be("None");
            saved.ObservedApplyStatus.Should().Contain("No keyboard lighting apply attempted");
            saved.ObservedAtUtc.Should().NotBeNull();
            vm.RgbSurfaceObservationStatusText.Should().Contain("Light bar changed");
        }

        // The reactive lighting toggles shipped inert twice over: the view-model was constructed
        // with no monitoring service, so nothing was subscribed, and even wired up the first paint
        // waited for a poll tick that may be 30 s out. Both look identical from the UI — the toggle
        // moves and no light changes — so these two tests pin the observable behaviour rather than
        // the wiring.
        [Fact]
        public async Task EnablingTemperatureResponsiveLighting_PaintsImmediately()
        {
            var (vm, provider, monitoring, tempDir) = CreateReactiveLightingVm();
            try
            {
                AddSample(monitoring, cpuTempC: 40, gpuTempC: 40);

                vm.TemperatureResponsiveLightingEnabled = true;
                await Task.Delay(250);

                // 40 °C is below both medium thresholds, so the low-temperature colour.
                provider.LastEffect.Should().Be($"color:{vm.TempLowColorHex}");
            }
            finally
            {
                vm.Cleanup();
                monitoring.Dispose();
                Cleanup(tempDir);
            }
        }

        [Fact]
        public async Task EnablingTemperatureResponsiveLighting_WithNoSampleYet_DoesNotThrow()
        {
            var (vm, provider, monitoring, tempDir) = CreateReactiveLightingVm();
            try
            {
                // No sample has arrived, which is the state during startup.
                vm.TemperatureResponsiveLightingEnabled = true;
                await Task.Delay(150);

                provider.LastEffect.Should().BeNull();
            }
            finally
            {
                vm.Cleanup();
                monitoring.Dispose();
                Cleanup(tempDir);
            }
        }

        private static (LightingViewModel vm, TestRgbProvider provider,
                        HardwareMonitoringService monitoring, string tempDir) CreateReactiveLightingVm()
        {
            Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tempDir);

            var logging = new LoggingService { Level = LogLevel.Info };
            var monitoring = new HardwareMonitoringService(
                new ReactiveLightingBridgeStub(),
                logging,
                new OmenCore.Models.MonitoringPreferences(),
                new OmenCore.Services.Diagnostics.ResumeRecoveryDiagnosticsService());

            var rgbManager = new RgbManager();
            var provider = new TestRgbProvider();
            rgbManager.RegisterProvider(provider);

            var vm = new LightingViewModel(
                null, null, logging,
                keyboardLightingService: null,
                configService: new ConfigurationService(),
                razerService: null,
                rgbManager: rgbManager,
                hardwareMonitoringService: monitoring);

            return (vm, provider, monitoring, tempDir);
        }

        /// <summary>
        /// Push a sample into the service's history without starting its poll loop. The paint on
        /// enable reads Samples.LastOrDefault(), so that is the only state the test needs.
        /// </summary>
        private static void AddSample(HardwareMonitoringService monitoring, double cpuTempC, double gpuTempC)
        {
            var field = typeof(HardwareMonitoringService)
                .GetField("_samples", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field.Should().NotBeNull();

            var samples = (System.Collections.ObjectModel.ObservableCollection<OmenCore.Models.MonitoringSample>)field!.GetValue(monitoring)!;
            samples.Add(new OmenCore.Models.MonitoringSample
            {
                CpuTemperatureC = cpuTempC,
                GpuTemperatureC = gpuTempC
            });
        }

        private static void Cleanup(string tempDir)
        {
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
            try
            {
                if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true);
            }
            catch { }
        }

        private sealed class ReactiveLightingBridgeStub : OmenCore.Hardware.IHardwareMonitorBridge
        {
            public string MonitoringSource => "ReactiveLightingStub";

            public Task<OmenCore.Models.MonitoringSample> ReadSampleAsync(System.Threading.CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new OmenCore.Models.MonitoringSample());
            }

            public Task<bool> TryRestartAsync() => Task.FromResult(true);
        }

        private class TestRgbProvider : IRgbProvider
        {
            public string ProviderName => "TestProvider";
            public string ProviderId => "test";
            public bool IsAvailable { get; private set; } = true;
            public bool IsConnected => IsAvailable;
            public int DeviceCount => IsAvailable ? 1 : 0;
            public IReadOnlyList<RgbEffectType> SupportedEffects => new[] { RgbEffectType.Static, RgbEffectType.Breathing, RgbEffectType.Spectrum };
                        public RgbProviderConnectionStatus ConnectionStatus =>
                            IsAvailable ? RgbProviderConnectionStatus.Connected : RgbProviderConnectionStatus.Disabled;
                        public string StatusDetail => IsAvailable ? "1 device" : "Not available";
            public string? LastEffect { get; private set; }

            public Task InitializeAsync()
            {
                IsAvailable = true;
                return Task.CompletedTask;
            }

            public Task ApplyEffectAsync(string effectId)
            {
                LastEffect = effectId;
                return Task.CompletedTask;
            }

            public Task SetStaticColorAsync(System.Drawing.Color color)
            {
                LastEffect = $"static:#{color.R:X2}{color.G:X2}{color.B:X2}";
                return Task.CompletedTask;
            }

            public Task SetBreathingEffectAsync(System.Drawing.Color color)
            {
                LastEffect = $"breathing:#{color.R:X2}{color.G:X2}{color.B:X2}";
                return Task.CompletedTask;
            }

            public Task SetSpectrumEffectAsync()
            {
                LastEffect = "spectrum";
                return Task.CompletedTask;
            }

            public Task TurnOffAsync()
            {
                LastEffect = "off";
                return Task.CompletedTask;
            }
        }
    }
}
