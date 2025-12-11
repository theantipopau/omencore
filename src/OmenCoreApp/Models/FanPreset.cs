using System.Collections.Generic;

namespace OmenCore.Models
{
    public class FanPreset
    {
        public string Name { get; set; } = string.Empty;
        public FanMode Mode { get; set; } = FanMode.Auto; // Default for backward compatibility
        public List<FanCurvePoint> Curve { get; set; } = new();
        public bool IsBuiltIn { get; set; }
        
        public override string ToString() => Name;
    }
}
