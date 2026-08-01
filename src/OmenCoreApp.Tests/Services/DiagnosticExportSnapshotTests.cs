using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// Tests for DiagnosticExportService monitoring-cadence-hold snapshot content.
    /// Runs a real export with null optional services and inspects the resulting ZIP.
    /// </summary>
    public class DiagnosticExportSnapshotTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly LoggingService _logging;

        public DiagnosticExportSnapshotTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "OmenCoreDiagTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);

            _logging = new LoggingService();
            _logging.Initialize();
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private sealed class ReadbackFanController : OmenCore.Hardware.IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Ready";
            public string Backend => "WMI BIOS";
            public bool ApplyPreset(FanPreset preset) => true;
            public bool ApplyCustomCurve(System.Collections.Generic.IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public System.Collections.Generic.IEnumerable<FanTelemetry> ReadFanSpeeds() => new[]
            {
                new FanTelemetry
                {
                    Name = "CPU Fan",
                    SpeedRpm = 1900,
                    DutyCyclePercent = 42,
                    RpmState = TelemetryDataState.Valid,
                    RpmSource = RpmSource.WmiBios
                }
            };
            public bool ApplyMaxCooling() {  return true; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public bool VerifyMaxApplied(out string details) { details = "ok"; return true; }
            public void Dispose() { }
        }

        [Fact]
        public async Task MonitoringCadenceHoldFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            // The export might fall back to a directory if ZIP fails; handle both.
            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("monitoring-cadence-hold.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("monitoring-cadence-hold.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "monitoring-cadence-hold.txt");
                File.Exists(txtPath).Should().BeTrue("monitoring-cadence-hold.txt must be written to the export folder");
            }
        }

        [Fact]
        public async Task ResourceFootprintFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("resource-footprint.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("resource-footprint.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "resource-footprint.txt");
                File.Exists(txtPath).Should().BeTrue("resource-footprint.txt must be written to the export folder");
            }
        }

        [Fact]
        public async Task RgbControlPathFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("rgb-control-path.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("rgb-control-path.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "rgb-control-path.txt");
                File.Exists(txtPath).Should().BeTrue("rgb-control-path.txt must be written to the export folder");
            }
        }

        [Fact]
        public async Task TuningSafetyFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("tuning-safety.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("tuning-safety.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "tuning-safety.txt");
                File.Exists(txtPath).Should().BeTrue("tuning-safety.txt must be written to the export folder");
            }
        }

        [Fact]
        public async Task LaunchReadinessFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("launch-readiness.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("launch-readiness.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "launch-readiness.txt");
                File.Exists(txtPath).Should().BeTrue("launch-readiness.txt must be written to the export folder");
            }
        }

        [Fact]
        public async Task CoreControlReadinessFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("core-control-readiness.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("core-control-readiness.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "core-control-readiness.txt");
                File.Exists(txtPath).Should().BeTrue("core-control-readiness.txt must be written to the export folder");
            }
        }

        [Fact]
        public async Task OmenMonRebornParityFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("omenmon-reborn-parity.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("omenmon-reborn-parity.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "omenmon-reborn-parity.txt");
                File.Exists(txtPath).Should().BeTrue("omenmon-reborn-parity.txt must be written to the export folder");
            }
        }

        [Fact]
        public async Task FieldValidationScriptFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("field-validation-script.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("field-validation-script.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "field-validation-script.txt");
                File.Exists(txtPath).Should().BeTrue("field-validation-script.txt must be written to the export folder");
            }
        }

        [Fact]
        public async Task PriorityModelValidationCardsFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("priority-model-validation-cards.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("priority-model-validation-cards.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "priority-model-validation-cards.txt");
                File.Exists(txtPath).Should().BeTrue("priority-model-validation-cards.txt must be written to the export folder");
            }
        }

        [Fact]
        public async Task RcValidationMatrixFile_IsIncludedInExport()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            zipPath.Should().NotBeNullOrEmpty();

            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.Equals("rc-validation-matrix.txt", StringComparison.OrdinalIgnoreCase));
                entry.Should().NotBeNull("rc-validation-matrix.txt must be present in the diagnostic bundle");
            }
            else
            {
                var txtPath = Path.Combine(zipPath, "rc-validation-matrix.txt");
                File.Exists(txtPath).Should().BeTrue("rc-validation-matrix.txt must be written to the export folder");
            }
        }

        [Fact]
        public void BuildPriorityModelValidationCardsReport_ContainsPriorityBoardCards()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var report = svc.BuildPriorityModelValidationCardsReport();

            report.Should().Contain("PRIORITY MODEL VALIDATION CARDS");
            report.Should().Contain("8D41 / OMEN Max 16-ah0xxx");
            report.Should().Contain("8D87 / OMEN Max follow-up");
            report.Should().Contain("8BD4 / Victus 16-s0xxx");
            report.Should().Contain("8C30 / Victus 15-fb1xxx");
            report.Should().Contain("8DCD / Victus 15");
            report.Should().Contain("878C / OMEN 15-ek0xxx");
            report.Should().Contain("8600 / OMEN 15-dh0xxx");
            report.Should().Contain("8BCD / Linux OMEN 16-xd0xxx");
            report.Should().Contain("OMEN 17 db-1000");
            report.Should().Contain("Victus 15/16 field cohort");
            report.Should().Contain("WMI thermal-policy fallback applied");
            report.Should().Contain("[Promotion Rule]");
        }

        [Fact]
        public void BuildRcValidationMatrixReport_ContainsReleaseGateRows()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var report = svc.BuildRcValidationMatrixReport();

            report.Should().Contain("RC VALIDATION MATRIX");
            report.Should().Contain("8D41 / OMEN Max 16-ah0xxx");
            report.Should().Contain("8D87 / OMEN Max follow-up");
            report.Should().Contain("8BD4 / Victus 16-s0xxx");
            report.Should().Contain("8C30 / Victus 15-fb1xxx");
            report.Should().Contain("8DCD / Victus 15");
            report.Should().Contain("878C / OMEN 15-ek0xxx");
            report.Should().Contain("8600 / OMEN 15-dh0xxx");
            report.Should().Contain("8BCD / Linux OMEN 16-xd0xxx");
            report.Should().Contain("OMEN 17 db-1000");
            report.Should().Contain("Victus 15/16 field cohort");
            report.Should().Contain("Field validation pending");
            report.Should().Contain("Keep v3.8.1 as RC/pre-release");
        }

        [Fact]
        public void BuildFieldValidationScriptReport_ContainsReleaseGateSteps()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var report = svc.BuildFieldValidationScriptReport();

            report.Should().Contain("FIELD VALIDATION SCRIPT");
            report.Should().Contain("[Fan Validation]");
            report.Should().Contain("Apply Max; hold for 10 minutes");
            report.Should().Contain("test 40%, 60%, and 80%");
            report.Should().Contain("[RGB / Surface Validation]");
            report.Should().Contain("[Performance / Tuning Validation]");
            report.Should().Contain("[Profile Cycling And Hotkeys]");
            report.Should().Contain("[Startup Restore Validation]");
            report.Should().Contain("[Evidence To Attach]");
        }

        [Fact]
        public void BuildOmenMonRebornParityReport_ContainsParityMatrixAndSafetyPolicy()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var report = svc.BuildOmenMonRebornParityReport();

            report.Should().Contain("OMENMON-REBORN PARITY SNAPSHOT");
            report.Should().Contain("[Source Expectations]");
            report.Should().Contain("[OmenCore Current Equivalents]");
            report.Should().Contain("[Parity Matrix]");
            report.Should().Contain("Probe report:");
            report.Should().Contain("Auto-calibration wizard:");
            report.Should().Contain("EC contention hardening:");
            report.Should().Contain("[Safe Emulation Policy]");
            report.Should().Contain("Emulate behavior and diagnostics, not GPL source code.");
            report.Should().Contain("[Next Evidence To Collect]");
        }

        [Fact]
        public async Task CoreControlReadinessFile_ContainsCoreControlSections_WhenServicesUnavailable()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "core-control-readiness.txt");

            content.Should().Contain("CORE CONTROL READINESS");
            content.Should().Contain("[Fans]");
            content.Should().Contain("Status: service unavailable");
            content.Should().Contain("[RGB]");
            content.Should().Contain("HpKeyboardService: unavailable");
            content.Should().Contain("[Tuning / OC / Undervolt]");
            content.Should().Contain("ReadbackRule:");
            content.Should().Contain("[Monitoring / Readback]");
            content.Should().Contain("[Hotkeys / OMEN Key]");
            content.Should().Contain("HotkeyService: unavailable");
            content.Should().Contain("OmenKeyService: unavailable");
            content.Should().Contain("[Suggested Next Validation Actions]");
        }

        [Fact]
        public void BuildCoreControlReadinessReport_ContainsStartupRestoreAndValidationGuidance()
        {
            var config = DefaultConfiguration.Create();
            config.EnableStartupHardwareRestore = true;
            config.LastPerformanceModeName = "Performance";
            config.LastGpuPowerBoostLevel = "Maximum";
            config.LastCpuPl1Watts = 55;
            config.LastCpuPl2Watts = 80;
            config.LastTccOffset = 10;
            config.Undervolt.ApplyOnStartup = true;
            config.Undervolt.StartupPendingConfirmation = true;
            config.GpuOc = new GpuOcSettings
            {
                ApplyOnStartup = true,
                StartupPendingConfirmation = true
            };
            new ConfigurationService().Save(config);

            var svc = new DiagnosticExportService(_logging, _tempDir);
            var report = svc.BuildCoreControlReadinessReport();

            report.Should().Contain("StartupHardwareRestoreEnabled: yes");
            report.Should().Contain("StartupRestoreCategories: Fans=on; Performance=on; RGB=on; Tuning=on");
            report.Should().Contain("SavedPerformanceMode: Performance");
            report.Should().Contain("SavedGpuPowerBoostLevel: Maximum");
            report.Should().Contain("SavedCpuPL1: 55 W");
            report.Should().Contain("SavedCpuPL2: 80 W");
            report.Should().Contain("SavedTccOffset: 10 C");
            report.Should().Contain("UndervoltApplyOnStartup: yes");
            report.Should().Contain("UndervoltRecoveryRequired: yes");
            report.Should().Contain("GpuOcApplyOnStartup: yes");
            report.Should().Contain("GpuOcRecoveryRequired: yes");
            report.Should().Contain("RollbackBundleAvailable: yes");
            report.Should().Contain("RollbackTargets: performance=Balanced; fan=Auto; gpuPower=Minimum");
            report.Should().Contain("RollbackCpuPowerTarget: PL1=55 W; PL2=80 W");
            report.Should().Contain("[Hotkeys / OMEN Key]");
            report.Should().Contain("ConfigOmenKeyInterceptionEnabled:");
            report.Should().Contain("ValidationRule: press the physical OMEN key");
            report.Should().Contain("Startup restore: keep disabled until manual readback passes");
        }

        [Fact]
        public void BuildCoreControlReadinessReport_IncludesLastFanCommandGatesAndReadback()
        {
            var capabilities = new OmenCore.Hardware.DeviceCapabilities
            {
                ProductId = "878C",
                ModelName = "OMEN Laptop 15-ek0xxx",
                Chassis = OmenCore.Hardware.ChassisType.Laptop,
                ModelFamily = OmenCore.Hardware.OmenModelFamily.Legacy,
                IsKnownModel = true,
                ModelConfig = OmenCore.Hardware.ModelCapabilityDatabase.GetCapabilities("878C")
            };
            var fanService = new FanService(
                new ReadbackFanController(),
                new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl()),
                _logging,
                new NotificationService(_logging),
                1000,
                new ResumeRecoveryDiagnosticsService(),
                capabilities: capabilities);

            try
            {
                var telemetryField = typeof(FanService).GetField(
                    "_fanTelemetry",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var telemetry = telemetryField!.GetValue(fanService)
                    .Should().BeAssignableTo<System.Collections.ObjectModel.ObservableCollection<FanTelemetry>>()
                    .Subject;
                telemetry.Add(new FanTelemetry
                {
                    Name = "CPU Fan",
                    SpeedRpm = 1900,
                    DutyCyclePercent = 42,
                    RpmState = TelemetryDataState.Valid,
                    RpmSource = RpmSource.WmiBios
                });

                fanService.ForceSetFanSpeed(60);

                var svc = new DiagnosticExportService(_logging, _tempDir, fanService: fanService);
                var report = svc.BuildCoreControlReadinessReport(fanService: fanService);

                report.Should().Contain("LastCommandModel: OMEN Laptop 15-ek0xxx (878C)");
                report.Should().Contain("LastCommandGates: writes=yes; curves=yes; manual=yes; desktopBlocked=no");
                report.Should().Contain("LastCommandReadback: CPU Fan: 1900 RPM, duty 42%");
            }
            finally
            {
                fanService.Dispose();
            }
        }

        [Fact]
        public async Task LaunchReadinessFile_ContainsExpectedUnavailableSections()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "launch-readiness.txt");

            content.Should().Contain("3.8.1 LAUNCH READINESS SNAPSHOT");
            content.Should().Contain("[Fan Recovery]");
            content.Should().Contain("Fan service unavailable.");
            content.Should().Contain("[Performance Mode Apply Trace]");
            content.Should().Contain("Performance mode service unavailable.");
            content.Should().Contain("[CPU Temperature Authority]");
            content.Should().Contain("Monitoring service unavailable.");
            content.Should().Contain("[HP Keyboard RGB]");
            content.Should().Contain("Keyboard lighting service unavailable.");
            content.Should().Contain("[Hardware Worker Containment]");
            content.Should().Contain("AMD ADL quarantine status");
        }

        [Fact]
        public async Task ResourceFootprintFile_ContainsLightweightBaselineSections()
        {
            const string timerName = "Test_ResourceFootprint_Timer";
            BackgroundTimerRegistry.Register(
                timerName,
                "DiagnosticExportSnapshotTests",
                "resource footprint test timer",
                1234,
                BackgroundTimerTier.Optional);

            try
            {
                var svc = new DiagnosticExportService(_logging, _tempDir);
                var zipPath = await svc.CollectAndExportAsync();

                string content = ReadFileFromExport(zipPath, "resource-footprint.txt");

                content.Should().Contain("RESOURCE FOOTPRINT SNAPSHOT");
                content.Should().Contain("[OmenCore App]");
                content.Should().Contain("AverageCpuPercentSinceStart");
                content.Should().Contain("[Hardware Worker Processes]");
                content.Should().Contain("[Monitoring Cadence]");
                content.Should().Contain("Monitoring service unavailable");
                content.Should().Contain("[Fan Activity Blockers]");
                content.Should().Contain("Fan service unavailable");
                content.Should().Contain("[Background Timers]");
                content.Should().Contain(timerName);
                content.Should().Contain("[Managed Runtime]");
                content.Should().Contain("[Optional Subsystem Load Hints]");
            }
            finally
            {
                BackgroundTimerRegistry.Unregister(timerName);
            }
        }

        [Fact]
        public async Task RuntimePerformanceFile_IncludesFanTelemetryChurnCounters()
        {
            RuntimeUiPerformanceCounters.ResetForTests();
            RuntimeUiPerformanceCounters.RecordFanTelemetrySync(collectionResized: true, itemsUpdated: 2);
            RuntimeUiPerformanceCounters.RecordFanTelemetrySync(collectionResized: false, itemsUpdated: 2);

            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "runtime-performance.txt");

            content.Should().Contain("FanTelemetrySyncs: 2");
            content.Should().Contain("FanTelemetryCollectionResizes: 1");
            content.Should().Contain("FanTelemetryItemsUpdated: 4");
            content.Should().Contain("FanTelemetryPropertyOnlySyncs: 1");
            content.Should().Contain("FanTelemetryCollectionResizeRatio: 0.50");
            content.Should().Contain("FanTelemetryPropertyOnlySyncRatio: 0.50");
        }

        [Fact]
        public async Task MonitoringCadenceHoldFile_ContainsExpectedSections_WhenServicesUnavailable()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "monitoring-cadence-hold.txt");

            content.Should().Contain("MONITORING CADENCE + FAN HOLD SNAPSHOT",
                "header section marker must be present");
            content.Should().Contain("Monitoring service unavailable",
                "null HardwareMonitoringService must produce an explicit unavailability notice");
            content.Should().Contain("Fan service unavailable",
                "null FanService must produce an explicit unavailability notice");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        [Fact]
        public async Task RgbControlPathFile_ContainsExpectedSections_WhenServicesUnavailable()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "rgb-control-path.txt");

            content.Should().Contain("RGB CONTROL PATH SNAPSHOT");
            content.Should().Contain("[HP Keyboard]");
            content.Should().Contain("Keyboard lighting service unavailable.");
            content.Should().Contain("[External RGB Providers]");
            content.Should().Contain("RGB manager unavailable or not initialized yet.");
            content.Should().Contain("[Known HP/OMEN RGB Conflict Processes]");
        }

        [Fact]
        public async Task RgbControlPathFile_IncludesPersistedObservedSurface()
        {
            var configService = new ConfigurationService();
            var config = DefaultConfiguration.Create();
            config.KeyboardLighting.ObservedSurface = "Light bar changed";
            config.KeyboardLighting.ObservedProbeColorHex = "#00FF66";
            config.KeyboardLighting.ObservedBackend = "WMI BIOS";
            config.KeyboardLighting.ObservedApplyStatus = "Accepted/unverified";
            config.KeyboardLighting.ObservedAtUtc = new DateTime(2026, 6, 12, 10, 30, 0, DateTimeKind.Utc);
            configService.Save(config);

            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "rgb-control-path.txt");

            content.Should().Contain("[HP Keyboard Observed Surface]");
            content.Should().Contain("ObservedSurface: Light bar changed");
            content.Should().Contain("ObservedAtUtc: 2026-06-12T10:30:00.0000000Z");
            content.Should().Contain("ObservedProbeColor: #00FF66");
            content.Should().Contain("ObservedBackend: WMI BIOS");
            content.Should().Contain("ObservedApplyStatus: Accepted/unverified");
        }

        [Fact]
        public async Task TuningSafetyFile_ContainsSavedSafetyStateWithoutHardwareProviders()
        {
            var config = DefaultConfiguration.Create();
            config.EnableStartupHardwareRestore = true;
            config.AllowStartupRestoreOnOmen16OrVictus = false;
            config.LastPerformanceModeName = "Performance";
            config.LastGpuPowerBoostLevel = "Maximum";
            config.LastCpuPl1Watts = 55;
            config.LastCpuPl2Watts = 80;
            config.LastTccOffset = 10;
            config.Undervolt = new UndervoltPreferences
            {
                ApplyOnStartup = true,
                PendingTestApply = true,
                StartupPendingConfirmation = true,
                RespectExternalControllers = true,
                DefaultOffset = new UndervoltOffset { CoreMv = -42, CacheMv = -30 },
                EnablePerCoreUndervolt = true,
                PerCoreOffsetsMv = new int?[] { -15, null, -20 },
                LastConfirmedOffset = new UndervoltOffset { CoreMv = -35, CacheMv = -25 },
                LastConfirmedAtUtc = new DateTime(2026, 5, 8, 1, 2, 3, DateTimeKind.Utc)
            };
            config.GpuOc = new GpuOcSettings
            {
                ApplyOnStartup = true,
                PendingTestApply = true,
                StartupPendingConfirmation = true,
                CoreClockOffsetMHz = 100,
                MemoryClockOffsetMHz = 250,
                PowerLimitPercent = 110,
                VoltageOffsetMv = 5,
                LastConfirmedCoreClockOffsetMHz = 80,
                LastConfirmedMemoryClockOffsetMHz = 200,
                LastConfirmedPowerLimitPercent = 105,
                LastConfirmedVoltageOffsetMv = 0,
                LastConfirmedAtUtc = new DateTime(2026, 5, 8, 4, 5, 6, DateTimeKind.Utc)
            };
            config.LastGpuOcProfileName = "Gaming";
            config.AmdPowerLimits = new AmdPowerLimits { StapmLimitWatts = 20, TempLimitC = 95 };
            new ConfigurationService().Save(config);

            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "tuning-safety.txt");

            content.Should().Contain("TUNING SAFETY SNAPSHOT");
            content.Should().Contain("Purpose: Export saved tuning safety state without waking System Control");
            content.Should().Contain("EnableStartupHardwareRestore: yes");
            content.Should().Contain("StartupRestoreCategories: Fans=on; Performance=on; RGB=on; Tuning=on");
            content.Should().Contain("LastGpuPowerBoostLevel: Maximum");
            content.Should().Contain("LastCpuPl1Watts: 55 W");
            content.Should().Contain("RecoveryRequiredOnNextStartup: yes");
            content.Should().Contain("SavedDefaultOffset: Core -42 mV, Cache -30 mV");
            content.Should().Contain("SavedPerCoreOffsets: 2/3 active");
            content.Should().Contain("SavedCoreClockOffsetMHz: +100");
            content.Should().Contain("SavedPowerLimitPercent: 110%");
            content.Should().Contain("SelectedGpuOcProfile: Gaming");
            content.Should().Contain("StapmLimitWatts: 20 W");
        }

        private static string ReadFileFromExport(string exportPath, string fileName)
        {
            if (File.Exists(exportPath))
            {
                using var archive = ZipFile.OpenRead(exportPath);
                var entry = archive.Entries
                    .First(e => e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            else
            {
                return File.ReadAllText(Path.Combine(exportPath, fileName));
            }
        }

        /// <summary>
        /// Issue #129/#128/#130 + RC Diagnostics: Bounded performance snapshot must include
        /// amplification classification (Dispatcher and Projection) for triage
        /// so field analysts can quickly assess CPU dispatch overhead.
        /// </summary>
        [Fact]
        public async Task BoundedPerformanceFile_IncludesAmplificationClassification()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "runtime-performance-bounded.txt");

            content.Should().Contain("DispatcherAmplificationClass:", "dispatcher amplification classification required");
            content.Should().Contain("ProjectionAmplificationClass:", "projection amplification classification required");
            // Classes should use the current diagnostic vocabulary.
            (content.Contains("Excellent") ||
             content.Contains("Expected") ||
             content.Contains("Elevated") ||
             content.Contains("Critical"))
                .Should().BeTrue("amplification should have a classification value");
        }

        /// <summary>
        /// Issue #129/#128/#130 + RC Diagnostics: Bounded snapshot must include
        /// acceptance ratio classifications (Dashboard, General, Main) for acceptance quality assessment.
        /// </summary>
        [Fact]
        public async Task BoundedPerformanceFile_IncludesAcceptanceClassification()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "runtime-performance-bounded.txt");

            content.Should().Contain("MainProjectionAcceptanceClass:", "main acceptance classification required");
            content.Should().Contain("DashboardAcceptanceClass:", "dashboard acceptance classification required");
            content.Should().Contain("GeneralAcceptanceClass:", "general acceptance classification required");
        }

        /// <summary>
        /// Issue #129/#128/#130 + RC Diagnostics: Bounded snapshot must include
        /// cache-hit ratio classifications (Tray and Popup) for rendering optimization assessment.
        /// </summary>
        [Fact]
        public async Task BoundedPerformanceFile_IncludesCacheHitClassification()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "runtime-performance-bounded.txt");

            content.Should().Contain("TrayRenderCacheClass:", "tray cache hit classification required");
            content.Should().Contain("PopupRenderCacheClass:", "popup cache hit classification required");
            content.Should().Contain("TrayRenderCacheHitRatio:", "tray cache hit ratio required");
            content.Should().Contain("PopupRenderCacheHitRatio:", "popup cache hit ratio required");
            content.Should().Contain("FanTelemetryCollectionResizeRatio:", "fan telemetry collection resize ratio required");
            content.Should().Contain("FanTelemetryPropertyOnlySyncRatio:", "fan telemetry property-only sync ratio required");
        }

        /// <summary>
        /// Issue #129/#128/#130 + RC Diagnostics: Bounded snapshot must include
        /// scenario assessment to contextualize performance readings (e.g., "Minimized", "Focused", "Tray").
        /// </summary>
        [Fact]
        public async Task BoundedPerformanceFile_IncludesScenarioAssessment()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "runtime-performance-bounded.txt");

            content.Should().Contain("ScenarioAssessment:", "scenario assessment required for context");
            // The assessment should have some description (even if it's empty or "unknown")
            var assessmentLine = content.Split('\n').FirstOrDefault(l => l.Contains("ScenarioAssessment:"));
            assessmentLine.Should().NotBeNull();
        }

        /// <summary>
        /// Issue #129: MonitoringSource in the bounded snapshot must include CPU thermal authority
        /// when collected, so field diagnostics can correlate performance with authority state.
        /// </summary>
        [Fact]
        public async Task BoundedPerformanceFile_MonitoringSource_IncludesCpuAuthority()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "runtime-performance-bounded.txt");

            // MonitoringSource line should be present and contain CPU Authority info
            var monitoringLine = content.Split('\n').FirstOrDefault(l => l.Contains("MonitoringSource:"));
            monitoringLine.Should().NotBeNull("MonitoringSource must be in runtime state summary");
            if (!monitoringLine!.Contains("Unavailable", StringComparison.OrdinalIgnoreCase))
            {
                monitoringLine.Should().Contain("CPU Authority", "CPU thermal authority source must be included when monitoring is available");
            }
        }

        private sealed class EmptyBridge : OmenCore.Hardware.IHardwareMonitorBridge
        {
            public string MonitoringSource => "EmptyBridge";
            public Task<MonitoringSample> ReadSampleAsync(System.Threading.CancellationToken token)
                => Task.FromResult(new MonitoringSample());
            public Task<bool> TryRestartAsync() => Task.FromResult(true);
        }

        // GitHub #143-adjacent field finding (2026-07-26): system-info.txt reported
        // GC.GetTotalMemory() - the .NET managed heap, fluctuating 16-51 MB across real exports
        // on the same physical machine - mislabeled as "RAM", which is confusing in a section
        // titled "SYSTEM INFORMATION" and duplicates what resource-footprint.txt already reports
        // correctly under "ManagedMemoryMB". Fixed to report real installed physical memory.
        [Fact]
        public async Task SystemInfoFile_ReportsInstalledPhysicalMemory_NotManagedHeap()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "system-info.txt");

            content.Should().Contain("Installed RAM:", "system-info.txt must report real physical memory, not the GC managed heap");
            content.Should().NotMatchRegex(@"RAM: \d+ MB", "the old mislabeled GC-heap-as-RAM line must be gone");
        }

        // GitHub PERF-3810-001 (2026-07-26): hardware-info.txt and ec-state.txt were confirmed,
        // via 5 real diagnostics exports spanning v3.8.0-v4.0.0, to be byte-identical
        // placeholders in every single one - neither production call site (Settings "Export
        // Diagnostics" nor "Report Model") ever passed ecAccess/hwMonitor, and there was no
        // constructor-level fallback for either (unlike monitoringService/fanService/
        // wmiController, which all had one). These tests pin both states.
        [Fact]
        public async Task HardwareInfoFile_ShowsPlaceholder_WhenNoMonitoringServiceAvailable()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "hardware-info.txt");
            content.Should().Be("Hardware monitoring not available");
        }

        [Fact]
        public async Task HardwareInfoFile_ReportsRealSample_WhenMonitoringServiceHasOne()
        {
            // HardwareMonitoringService starts its own background monitor loop on construction
            // and implements IDisposable specifically to stop it - dispose explicitly so this
            // test doesn't leak a polling loop into the rest of the test process's lifetime.
            using var monitoringService = new HardwareMonitoringService(
                new EmptyBridge(), _logging, new MonitoringPreferences(), new ResumeRecoveryDiagnosticsService());

            // LastSample is only set by the running monitor loop; seed it directly via
            // reflection for a deterministic unit test, matching this codebase's established
            // pattern for exercising private state without running the full async loop.
            var field = typeof(HardwareMonitoringService).GetField("_lastSample",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.SetValue(monitoringService, new MonitoringSample
            {
                CpuTemperatureC = 62.5,
                GpuTemperatureC = 58.0,
                Fan1Rpm = 3200,
                Fan2Rpm = 3100,
                GpuName = "Test GPU"
            });

            var svc = new DiagnosticExportService(_logging, _tempDir, hardwareMonitoringService: monitoringService);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "hardware-info.txt");
            content.Should().Contain("62.5", "the real CPU temperature from LastSample must be reported");
            content.Should().Contain("3200", "the real fan RPM from LastSample must be reported");
            content.Should().NotBe("Hardware monitoring not available");
        }

        [Fact]
        public async Task EcStateFile_ShowsPlaceholder_WhenNoEcAccessAvailable()
        {
            var svc = new DiagnosticExportService(_logging, _tempDir);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "ec-state.txt");
            content.Should().Be("EC access not available");
        }

        // Real field diagnostics (2026-07-26, reviewed across multiple users' exports spanning
        // v3.8.0-v4.0.0) showed IsManualControlActive/CommandsIneffective/VerifyFailCount/
        // LastMaxModeExternalResetUtc/LastMaxModeExternalResetDetails as a byte-identical
        // "<unavailable>" placeholder in every single export, on every board - including board
        // 8A18, the exact board this session's #153 fix targeted, where this evidence would
        // have directly answered the still-open "is there a real external fan-reset actor"
        // question. Root cause: these fields were reflected off wmiController (HpWmiBios, the
        // raw WMI command layer), which never had them - they live on the fan controller
        // (WmiFanController/WmiFanControllerWrapper) instead. These tests pin that all four
        // collectors that show this section now read from fanService.Controller and no longer
        // show the broken-wiring "<unavailable>" placeholder.
        [Fact]
        public void BuildCoreControlReadinessReport_FanOwnershipFields_ComeFromFanControllerNotWmiController()
        {
            using var fanService = new FanService(
                new ReadbackFanController(),
                new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl()),
                _logging,
                new NotificationService(_logging),
                1000,
                new ResumeRecoveryDiagnosticsService());

            var svc = new DiagnosticExportService(_logging, _tempDir, fanService: fanService);
            var report = svc.BuildCoreControlReadinessReport(fanService: fanService);

            report.Should().NotContain("IsManualControlActive: <unavailable>",
                "the fix must read this from fanService.Controller, not the unrelated wmiController object");
            report.Should().Contain("IsManualControlActive: False");
            report.Should().Contain("CommandsIneffective: False");
            report.Should().Contain("VerifyFailCount: 0");
            report.Should().Contain("LastMaxModeExternalResetDetails: Not tracked by this backend.",
                "ReadbackFanController doesn't override this, so it must show the new IFanController default, not the old '<unavailable>' wiring-gap placeholder");
        }

        [Fact]
        public async Task MonitoringCadenceHoldFile_FanOwnershipFields_ComeFromFanControllerNotWmiController()
        {
            using var fanService = new FanService(
                new ReadbackFanController(),
                new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl()),
                _logging,
                new NotificationService(_logging),
                1000,
                new ResumeRecoveryDiagnosticsService());

            var svc = new DiagnosticExportService(_logging, _tempDir, fanService: fanService);
            var zipPath = await svc.CollectAndExportAsync();

            string content = ReadFileFromExport(zipPath, "monitoring-cadence-hold.txt");
            content.Should().NotContain("IsManualControlActive: <unavailable>");
            content.Should().Contain("LastMaxModeExternalResetDetails: Not tracked by this backend.");
        }

        [Fact]
        public async Task TuningFanFocusFile_FanOwnershipFields_ComeFromFanControllerNotWmiController()
        {
            using var fanService = new FanService(
                new ReadbackFanController(),
                new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl()),
                _logging,
                new NotificationService(_logging),
                1000,
                new ResumeRecoveryDiagnosticsService());

            // [WMI Fan Ownership] (where these fields live in this file) is gated behind a
            // non-null wmiController, same as production (HpWmiBios is always present there) -
            // ReadbackFanController conveniently already exposes IsAvailable/Status, so it also
            // works as the wmiController stub here.
            var svc = new DiagnosticExportService(_logging, _tempDir, fanService: fanService);
            var zipPath = await svc.CollectAndExportAsync(wmiController: new ReadbackFanController());

            string content = ReadFileFromExport(zipPath, "tuning-fan-focus.txt");
            content.Should().NotContain("IsManualControlActive: <unavailable>");
            content.Should().Contain("CommandsIneffective: False");
            content.Should().Contain("VerifyFailCount: 0");
        }
    }
}
