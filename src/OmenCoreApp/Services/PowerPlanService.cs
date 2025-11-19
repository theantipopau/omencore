using System;
using System.Runtime.InteropServices;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class PowerPlanService
    {
        private static readonly Guid ProcessorSubGroup = Guid.Parse("54533251-82be-4824-96C1-47B60B740D00");
        private static readonly Guid ProcessorBoost = Guid.Parse("BE337238-0D82-4146-A960-4F3749D470C7");
        private readonly LoggingService _logging;

        public PowerPlanService(LoggingService logging)
        {
            _logging = logging;
        }

        public void Apply(PerformanceMode mode)
        {
            if (Guid.TryParse(mode.LinkedPowerPlanGuid, out var plan))
            {
                var res = PowerSetActiveScheme(IntPtr.Zero, ref plan);
                if (res == 0)
                {
                    _logging.Info($"Power plan {plan} activated");
                }
                else
                {
                    _logging.Warn($"Failed to set power plan {plan}: {res}");
                }
            }

            var boostIndex = (uint)Math.Clamp(mode.CpuPowerLimitWatts / 20, 0, 5);
            var processorGroup = ProcessorSubGroup;
            var boostSetting = ProcessorBoost;
            var status = PowerWriteACValueIndex(IntPtr.Zero, IntPtr.Zero, ref processorGroup, ref boostSetting, boostIndex);
            if (status == 0)
            {
                processorGroup = ProcessorSubGroup;
                boostSetting = ProcessorBoost;
                PowerWriteDCValueIndex(IntPtr.Zero, IntPtr.Zero, ref processorGroup, ref boostSetting, boostIndex);
                _logging.Info($"Processor boost index -> {boostIndex}");
            }
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint valueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint valueIndex);
    }
}
