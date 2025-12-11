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
    }
}
