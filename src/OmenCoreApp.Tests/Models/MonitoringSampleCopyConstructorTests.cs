using System.Collections.Generic;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Models
{
    public class MonitoringSampleCopyConstructorTests
    {
        [Fact]
        public void CopyConstructor_CopiesScalarProperties()
        {
            var s1 = new MonitoringSample
            {
                CpuLoadPercent         = 42,
                CpuTemperatureC        = 75.5,
                GpuTemperatureC        = 68,
                Fan1Rpm                = 3200,
                Fan2Rpm                = 2900,
                BatteryChargePercent   = 87,
                IsOnAcPower            = true,
                IsCpuThermalThrottling = true,
                GpuName                = "RTX 5060",
                BatteryTimeRemaining   = "2:30",
                CpuTemperatureState    = TelemetryDataState.Valid,
            };

            var s2 = new MonitoringSample(s1);

            Assert.Equal(42,                      s2.CpuLoadPercent);
            Assert.Equal(75.5,                    s2.CpuTemperatureC);
            Assert.Equal(68,                      s2.GpuTemperatureC);
            Assert.Equal(3200,                    s2.Fan1Rpm);
            Assert.Equal(2900,                    s2.Fan2Rpm);
            Assert.Equal(87,                      s2.BatteryChargePercent);
            Assert.True(s2.IsOnAcPower);
            Assert.True(s2.IsCpuThermalThrottling);
            Assert.Equal("RTX 5060",              s2.GpuName);
            Assert.Equal("2:30",                  s2.BatteryTimeRemaining);
            Assert.Equal(TelemetryDataState.Valid, s2.CpuTemperatureState);
        }

        [Fact]
        public void CopyConstructor_ProducesIndependentInstance()
        {
            var s1 = new MonitoringSample { CpuLoadPercent = 42 };

            var s2 = new MonitoringSample(s1);

            Assert.NotSame(s1, s2);
        }

        [Fact]
        public void CopyConstructor_CpuCoreClocksMhz_IsDeepCopied()
        {
            var s1 = new MonitoringSample { CpuCoreClocksMhz = new List<double> { 3200, 3400 } };

            var s2 = new MonitoringSample(s1);

            // Mutating s1's list must not affect s2
            s1.CpuCoreClocksMhz.Add(9999);

            Assert.Equal(2, s2.CpuCoreClocksMhz.Count);
            Assert.NotSame(s1.CpuCoreClocksMhz, s2.CpuCoreClocksMhz);
        }

        [Fact]
        public void CopyConstructor_MutatingCopy_DoesNotAffectOriginal()
        {
            var s1 = new MonitoringSample { CpuLoadPercent = 42 };

            var s2 = new MonitoringSample(s1);
            s2.CpuLoadPercent = 99;

            Assert.Equal(42, s1.CpuLoadPercent);
        }
    }
}
