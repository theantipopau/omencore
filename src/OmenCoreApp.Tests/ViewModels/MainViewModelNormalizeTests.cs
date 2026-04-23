using System;
using System.Reflection;
using OmenCore.Models;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// T4 from REGRESSION_MATRIX.md — verifies that NormalizeMonitoringSample returns a new
    /// independent object and does not mutate the original sample (STEP-08).
    /// </summary>
    public class MainViewModelNormalizeTests
    {
        private static MonitoringSample? InvokeNormalize(MonitoringSample? sample, MonitoringSample? previous)
        {
            var method = typeof(MainViewModel).GetMethod(
                "NormalizeMonitoringSample",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            return (MonitoringSample?)method!.Invoke(null, new object?[] { sample, previous });
        }

        [Fact]
        public void NormalizeMonitoringSample_ReturnsNewInstance_NotInputReference()
        {
            var s1 = new MonitoringSample { CpuLoadPercent = 50 };

            var s2 = InvokeNormalize(s1, previous: null);

            Assert.NotNull(s2);
            Assert.NotSame(s1, s2);
        }

        [Fact]
        public void NormalizeMonitoringSample_DoesNotMutateOriginal_CpuLoadPercent()
        {
            // An out-of-range value will be clamped by the normalizer.
            // After normalization the original must remain unchanged (T4).
            var s1 = new MonitoringSample { CpuLoadPercent = 999 };

            var s2 = InvokeNormalize(s1, previous: null);

            // Normalized copy is clamped to 100
            Assert.Equal(100, s2!.CpuLoadPercent);
            // Original is untouched
            Assert.Equal(999, s1.CpuLoadPercent);
        }

        [Fact]
        public void NormalizeMonitoringSample_DoesNotMutateOriginal_GpuInactiveZeroing()
        {
            // When GpuTemperatureState is Inactive, the normalizer zeros out GPU metrics on
            // the copy. The original must not be affected.
            var s1 = new MonitoringSample
            {
                GpuTemperatureState = TelemetryDataState.Inactive,
                GpuLoadPercent      = 75,
                GpuPowerWatts       = 120,
                GpuClockMhz         = 1800,
                GpuMemoryClockMhz   = 9000,
            };

            var s2 = InvokeNormalize(s1, previous: null);

            // Normalized copy has zeroed GPU metrics
            Assert.Equal(0, s2!.GpuLoadPercent);
            Assert.Equal(0, s2.GpuPowerWatts);
            Assert.Equal(0, s2.GpuClockMhz);
            Assert.Equal(0, s2.GpuMemoryClockMhz);

            // Original is untouched
            Assert.Equal(75,   s1.GpuLoadPercent);
            Assert.Equal(120,  s1.GpuPowerWatts);
            Assert.Equal(1800, s1.GpuClockMhz);
            Assert.Equal(9000, s1.GpuMemoryClockMhz);
        }

        [Fact]
        public void NormalizeMonitoringSample_ReturnsNull_WhenSampleIsNull()
        {
            var result = InvokeNormalize(sample: null, previous: null);

            Assert.Null(result);
        }
    }
}
