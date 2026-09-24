using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Validates capability visibility gating for undervolt, RGB, and GPU power controls.
    /// Covers field-reported unsupported feature leakage on AMD models and driver-present/
    /// runtime-blocked scenarios (field evidence: 3.6.1-FIELD-EVIDENCE.md §J, cohort A 8BCD).
    /// </summary>
    public class DeviceCapabilitiesTests
    {
        #region Undervolt gating

        [Fact]
        public void ShowUndervolt_IsFalse_WhenRuntimeNotReady_EvenIfDriverCapabilityIsTrue()
        {
            var caps = new DeviceCapabilities
            {
                CanUndervolt = true,
                UndervoltRuntimeReady = false,
                UndervoltBlockReason = "MSR backend unavailable"
            };

            caps.ShowUndervolt.Should().BeFalse("runtime readiness must gate undervolt controls to avoid false actionable UI");
        }

        [Fact]
        public void ShowUndervolt_IsTrue_WhenCapabilityAndRuntimeAreReady()
        {
            var caps = new DeviceCapabilities
            {
                CanUndervolt = true,
                UndervoltRuntimeReady = true
            };

            caps.ShowUndervolt.Should().BeTrue();
        }

        [Fact]
        public void ShowUndervolt_IsFalse_WhenCapabilityIsFalse()
        {
            var caps = new DeviceCapabilities
            {
                CanUndervolt = false,
                UndervoltRuntimeReady = true
            };

            caps.ShowUndervolt.Should().BeFalse();
        }

        [Fact]
        public void ShowUndervolt_IsFalse_WhenModelConfigExplicitlyDisablesIt_EvenIfRuntimeIsReady()
        {
            // AMD models set SupportsUndervolt = false in ModelCapabilityDatabase.
            // The capability gate must respect this to prevent Intel-style MSR controls
            // from being shown on AMD laptops (field evidence: cohort A 8BCD AMD Ryzen).
            var amdModelConfig = new ModelCapabilities
            {
                SupportsUndervolt = false
            };

            var caps = new DeviceCapabilities
            {
                CanUndervolt = true,
                UndervoltRuntimeReady = true,
                ModelConfig = amdModelConfig
            };

            caps.ShowUndervolt.Should().BeFalse("AMD models must not show Intel-style undervolt controls");
        }

        [Fact]
        public void ShowUndervolt_BlockReason_IsPopulated_WhenRuntimeBlocked()
        {
            const string expectedReason = "PawnIO MSR write failed HRESULT 0x80070002";
            var caps = new DeviceCapabilities
            {
                CanUndervolt = true,
                UndervoltRuntimeReady = false,
                UndervoltBlockReason = expectedReason
            };

            caps.UndervoltBlockReason.Should().Be(expectedReason);
            caps.ShowUndervolt.Should().BeFalse();
        }

        #endregion

        #region RGB lighting gating

        [Fact]
        public void ShowRgbLighting_IsFalse_WhenNoRuntimeOrModelRgb()
        {
            var caps = new DeviceCapabilities
            {
                HasZoneLighting = false,
                HasPerKeyLighting = false,
                IsKnownModel = true,
                ModelConfig = new ModelCapabilities
                {
                    HasFourZoneRgb = false,
                    HasPerKeyRgb = false
                }
            };

            caps.ShowRgbLighting.Should().BeFalse("no RGB capability should hide lighting controls");
        }

        [Fact]
        public void ShowRgbLighting_IsTrue_WhenRuntimeDetectsZoneLighting()
        {
            var caps = new DeviceCapabilities
            {
                HasZoneLighting = true,
                HasPerKeyLighting = false
            };

            caps.ShowRgbLighting.Should().BeTrue();
        }

        [Fact]
        public void ShowRgbLighting_IsTrue_WhenKnownModelHasFourZoneRgb()
        {
            var caps = new DeviceCapabilities
            {
                HasZoneLighting = false,
                HasPerKeyLighting = false,
                IsKnownModel = true,
                ModelConfig = new ModelCapabilities
                {
                    HasFourZoneRgb = true,
                    HasPerKeyRgb = false
                }
            };

            caps.ShowRgbLighting.Should().BeTrue();
        }

        [Fact]
        public void ShowRgbLighting_IsFalse_WhenUnknownModelButModelConfigHasRgb()
        {
            // Unknown/unverified models should not show OMEN keyboard RGB controls
            // even if a model config entry suggests RGB — runtime detection must confirm.
            var caps = new DeviceCapabilities
            {
                HasZoneLighting = false,
                HasPerKeyLighting = false,
                IsKnownModel = false,
                ModelConfig = new ModelCapabilities
                {
                    HasFourZoneRgb = true
                }
            };

            caps.ShowRgbLighting.Should().BeFalse("unknown model must not show OMEN RGB controls without runtime confirmation");
        }

        #endregion

        #region GPU power boost gating

        [Fact]
        public void ShowGpuPowerBoost_IsTrue_WhenRuntimeDetectsGpuPowerControl()
        {
            var caps = new DeviceCapabilities
            {
                HasGpuPowerControl = true
            };

            caps.ShowGpuPowerBoost.Should().BeTrue();
        }

        [Fact]
        public void ShowGpuPowerBoost_IsTrue_WhenKnownModelSupportsIt()
        {
            var caps = new DeviceCapabilities
            {
                HasGpuPowerControl = true,
                IsKnownModel = true,
                ModelConfig = new ModelCapabilities
                {
                    SupportsGpuPowerBoost = true
                }
            };

            caps.ShowGpuPowerBoost.Should().BeTrue();
        }

        [Fact]
        public void ShowGpuPowerBoost_IsFalse_WhenKnownModelExplicitlyDisablesIt_EvenIfRuntimeDetectsControl()
        {
            var caps = new DeviceCapabilities
            {
                HasGpuPowerControl = true,
                IsKnownModel = true,
                ModelConfig = new ModelCapabilities
                {
                    SupportsGpuPowerBoost = false
                }
            };

            caps.ShowGpuPowerBoost.Should().BeFalse("an exact model denial must override a shared runtime probe");
        }
        [Fact]
        public void ShowGpuPowerBoost_IsFalse_WhenUnknownModelAndNoRuntimeDetection()
        {
            var caps = new DeviceCapabilities
            {
                HasGpuPowerControl = false,
                IsKnownModel = false,
                ModelConfig = new ModelCapabilities
                {
                    SupportsGpuPowerBoost = true
                }
            };

            caps.ShowGpuPowerBoost.Should().BeFalse("unknown model must not show GPU power boost without runtime confirmation");
        }

        #endregion

        #region Backend degradation classification

        [Fact]
        public void HasCriticalBackendDegradation_IsTrue_WhenTelemetryProviderMissing()
        {
            var caps = new DeviceCapabilities
            {
                BackendStatuses = new[]
                {
                    new BackendStatus
                    {
                        Name = "WMI",
                        Available = false,
                        Healthy = false,
                        Capabilities = new[]
                        {
                            BackendCapability.Telemetry,
                            BackendCapability.FanControl,
                            BackendCapability.PerformanceProfiles
                        }
                    }
                }
            };

            caps.HasCriticalBackendDegradation.Should().BeTrue();
            caps.BackendDegradationSummary.Should().StartWith("Critical degradation");
        }

        [Fact]
        public void HasOptionalBackendDegradation_IsTrue_WhenOnlyOptionalCapabilityMissing()
        {
            var caps = new DeviceCapabilities
            {
                BackendStatuses = new[]
                {
                    new BackendStatus
                    {
                        Name = "WMI",
                        Available = true,
                        Healthy = true,
                        Capabilities = new[]
                        {
                            BackendCapability.Telemetry,
                            BackendCapability.FanControl,
                            BackendCapability.PerformanceProfiles
                        }
                    },
                    new BackendStatus
                    {
                        Name = "PawnIO",
                        Available = false,
                        Healthy = false,
                        Capabilities = new[]
                        {
                            BackendCapability.ECAccess,
                            BackendCapability.Undervolt
                        }
                    }
                }
            };

            caps.HasCriticalBackendDegradation.Should().BeFalse();
            caps.HasOptionalBackendDegradation.Should().BeTrue();
            caps.BackendDegradationSummary.Should().StartWith("Optional degradation");
        }

        [Fact]
        public void BackendDegradationSummary_IsHealthy_WhenAllTrackedCapabilitiesPresent()
        {
            var caps = new DeviceCapabilities
            {
                BackendStatuses = new[]
                {
                    new BackendStatus
                    {
                        Name = "WMI",
                        Available = true,
                        Healthy = true,
                        Capabilities = new[]
                        {
                            BackendCapability.Telemetry,
                            BackendCapability.FanControl,
                            BackendCapability.PerformanceProfiles
                        }
                    },
                    new BackendStatus
                    {
                        Name = "PawnIO",
                        Available = true,
                        Healthy = true,
                        Capabilities = new[]
                        {
                            BackendCapability.ECAccess,
                            BackendCapability.Undervolt
                        }
                    }
                }
            };

            caps.HasCriticalBackendDegradation.Should().BeFalse();
            caps.HasOptionalBackendDegradation.Should().BeFalse();
            caps.BackendDegradationSummary.Should().Be("All tracked backend capabilities are healthy.");
        }

        #endregion

        #region Fan write safety gating

        [Fact]
        public void FanWritesBlockedForSafety_IsTrue_ForDesktopChassis()
        {
            var caps = new DeviceCapabilities
            {
                Chassis = ChassisType.Tower,
                CanSetFanSpeed = true,
                ModelConfig = new ModelCapabilities
                {
                    SupportsFanCurves = true
                }
            };

            caps.FanWritesBlockedForSafety.Should().BeTrue();
            caps.ShowFanCurveEditor.Should().BeFalse("desktop fan writes are blocked by the v3.6.3 safety gate");
        }

        [Fact]
        public void FanWritesBlockedForSafety_IsTrue_ForDesktopModelFamily()
        {
            var caps = new DeviceCapabilities
            {
                Chassis = ChassisType.Unknown,
                ModelFamily = OmenModelFamily.Desktop,
                CanSetFanSpeed = true,
                ModelConfig = new ModelCapabilities
                {
                    SupportsFanCurves = true
                }
            };

            caps.FanWritesBlockedForSafety.Should().BeTrue();
            caps.ShowFanCurveEditor.Should().BeFalse();
        }

        [Fact]
        public void ShowFanCurveEditor_RemainsTrue_ForWritableLaptopProfile()
        {
            var caps = new DeviceCapabilities
            {
                Chassis = ChassisType.Laptop,
                ModelFamily = OmenModelFamily.Legacy,
                CanSetFanSpeed = true,
                ModelConfig = new ModelCapabilities
                {
                    SupportsFanCurves = true
                }
            };

            caps.FanWritesBlockedForSafety.Should().BeFalse();
            caps.ShowFanCurveEditor.Should().BeTrue();
        }

        #endregion
    }
}
