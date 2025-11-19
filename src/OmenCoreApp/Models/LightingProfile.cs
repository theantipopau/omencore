using System.Collections.Generic;

namespace OmenCore.Models
{
    public class LightingProfile
    {
        public string Name { get; set; } = string.Empty;
        public LightingEffectType Effect { get; set; } = LightingEffectType.Static;
        public List<string> Zones { get; set; } = new();
        public string PrimaryColor { get; set; } = "#FF0000";
        public string SecondaryColor { get; set; } = "#0000FF";
        public double Speed { get; set; } = 1.0;
    }
}
