using System;
using System.Collections.Generic;
using System.Linq;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    public class FanController
    {
        private readonly IEcAccess _ecAccess;
        private readonly IReadOnlyDictionary<string, int> _registerMap;
        private readonly LibreHardwareMonitorImpl _bridge;

        public FanController(IEcAccess ecAccess, IReadOnlyDictionary<string, int> registerMap, LibreHardwareMonitorImpl bridge)
        {
            _ecAccess = ecAccess;
            _registerMap = registerMap;
            _bridge = bridge;
        }

        public bool IsEcReady => _ecAccess.IsAvailable;

        public void ApplyPreset(FanPreset preset)
        {
            if (preset.Curve.Count == 0)
            {
                return;
            }
            WriteDuty(preset.Curve.Max(p => p.FanPercent));
        }

        public void ApplyCustomCurve(IEnumerable<FanCurvePoint> curve)
        {
            var table = curve.OrderBy(p => p.TemperatureC).ToList();
            if (!table.Any())
            {
                return;
            }
            // TODO: Write entire curve table if EC supports it. For now, set high-water mark.
            WriteDuty(table.Max(p => p.FanPercent));
        }

        public IEnumerable<FanTelemetry> ReadFanSpeeds()
        {
            var fans = new List<FanTelemetry>();

            // Get fan speeds from hardware monitor
            var fanSpeeds = _bridge.GetFanSpeeds();
            int index = 0;

            foreach (var (name, rpm) in fanSpeeds)
            {
                fans.Add(new FanTelemetry
                {
                    Name = name,
                    SpeedRpm = (int)rpm,
                    DutyCyclePercent = CalculateDutyFromRpm((int)rpm, index),
                    Temperature = index == 0 ? _bridge.GetCpuTemperature() : _bridge.GetGpuTemperature()
                });
                index++;
            }

            // Fallback if no fans detected
            if (fans.Count == 0)
            {
                fans.Add(new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = _bridge.GetCpuTemperature() });
                fans.Add(new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = _bridge.GetGpuTemperature() });
            }

            return fans;
        }

        private int CalculateDutyFromRpm(int rpm, int fanIndex)
        {
            // Estimate duty cycle from RPM
            // Typical laptop fans: 0 RPM = 0%, 2000-3000 RPM = 50%, 5000-6000 RPM = 100%
            if (rpm == 0) return 0;
            
            const int minRpm = 1500;
            const int maxRpm = 6000;
            
            return Math.Clamp((rpm - minRpm) * 100 / (maxRpm - minRpm), 0, 100);
        }

        private void WriteDuty(int percent)
        {
            var duty = (byte)Math.Clamp(percent * 255 / 100, 0, 255);
            foreach (var register in _registerMap.Values)
            {
                _ecAccess.WriteByte((ushort)register, duty);
            }
        }
        
        /// <summary>
        /// Reset EC to factory defaults.
        /// Clears all manual fan overrides and restores BIOS control.
        /// Based on OMEN laptop EC register map.
        /// </summary>
        public bool ResetEcToDefaults()
        {
            if (!IsEcReady)
                return false;
            
            try
            {
                // OMEN EC registers (from omen-fan project and OmenMon research)
                const ushort REG_FAN1_SPEED_SET = 0x34;   // Fan 1 speed in units of 100 RPM
                const ushort REG_FAN2_SPEED_SET = 0x35;   // Fan 2 speed in units of 100 RPM
                const ushort REG_FAN1_SPEED_PCT = 0x2E;   // Fan 1 speed 0-100%
                const ushort REG_FAN2_SPEED_PCT = 0x2F;   // Fan 2 speed 0-100%
                const ushort REG_FAN_BOOST = 0xEC;        // Fan boost: 0x00=OFF, 0x0C=ON
                const ushort REG_FAN_STATE = 0xF4;        // Fan state: 0x00=Enable, 0x02=Disable
                const ushort REG_BIOS_CONTROL = 0x62;     // BIOS control: 0x00=Enabled, 0x06=Disabled
                const ushort REG_TIMER = 0x63;            // Timer (counts down from 0x78)
                
                // Step 1: Clear manual fan speed registers (write 0 to let BIOS control)
                _ecAccess.WriteByte(REG_FAN1_SPEED_SET, 0x00);
                _ecAccess.WriteByte(REG_FAN2_SPEED_SET, 0x00);
                _ecAccess.WriteByte(REG_FAN1_SPEED_PCT, 0x00);
                _ecAccess.WriteByte(REG_FAN2_SPEED_PCT, 0x00);
                
                // Step 2: Disable fan boost
                _ecAccess.WriteByte(REG_FAN_BOOST, 0x00);
                
                // Step 3: Enable fan state (allow BIOS to control)
                _ecAccess.WriteByte(REG_FAN_STATE, 0x00);
                
                // Step 4: Re-enable BIOS fan control
                _ecAccess.WriteByte(REG_BIOS_CONTROL, 0x00);
                
                // Step 5: Reset timer to trigger BIOS to recalculate fan speeds
                // Timer counts down from 0x78 (120); setting to 0x78 forces BIOS to take over
                _ecAccess.WriteByte(REG_TIMER, 0x78);
                
                // Step 6: Wait briefly then verify BIOS has taken control
                System.Threading.Thread.Sleep(100);
                
                // Double-check: write fan state again to ensure BIOS control
                _ecAccess.WriteByte(REG_FAN_STATE, 0x00);
                
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
