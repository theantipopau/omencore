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

        public FanController(IEcAccess ecAccess, IReadOnlyDictionary<string, int> registerMap)
        {
            _ecAccess = ecAccess;
            _registerMap = registerMap;
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
