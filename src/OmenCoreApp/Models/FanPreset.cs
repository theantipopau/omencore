using System.Collections.Generic;

namespace OmenCore.Models
{
    public class FanPreset
    {
        public string Name { get; set; } = string.Empty;
        public List<FanCurvePoint> Curve { get; set; } = new();
        public bool IsBuiltIn { get; set; }
    }
}
