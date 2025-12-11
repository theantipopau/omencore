using System.Collections.Generic;
using OmenCore.Models;

namespace OmenCore.Corsair
{
    public class CorsairLightingPreset
    {
        public string Name { get; set; } = string.Empty;
        public LightingEffectType Effect { get; set; } = LightingEffectType.Static;
        public string PrimaryColor { get; set; } = "#FFFFFF";
        public string SecondaryColor { get; set; } = "#0000FF";
        public string ColorHex { get; set; } = "#FFFFFF"; // Alias for PrimaryColor
        public double Speed { get; set; } = 1.0;
        public List<string> TargetZones { get; set; } = new();
    }
}
